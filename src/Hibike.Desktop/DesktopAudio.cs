using Hibike.App;
using Hibike.Core.Services;

namespace Hibike.Desktop;

internal static class DesktopAudio
{
    internal static IAudioPlayer Configure(string directory)
    {
        var path = Path.Combine(directory, "audio-engine.txt");
        var selected = OperatingSystem.IsWindows() ? "libmpv" : "VLC";
        try { if (File.Exists(path)) selected = File.ReadAllText(path).Trim(); }
        catch (IOException e) { PlaybackDiagnostics.Write("Motor de áudio", e.Message); }
        var choices = OperatingSystem.IsWindows() ? new[] { "libmpv", "VLC" } : new[] { "VLC" };
        if (!choices.Contains(selected)) { PlaybackDiagnostics.Write("Motor de áudio", "Preferência inválida; usando padrão da plataforma."); selected = choices[0]; }
        AppServices.AudioEngines = choices;
        AppServices.SelectedAudioEngine = selected;
        AppServices.AudioEngineAvailable = choices.Contains;
        AppServices.SaveAudioEngine = name =>
        {
            if (!choices.Contains(name) || !AppServices.AudioEngineAvailable(name)) throw new InvalidOperationException("Motor indisponível.");
            Directory.CreateDirectory(directory);
            var temp = path + ".tmp"; File.WriteAllText(temp, name); File.Move(temp, path, true);
            AppServices.SelectedAudioEngine = name;
        };
        return Create(selected);
    }
    internal static IAudioPlayer Create(string name) => name switch
    {
        "libmpv" => new MpvAudioPlayer(),
        "VLC" => new VlcAudioPlayer(),
        _ => throw new PlatformNotSupportedException("Motor não disponível nesta plataforma: " + name)
    };
}
