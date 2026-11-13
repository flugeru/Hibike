using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Hibike.Core.Models;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private Button SelectTracksAction(Track track, Func<IEnumerable<Track>> candidates) =>
        ActionButton("Selecionar músicas", () =>
        {
            _songPopup?.SetCurrentValue(Avalonia.Controls.Primitives.Popup.IsOpenProperty, false);
            ShowTrackSelection(track, candidates());
            return Task.CompletedTask;
        });

    private void ShowTrackSelection(Track initial, IEnumerable<Track> candidates)
    {
        var tracks = candidates.Where(track => !track.IsArtist).DistinctBy(track => track.Source).ToArray();
        var initialIndex = Array.FindIndex(tracks, track => track.Source == initial.Source);
        if (initialIndex < 0) return;

        var list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            ItemsSource = tracks,
            SelectedIndex = initialIndex,
            ItemTemplate = new FuncDataTemplate<Track>((track, _) =>
            {
                if (track is null) return new Border();
                var title = CollectionText(TrackTextFormatter.Title(track.Title, track.Artist), UiTheme.Body);
                var artist = CollectionText(TrackTextFormatter.Artist(track.Artist), UiTheme.Caption, Muted);
                return new StackPanel
                {
                    MinHeight = UiTheme.TouchSize,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { title, artist }
                };
            })
        };
        Avalonia.Automation.AutomationProperties.SetName(list, "Músicas para selecionar");
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = UiTheme.CompactGap };
        var toolbar = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        var count = CollectionText("", UiTheme.Caption, Muted);
        var selectAll = ActionButton("Selecionar todas", () =>
        {
            if (list.Selection.Count == tracks.Length) list.Selection.Clear();
            else list.Selection.SelectAll();
            return Task.CompletedTask;
        });
        toolbar.Children.Add(selectAll);
        toolbar.Children.Add(count);
        layout.Children.Add(toolbar);
        Put(layout, list, 1, 0);

        Track[] SelectedTracks()
        {
            var selected = list.SelectedItems!.Cast<Track>().ToHashSet();
            return tracks.Where(selected.Contains).ToArray();
        }

        var actions = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        var save = ActionButton("Salvar em playlist", () =>
        {
            ShowPlaylistPicker(SelectedTracks());
            return Task.CompletedTask;
        });
        var download = ActionButton("Adicionar aos downloads", () => DownloadTracksAsync(SelectedTracks()));
        var downloadable = DownloadableTracks(tracks).ToHashSet();
        download.IsVisible = _music.CanUseOnline && downloadable.Count > 0;
        actions.Children.Add(save);
        actions.Children.Add(download);
        Put(layout, actions, 2, 0);

        void UpdateSelection()
        {
            var selected = list.Selection.Count;
            count.Text = $"{selected} de {tracks.Length}";
            selectAll.Content = selected == tracks.Length ? "Limpar seleção" : "Selecionar todas";
            save.IsEnabled = selected > 0;
            download.IsEnabled = list.SelectedItems!.Cast<Track>().Any(downloadable.Contains);
        }
        list.SelectionChanged += (_, _) => UpdateSelection();
        UpdateSelection();
        ShowSheet("Selecionar músicas", layout, preservePlayer: true, scrollContent: false);
        list.ScrollIntoView(initialIndex);
    }
}
