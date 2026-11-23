using System.Globalization;
using Hibike.Core.Services;

namespace Hibike.App;

public sealed partial class MusicController
{
    // Slider drags change volume many times per second; only the latest value reaches the disk,
    // and never from the UI thread.
    private readonly LatestSnapshotWriter<string> _volumeWriter;

    public double Volume
    {
        get => Player.Volume;
        set
        {
            if (!double.IsFinite(value)) return;
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(Player.Volume - value) < .000001) return;
            Player.Volume = value;
            _volumeWriter.Enqueue(value.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private LatestSnapshotWriter<string> CreateVolumeWriter() => new(WriteVolume, error =>
    {
        PlaybackDiagnostics.Write("Salvar volume", error.Message);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SetStatus("Não foi possível salvar volume: " + error.Message));
    });

    private void WriteVolume(string value)
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, "volume.txt");
        File.WriteAllText(path + ".tmp", value);
        File.Move(path + ".tmp", path, true);
    }

    private void LoadVolume()
    {
        try
        {
            var path = Path.Combine(DataDirectory, "volume.txt");
            if (File.Exists(path) && double.TryParse(File.ReadAllText(path), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value is >= 0 and <= 1)
                Player.Volume = value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatus("Não foi possível restaurar volume: " + e.Message); }
    }
}
