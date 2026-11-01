using System.Diagnostics;
using Hibiki.Core.Models;
namespace Hibiki.Core.Services;

public sealed class DesktopCommandRunner : ICommandRunner
{
    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("HIBIKI_YTDLP") ?? (File.Exists(bundled) ? bundled : "yt-dlp"))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex)
        { throw new InvalidOperationException("Instale yt-dlp ou configure HIBIKI_YTDLP com o caminho do executável.", ex); }
        using var registration = cancellationToken.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new(process.ExitCode, await output, await error);
    }
}
