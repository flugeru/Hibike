using System.IO.Compression;
using Android.Content;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.Android;

public sealed class AndroidCommandRunner(Context context) : ICommandRunner
{
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private string? _pythonHome;
    private string? _script;
    private string? _workerScript;
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
            // Refresh on process initialization so app updates cannot keep an older extractor.
            using (var input = context.Assets!.Open("runtime/yt-dlp"))
            using (var output = File.Create(script + ".tmp")) await input.CopyToAsync(output, token);
            File.Move(script + ".tmp", script, true);
            var host = Path.Combine(baseDir, "ytdlp-worker.py");
            using (var input = context.Assets!.Open("ytdlp-worker.py"))
            using (var output = File.Create(host + ".tmp")) await input.CopyToAsync(output, token);
            File.Move(host + ".tmp", host, true);
            _workerScript = host;
            _script = script;
            _pythonHome = Path.Combine(python, "usr");
        }
        finally { _initialization.Release(); }
    }

    // Starting Python and importing yt-dlp is paid once per worker instead of on every search and
    // track. Two workers cover a resolve running alongside a search or prefetch; anything beyond
    // that, and any worker failure, uses a separate process exactly as before.
    private const int MaxWorkers = 2;
    private static readonly TimeSpan WorkerIdleLifetime = TimeSpan.FromMinutes(5);
    private readonly object _workersGate = new();
    private readonly List<YtDlpWorker> _workers = [];
    private Timer? _workerReaper;

    private string[] Prefix(string script) => [Path.Combine(_native, "libpython.so"), script];
    private string[] CommonArguments => ["--verbose", "--js-runtimes", "quickjs:" + Path.Combine(_native, "libqjs.so")];

    /// <summary>Prepares the runtime and one warm worker in the background, before the first request.</summary>
    public void Prewarm() => _ = Task.Run(async () =>
    {
        try
        {
            await InitializeAsync(CancellationToken.None);
            var worker = AcquireWorker();
            if (worker is not null) { await worker.StartAsync(CancellationToken.None); ReleaseWorker(worker); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { PlaybackDiagnostics.Write("YT-DLP", "Pré-carregamento indisponível: " + error.GetType().Name); }
    });

    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (AcquireWorker() is { } worker)
        {
            try
            {
                var result = await worker.RunAsync([.. CommonArguments, .. arguments], cancellationToken);
                foreach (var line in result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    PlaybackDiagnostics.Write("YT-DLP", line.TrimEnd('\r'));
                return result with { Error = result.Error.Length <= 24000 ? result.Error : result.Error[..24000] };
            }
            catch (OperationCanceledException)
            {
                PlaybackDiagnostics.Write("YT-DLP", "Processo interrompido pelo cancelamento/limite de tempo.");
                throw;
            }
            catch (Exception error) when (error is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
            {
                PlaybackDiagnostics.Write("YT-DLP", "Worker reiniciado; usando processo separado. " + error.GetType().Name);
            }
            finally { ReleaseWorker(worker); }
        }
        return await RunOnceAsync(arguments, cancellationToken);
    }

    private YtDlpWorker? AcquireWorker()
    {
        lock (_workersGate)
        {
            foreach (var dead in _workers.Where(worker => worker.IsDead).ToArray()) { _workers.Remove(dead); dead.Dispose(); }
            if (_workers.FirstOrDefault(worker => !worker.Busy) is { } idle) { idle.Busy = true; return idle; }
            if (_workers.Count >= MaxWorkers) return null;
            var created = new YtDlpWorker(CreateBuilder([.. Prefix(_workerScript!), _script!])) { Busy = true };
            _workers.Add(created);
            _workerReaper ??= new Timer(_ => ReapIdleWorkers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            return created;
        }
    }

    private void ReleaseWorker(YtDlpWorker worker)
    {
        lock (_workersGate)
        {
            worker.Busy = false;
            worker.LastUsed = DateTime.UtcNow;
            if (worker.IsDead) { _workers.Remove(worker); worker.Dispose(); }
        }
    }

    // A resident interpreter holds tens of megabytes; let it go when the app stops asking.
    private void ReapIdleWorkers()
    {
        lock (_workersGate)
            foreach (var worker in _workers.Where(worker => !worker.Busy && DateTime.UtcNow - worker.LastUsed > WorkerIdleLifetime).ToArray())
            { _workers.Remove(worker); worker.Dispose(); }
    }

    private Java.Lang.ProcessBuilder CreateBuilder(IEnumerable<string> command)
    {
        var builder = new Java.Lang.ProcessBuilder(command.ToArray());
        var env = builder.Environment()!;
        env["PYTHONHOME"] = _pythonHome!;
        env["HOME"] = context.FilesDir!.AbsolutePath;
        env["LD_LIBRARY_PATH"] = Path.Combine(_pythonHome!, "lib") + ":" + _native;
        env["SSL_CERT_FILE"] = Path.Combine(_pythonHome!, "etc/tls/cert.pem");
        env["TMPDIR"] = context.CacheDir!.AbsolutePath;
        return builder;
    }

    private Task<ProcessResult> RunOnceAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => Task.Run(async () =>
        {
            using var builder = CreateBuilder([.. Prefix(_script!), .. CommonArguments, .. arguments]);
            using var process = builder.Start()!;
            using var stdout = new StreamReader(process.InputStream!);
            using var stderr = new StreamReader(process.ErrorStream!);
            var output = stdout.ReadToEndAsync();
            var error = Task.Run(async () =>
            {
                var lines = new System.Text.StringBuilder();
                while (await stderr.ReadLineAsync() is { } line)
                {
                    PlaybackDiagnostics.Write("YT-DLP", line);
                    if (lines.Length < 24000) lines.AppendLine(line);
                }
                return lines.ToString();
            });
            try
            {
                var exit = await Task.Run(() => process.WaitFor(), CancellationToken.None).WaitAsync(cancellationToken);
                return new ProcessResult(exit, await output, await error);
            }
            catch (OperationCanceledException)
            {
                PlaybackDiagnostics.Write("YT-DLP", "Processo interrompido pelo cancelamento/limite de tempo.");
                Stop(process);
                await Task.Run(() => process.WaitFor(), CancellationToken.None);
                await Task.WhenAll(output, error);
                throw;
            }
        }, cancellationToken);

    private static void Stop(Java.Lang.Process process)
    {
        process.Destroy();
        // destroyForcibly() is API 26+; on Android 7 it threw NoSuchMethodError and
        // replaced the cancellation with an unrelated failure, leaving the child unreaped.
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) process.DestroyForcibly();
    }

    /// <summary>One resident Python process running ytdlp-worker.py; used by one request at a time.</summary>
    private sealed class YtDlpWorker(Java.Lang.ProcessBuilder builder)
    {
        private Java.Lang.Process? _process;
        private Stream? _input;
        private StreamReader? _output;
        private Task? _started;
        private volatile bool _dead;
        public bool Busy { get; set; }
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public bool IsDead => _dead;

        public Task StartAsync(CancellationToken token) => (_started ??= Task.Run(async () =>
        {
            _process = builder.Start()!;
            builder.Dispose();
            _input = _process.OutputStream!;
            _output = new StreamReader(_process.InputStream!, System.Text.Encoding.UTF8);
            var errors = new StreamReader(_process.ErrorStream!);
            // Interpreter-level output outside a request (crashes, warnings) must never fill the pipe.
            _ = Task.Run(async () =>
            {
                try { while (await errors.ReadLineAsync() is { } line) PlaybackDiagnostics.Write("YT-DLP", line); }
                catch (Exception) { }
                finally { errors.Dispose(); }
            });
            var ready = await _output.ReadLineAsync() ?? throw new IOException("Worker do YT-DLP encerrou ao iniciar.");
            using var document = System.Text.Json.JsonDocument.Parse(ready);
            if (!document.RootElement.TryGetProperty("ready", out var flag) || !flag.GetBoolean())
                throw new IOException("Worker do YT-DLP não ficou pronto.");
        })).WaitAsync(token);

        public async Task<ProcessResult> RunAsync(string[] arguments, CancellationToken token)
        {
            try
            {
                await StartAsync(token);
                var request = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { args = arguments });
                await Task.Run(() =>
                {
                    _input!.Write(request);
                    _input.WriteByte((byte)'\n');
                    _input.Flush();
                }, token);
                var line = await Task.Run(() => _output!.ReadLine(), CancellationToken.None).WaitAsync(token)
                    ?? throw new IOException("Worker do YT-DLP encerrou durante a solicitação.");
                using var document = System.Text.Json.JsonDocument.Parse(line);
                var root = document.RootElement;
                return new ProcessResult(root.GetProperty("exit").GetInt32(),
                    root.GetProperty("stdout").GetString() ?? "", root.GetProperty("stderr").GetString() ?? "");
            }
            catch
            {
                // Cancellation or a broken pipe leaves the worker mid-request; it is never reused.
                _dead = true;
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _dead = true;
            if (_process is { } process)
            {
                try { Stop(process); } catch (Java.Lang.Exception) { }
                try { process.Dispose(); } catch (Exception) { }
            }
            _process = null;
        }
    }
}
