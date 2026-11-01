using System.Diagnostics;
using Hibiki.Core.Models;

namespace Hibiki.Core.Services;

public sealed class DesktopCommandRunner : ICommandRunner
{
    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        // Allow explicit overrides for developers, but use the verified tools shipped
        // alongside the Windows executable without requiring global installations.
        var overridePath = Environment.GetEnvironmentVariable("HIBIKI_YTDLP");
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
        var executable = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : File.Exists(bundled) ? bundled : "yt-dlp";

        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        var deno = Path.Combine(AppContext.BaseDirectory, "tools", "deno.exe");
        if (File.Exists(deno))
        {
            info.ArgumentList.Add("--js-runtimes");
            info.ArgumentList.Add("deno:" + deno);
        }

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(
                "O extrator YT-DLP não está disponível. Baixe novamente o pacote Windows completo do Hibiki e extraia todos os arquivos.",
                ex);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, await output, await error);
    }
}
