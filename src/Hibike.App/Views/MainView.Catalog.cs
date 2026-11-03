using Avalonia.Controls;
using Avalonia.Media;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private async Task ShowCatalogSearchAsync(CatalogKind kind, string query)
    {
        var title = kind switch { CatalogKind.Artist => "Artistas", CatalogKind.Album => "Álbuns", _ => "Playlists" };
        if (string.IsNullOrWhiteSpace(query)) { _search.Focus(); return; }
        await ShowCatalogPanelAsync(title, async (panel, token) =>
        {
            var entries = await MusicDiscovery.SearchCatalogAsync(query, kind, token);
            token.ThrowIfCancellationRequested();
            foreach (var entry in entries)
            {
                var button = ActionButton(entry.Title, () => ShowCatalogEntryAsync(entry));
                button.Content = new TextBlock { Text = entry.Title, TextWrapping = TextWrapping.Wrap };
                panel.Children.Add(button);
            }
        });
    }

    private Task ShowCatalogEntryAsync(CatalogEntry entry) =>
        ShowCatalogPanelAsync(entry.Title, async (panel, token) =>
        {
            var local = _music.Favorites.Where(track => entry.Kind == CatalogKind.Artist
                ? track.ArtistId == entry.Id || track.ArtistId is null && string.Equals(track.Artist, entry.Title, StringComparison.CurrentCultureIgnoreCase)
                : entry.Kind == CatalogKind.Album && track.AlbumId == entry.Id).ToArray();
            if (local.Length > 0)
            {
                panel.Children.Add(Text("Da sua biblioteca", UiTheme.SectionTitle));
                foreach (var track in local.Take(10)) panel.Children.Add(TrackRow(track));
            }
            var tracks = await MusicDiscovery.BrowseTracksAsync(entry, token);
            token.ThrowIfCancellationRequested();
            if (tracks.Count == 0)
            {
                panel.Children.Add(Text("Nenhuma faixa do catálogo disponível.", UiTheme.Body, Muted));
                return;
            }
            panel.Children.Add(Text("Do catálogo", UiTheme.SectionTitle));
            panel.Children.Add(ActionButton("Tocar tudo", async () => { CloseSheet(); await PlayCollectionAsync(tracks); }));
            panel.Children.Add(ActionButton("Rádio da primeira faixa", async () => { CloseSheet(); await _music.StartRadioAsync(tracks[0]); }));
            foreach (var track in tracks.Take(40)) panel.Children.Add(TrackRow(track));
        });

    private async Task ShowCatalogPanelAsync(string title, Func<StackPanel, CancellationToken, Task> load)
    {
        var panel = new StackPanel { Spacing = 8 };
        var status = Text("Buscando no catálogo…", UiTheme.Caption, Muted);
        panel.Children.Add(status);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var loading = true;
        var attached = true;
        panel.DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            if (loading) timeout.Cancel();
        };
        ShowSheet(title, panel);
        try
        {
            await load(panel, timeout.Token);
            status.IsVisible = panel.Children.Count == 1;
            status.Text = "Nenhum resultado.";
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            if (attached) status.Text = "Catálogo indisponível. Tente novamente.";
        }
        finally { loading = false; }
    }
}
