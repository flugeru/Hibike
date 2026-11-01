using System.IO.Compression;
using Android.Content;
using Hibiki.Core.Models;
using Hibiki.Core.Services;

namespace Hibiki.Android;

public sealed class AndroidCommandRunner(Context context) : ICommandRunner
{
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private string? _pythonHome;
    private string? _script;
    private readonly string _native = context.ApplicationInfo!.NativeLibraryDir!;

    private async Task InitializeAsync(CancellationToken token)
    {
        await _initialization.WaitAsync(token);
        try
        {
            if (_pythonHome is not null) return;
            var abi = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64-v8a",
                System.Runtime.InteropServices.Architecture.X64 => "x86_64",
                _ => throw new PlatformNotSupportedException("Arquitetura de processo não suportada pelo runtime de áudio.")
            };
            var baseDir = Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "runtime-v2-" + abi);
            var python = Path.Combine(baseDir, "python");
            var marker = Path.Combine(python, ".ready");
            if (!File.Exists(marker))
            {
                if (Directory.Exists(python)) Directory.Delete(python, true);
                Directory.CreateDirectory(python);
                using var asset = context.Assets!.Open($"runtime/{abi}/python.zip");
                // ZipArchive needs a seekable stream on Android.
                using var copy = new MemoryStream();
                await asset.CopyToAsync(copy, token);
                copy.Position = 0;
                using var zip = new ZipArchive(copy, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    var target = Path.GetFullPath(Path.Combine(python, entry.FullName));
                    if (!target.StartsWith(python + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new IOException("Caminho inválido no runtime Python.");
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    // Preserve Unix symlinks used by Python's shared libraries.
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    {
                        using var reader = new StreamReader(entry.Open());
                        var link = await reader.ReadToEndAsync(token);
                        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, link));
                        if (!resolved.StartsWith(python + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                            throw new IOException("Link inválido no runtime Python.");
                        File.CreateSymbolicLink(target, link);
                    }
                    else
                    {
                        using var input = entry.Open();
                        using var output = File.Create(target);
                        await input.CopyToAsync(output, token);
                    }
                }
                await File.WriteAllTextAsync(marker, "1", token);
            }
            var script = Path.Combine(baseDir, "yt-dlp");
            if (!File.Exists(script))
            {
                using var input = context.Assets!.Open("runtime/yt-dlp");
                using (var output = File.Create(script + ".tmp")) await input.CopyToAsync(output, token);
                File.Move(script + ".tmp", script, true);
            }
            _script = script;
            _pythonHome = Path.Combine(python, "usr");
        }
        finally { _initialization.Release(); }
    }

    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        return await Task.Run(async () =>
        {
            var command = new List<string> { Path.Combine(_native, "libpython.so"), _script!,
                "--verbose", "--js-runtimes", "quickjs:" + Path.Combine(_native, "libqjs.so") };
            command.AddRange(arguments);
            using var builder = new Java.Lang.ProcessBuilder(command.ToArray());
            var env = builder.Environment()!;
            env["PYTHONHOME"] = _pythonHome!;
            env["HOME"] = context.FilesDir!.AbsolutePath;
            env["LD_LIBRARY_PATH"] = Path.Combine(_pythonHome!, "lib") + ":" + _native;
            env["SSL_CERT_FILE"] = Path.Combine(_pythonHome!, "etc/tls/cert.pem");
            env["TMPDIR"] = context.CacheDir!.AbsolutePath;
            using var process = builder.Start()!;
            using var cancel = cancellationToken.Register(() =>
            {
                PlaybackDiagnostics.Write("YT-DLP", "Processo interrompido pelo cancelamento/limite de tempo.");
                process.Destroy();
            });
            using var stdout = new StreamReader(process.InputStream!);
            using var stderr = new StreamReader(process.ErrorStream!);
            var output = stdout.ReadToEndAsync(cancellationToken);
            var error = Task.Run(async () =>
            {
                var lines = new System.Text.StringBuilder();
                while (await stderr.ReadLineAsync(cancellationToken) is { } line)
                {
                    PlaybackDiagnostics.Write("YT-DLP", line);
                    if (lines.Length < 24000) lines.AppendLine(line);
                }
                return lines.ToString();
            }, cancellationToken);
            var exit = await Task.Run(() => process.WaitFor(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(exit, await output, await error);
        }, cancellationToken);
    }
}
