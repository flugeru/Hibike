using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Hibike.Core.Models;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private static TextBlock WrappedText(string value, double size, string color)
    {
        var text = Text(value, size, color);
        text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        return text;
    }

    private void ShowLibraryTools()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(WrappedText("O backup inclui preferências, playlists e referências online. Guarde o áudio local separadamente.", UiTheme.Caption, Muted));
        panel.Children.Add(ActionButton("Exportar backup", async () =>
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage?.CanSave != true) return;
            using var file = await storage.SaveFilePickerAsync(new() { Title = "Exportar backup", SuggestedFileName = "hibike-backup.json", DefaultExtension = "json" });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            await _music.ExportLibraryAsync(stream);
            CloseSheet();
        }));
        panel.Children.Add(ActionButton("Importar backup", async () =>
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage?.CanOpen != true) return;
            var files = await storage.OpenFilePickerAsync(new() { Title = "Importar backup Hibike", FileTypeFilter = [new("Backup Hibike") { Patterns = ["*.json"], MimeTypes = ["application/json"] }] });
            using var file = files.FirstOrDefault();
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            await _music.ImportLibraryAsync(stream);
            CloseSheet();
        }));
        panel.Children.Add(ActionButton("Downloads", () => { ShowDownloads(); return Task.CompletedTask; }));
        panel.Children.Add(ActionButton("Selecionar músicas para playlist", () => { ShowBatchPlaylist(); return Task.CompletedTask; }));
        ShowSheet("Biblioteca e backup", panel);
    }

    private void ShowAudioSettings()
    {
        var panel = new StackPanel { Spacing = 8 };
        var normalize = new CheckBox { Content = "Normalizar volume quando houver metadados", IsChecked = _music.NormalizeAudio };
        normalize.IsCheckedChanged += (_, _) => _music.NormalizeAudio = normalize.IsChecked == true;
        panel.Children.Add(normalize);
        panel.Children.Add(Text("Equalizador", UiTheme.Body));
        var presets = new[] { "Desligado", "Graves", "Voz", "Agudos" };
        var equalizer = new ComboBox { ItemsSource = presets, SelectedIndex = (int)_music.Equalizer };
        equalizer.SelectionChanged += (_, _) =>
        {
            if (equalizer.SelectedIndex >= 0) _music.Equalizer = (EqualizerPreset)equalizer.SelectedIndex;
        };
        panel.Children.Add(equalizer);
        panel.Children.Add(WrappedText("Os ajustes valem a partir da próxima faixa. O equalizador depende dos efeitos de áudio oferecidos pelo aparelho.", UiTheme.Caption, Muted));
        ShowSheet("Ajustes de áudio", panel);
    }

    private void ShowBatchPlaylist()
    {
        var panel = new StackPanel { Spacing = 8 };
        var selection = new ListBox { ItemsSource = _music.Favorites, SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            MaxHeight = Math.Max(120, Bounds.Height * .45),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Track>((track, _) => Text(track?.Title ?? "", UiTheme.Body)) };
        panel.Children.Add(selection);
        var name = UiTheme.Field("Nome da playlist");
        panel.Children.Add(name);
        panel.Children.Add(ActionButton("Criar playlist com a seleção", () =>
        {
            var tracks = selection.SelectedItems?.Cast<Track>().ToArray() ?? [];
            if (tracks.Length == 0) throw new InvalidOperationException("Selecione ao menos uma música.");
            _music.CreatePlaylist(name.Text ?? "", tracks);
            CloseSheet();
            return Task.CompletedTask;
        }));
        ShowSheet("Selecionar faixas", panel);
    }

    private async Task ExportPlaylistAsync(MusicController.LocalPlaylist playlist)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage?.CanSave != true) return;
        using var file = await storage.SaveFilePickerAsync(new() { Title = "Exportar playlist", SuggestedFileName = "playlist.m3u8", DefaultExtension = "m3u8" });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("#EXTM3U");
        foreach (var track in playlist.Tracks)
        {
            if (track.IsLocal || !Uri.TryCreate(track.Source, UriKind.Absolute, out var url) || url.Scheme != "https") continue;
            var label = (track.Artist + " - " + track.Title).Replace('\r', ' ').Replace('\n', ' ');
            await writer.WriteLineAsync($"#EXTINF:{(int)track.DurationSeconds},{label}");
            await writer.WriteLineAsync(url.AbsoluteUri);
        }
        _music.SetStatus("Playlist exportada. Arquivos locais ficaram de fora.");
    }

    private void ShowDownloads()
    {
        var panel = new StackPanel { Spacing = 8 };
        var tracks = _music.OfflineTracks;
        panel.Children.Add(WrappedText(tracks.Count == 0 ? "Sem downloads. Use o menu da faixa para baixar." : "Músicas salvas neste aparelho · limite de 1 GB.", UiTheme.Caption, Muted));
        if (_music.DownloadingOffline) panel.Children.Add(ActionButton("Cancelar download atual", () => { _music.CancelOfflineDownload(); return Task.CompletedTask; }));
        if (tracks.Count > 0) panel.Children.Add(ActionButton("Tocar downloads", async () => { CloseSheet(); await PlayCollectionAsync(tracks); }));
        foreach (var track in tracks)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto") };
            row.Children.Add(ActionButton(track.Title, async () => { CloseSheet(); await _music.PlayAsync(track); }));
            Put(row, IconButton("close", "Apagar download de " + track.Title, () => { _music.DeleteOffline(track); ShowDownloads(); return Task.CompletedTask; }), 0, 1);
            panel.Children.Add(row);
        }
        ShowSheet("Downloads", panel);
    }

    private async Task DownloadOfflineAsync(Track track)
    {
        if (_music.DownloadingOffline) { ShowDownloads(); return; }
        var panel = new StackPanel { Spacing = 8 };
        var status = Text("Preparando download…", UiTheme.Caption, Muted);
        panel.Children.Add(status);
        panel.Children.Add(ActionButton("Cancelar", () => { _music.CancelOfflineDownload(); return Task.CompletedTask; }));
        ShowSheet("Baixar · " + track.Title, panel);
        try
        {
            await _music.DownloadOfflineAsync(track, new Progress<string>(message => status.Text = message));
            status.Text = "Faixa disponível offline.";
        }
        catch (OperationCanceledException) { status.Text = "Download cancelado ou tempo esgotado."; }
        catch (Exception error) when (error is IOException or InvalidOperationException or HttpRequestException)
        { status.Text = error.Message; }
    }
}
