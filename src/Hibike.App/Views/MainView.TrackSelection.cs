using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Hibike.Core.Models;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly HashSet<string> _selectedTrackSources = new(StringComparer.Ordinal);
    private ScrollViewer? _trackSelectionList;
    private Func<IEnumerable<Track>>? _trackSelectionCandidates;
    private DockPanel? _trackSelectionHost;
    private Border? _trackSelectionBar;
    private Action? _updateTrackSelectionBar;
    private event Action? TrackSelectionChanged;

    private DockPanel CreateTrackSelectionHost(Control content)
    {
        var host = new DockPanel { LastChildFill = true };
        host.Classes.Add("trackSelectionHost");
        host.Children.Add(content);
        return host;
    }

    private Button SelectTracksAction(Track track, Control anchor, Func<IEnumerable<Track>> candidates) =>
        ActionButton("Selecionar músicas", () =>
        {
            _songPopup?.SetCurrentValue(Avalonia.Controls.Primitives.Popup.IsOpenProperty, false);
            if (_overlay is { } sheet && !sheet.IsVisualAncestorOf(anchor)) CloseSheet();
            StartTrackSelection(track, anchor, candidates);
            return Task.CompletedTask;
        });

    private bool IsSelectingTracks(Control row) => _trackSelectionList is not null
        && ReferenceEquals(row.FindAncestorOfType<ScrollViewer>(), _trackSelectionList);

    private void ToggleSelectedTrack(Track track)
    {
        if (!_selectedTrackSources.Add(track.Source)) _selectedTrackSources.Remove(track.Source);
        TrackSelectionChanged?.Invoke();
    }

    private void StartTrackSelection(Track initial, Control anchor, Func<IEnumerable<Track>> candidates)
    {
        if (anchor.FindAncestorOfType<ScrollViewer>() is not { } list
            || anchor.GetVisualAncestors().OfType<DockPanel>().FirstOrDefault(panel => panel.Classes.Contains("trackSelectionHost")) is not { } host)
            return;
        if (ReferenceEquals(list, _trackSelectionList))
        {
            ToggleSelectedTrack(initial);
            return;
        }

        EndTrackSelection();
        _trackSelectionList = list;
        _trackSelectionCandidates = candidates;
        _trackSelectionHost = host;
        list.DetachedFromVisualTree += OnSelectionListDetached;
        list.PropertyChanged += OnSelectionListPropertyChanged;
        _selectedTrackSources.Add(initial.Source);

        var actions = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto,Auto") };
        actions.Children.Add(CollectionIcon("close", "Sair da seleção", () =>
        {
            EndTrackSelection();
            return Task.CompletedTask;
        }));
        var count = CollectionText("", UiTheme.Body, weight: Avalonia.Media.FontWeight.SemiBold);
        count.VerticalAlignment = VerticalAlignment.Center;
        Put(actions, count, 0, 1);
        var all = CollectionIcon("select-all", "Selecionar todas", () =>
        {
            var available = AvailableSelectionTracks();
            if (available.All(track => _selectedTrackSources.Contains(track.Source))) _selectedTrackSources.Clear();
            else _selectedTrackSources.UnionWith(available.Select(track => track.Source));
            TrackSelectionChanged?.Invoke();
            return Task.CompletedTask;
        });
        Put(actions, all, 0, 2);
        var save = CollectionIcon("playlist-add", "Salvar em playlist", () =>
        {
            var tracks = SelectedTracks();
            if (tracks.Length == 0) return Task.CompletedTask;
            EndTrackSelection();
            ShowPlaylistPicker(tracks);
            return Task.CompletedTask;
        });
        Put(actions, save, 0, 3);
        var download = CollectionIcon("download", "Adicionar aos downloads", () =>
        {
            var tracks = SelectedTracks();
            EndTrackSelection();
            return DownloadTracksAsync(tracks);
        });
        Put(actions, download, 0, 4);

        void Update()
        {
            var available = AvailableSelectionTracks();
            var tracks = available.Where(track => _selectedTrackSources.Contains(track.Source)).ToArray();
            var allSelected = tracks.Length > 0 && tracks.Length == available.Length;
            count.Text = tracks.Length.ToString(System.Globalization.CultureInfo.CurrentCulture);
            AutomationProperties.SetName(count, $"{tracks.Length} músicas selecionadas");
            var allLabel = allSelected ? "Limpar seleção" : "Selecionar todas";
            all.Content = Icons.Make(allSelected ? "clear-selection" : "select-all");
            AutomationProperties.SetName(all, allLabel);
            ToolTip.SetTip(all, allLabel);
            save.IsEnabled = tracks.Length > 0;
            download.IsVisible = _music.CanUseOnline && available.Any(track => !track.IsLocal);
            download.IsEnabled = download.IsVisible && DownloadableTracks(tracks).Length > 0;
        }

        var bar = UiTheme.Card(actions, padding: 0);
        bar.Margin = new Thickness(0, 0, 0, UiTheme.CompactGap);
        _trackSelectionBar = bar;
        DockPanel.SetDock(bar, Dock.Top);
        host.Children.Insert(0, bar);
        _updateTrackSelectionBar = Update;
        TrackSelectionChanged += Update;
        TrackSelectionChanged?.Invoke();
    }

    private Track[] AvailableSelectionTracks() => _trackSelectionCandidates?.Invoke()
        .Where(track => !track.IsArtist).DistinctBy(track => track.Source).ToArray() ?? [];

    private Track[] SelectedTracks() => AvailableSelectionTracks()
        .Where(track => _selectedTrackSources.Contains(track.Source)).ToArray();

    private void OnSelectionListDetached(object? sender, VisualTreeAttachmentEventArgs args) => EndTrackSelection();

    private void OnSelectionListPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty && _trackSelectionList?.IsVisible == false)
            EndTrackSelection();
    }

    private void EndTrackSelection()
    {
        if (_trackSelectionList is null) return;
        _trackSelectionList.DetachedFromVisualTree -= OnSelectionListDetached;
        _trackSelectionList.PropertyChanged -= OnSelectionListPropertyChanged;
        TrackSelectionChanged -= _updateTrackSelectionBar;
        _updateTrackSelectionBar = null;
        _trackSelectionList = null;
        _trackSelectionCandidates = null;
        _selectedTrackSources.Clear();
        if (_trackSelectionBar is { } bar) _trackSelectionHost?.Children.Remove(bar);
        _trackSelectionHost = null;
        _trackSelectionBar = null;
        TrackSelectionChanged?.Invoke();
    }
}
