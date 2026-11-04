using System.Globalization;

namespace Hibike.App;

public sealed partial class MusicController
{
    public double Volume
    {
        get => Player.Volume;
        set
        {
            if (!double.IsFinite(value)) return;
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(Player.Volume - value) < .000001) return;
            Player.Volume = value;
            try
            {
                Directory.CreateDirectory(DataDirectory);
                var path = Path.Combine(DataDirectory, "volume.txt");
                File.WriteAllText(path + ".tmp", value.ToString("R", CultureInfo.InvariantCulture));
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatus("Não foi possível salvar volume: " + e.Message); }
        }
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
