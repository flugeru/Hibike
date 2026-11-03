using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hibike.Core.Models;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private string? _mobileSearchAttempt;
    private Button _mobileSearchOnline = null!, _mobileSearchLocal = null!, _mobileSearchOptions = null!, _mobileSearchSubmit = null!;
    private Grid _mobileSearchHeading = null!;
    private TextBlock _mobileSearchCount = null!, _mobileSearchError = null!;
    private ContentControl _mobileSearchState = null!;
    private ItemsControl _mobileSearchItems = null!;
    private ScrollViewer _mobileSearchScroll = null!;
    private Track[] _mobileSearchTracks = [];
    private Control _mobileSearchSuggestions = null!, _mobileSearchBusy = null!;

    private Grid BuildMobileSearch()
    {
        var page = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*"), RowSpacing = UiTheme.CompactGap };
        var input = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap,
            HorizontalAlignment = HorizontalAlignment.Left };
        page.SizeChanged += (_, e) => input.Width = Math.Min(e.NewSize.Width, 720);
        input.Children.Add(_search);
        _mobileSearchSubmit = ActionButton("Buscar", SearchAsync);
        Put(input, _mobileSearchSubmit, 0, 1);
        page.Children.Add(input);

        var scopes = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        _mobileSearchOnline = ScopeButton("Online");
        _mobileSearchLocal = ScopeButton("Sua biblioteca");
        scopes.Children.Add(_mobileSearchOnline);
        scopes.Children.Add(_mobileSearchLocal);
        Put(page, scopes, 1, 0);

        _mobileSearchHeading = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = UiTheme.CompactGap };
        _mobileSearchHeading.Children.Add(CollectionText("Resultados", UiTheme.DialogTitle, weight: FontWeight.SemiBold));
        _mobileSearchCount = CollectionText("", UiTheme.Label, Muted);
        var chip = UiTheme.Chip(_mobileSearchCount);
        chip.VerticalAlignment = VerticalAlignment.Center;
        Put(_mobileSearchHeading, chip, 0, 1);
        _mobileSearchOptions = CollectionIcon("more", "Opções dos resultados", () =>
        {
            ShowMobileSearchOptions();
            return Task.CompletedTask;
        });
        Put(_mobileSearchHeading, _mobileSearchOptions, 0, 2);
        Put(page, _mobileSearchHeading, 2, 0);

        _mobileSearchError = CollectionText("", UiTheme.Caption, UiTheme.Error);
        _mobileSearchError.TextWrapping = TextWrapping.Wrap;
        _mobileSearchError.MaxLines = 3;
        Put(page, _mobileSearchError, 3, 0);

        _mobileSearchItems = CollectionList<Track>(track => TrackRow(track, showMenu: true));
        _mobileSearchScroll = UiTheme.FadingScroll(_mobileSearchItems);
        _mobileSearchState = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        Put(page, new Grid { Children = { _mobileSearchScroll, _mobileSearchState } }, 4, 0);

        _mobileSearchSuggestions = BuildSearchSuggestions();
        var loading = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var spinner = LoadingRing(20, UiTheme.Primary);
        spinner.VerticalAlignment = VerticalAlignment.Center;
        loading.Children.Add(spinner);
        Put(loading, Text("Buscando…", UiTheme.Body), 0, 1);
        var cancel = ActionButton("Cancelar", () => { _music.CancelSearch(); return Task.CompletedTask; });
        cancel.Classes.Add("subtle");
        Put(loading, cancel, 0, 2);
        _mobileSearchBusy = UiTheme.Card(loading, UiTheme.CompactGap);
        _mobileSearchBusy.VerticalAlignment = VerticalAlignment.Top;
        _search.TextChanged += (_, _) => { if (_page == "explore") RefreshMobileSearch(); };
        return page;

        Button ScopeButton(string name)
        {
            var button = ActionButton(name, () =>
            {
                _searchScope = name;
                _mobileSearchScroll.Offset = default;
                RefreshMobileSearch();
                return Task.CompletedTask;
            });
            button.Classes.Add("pill");
            return button;
        }
    }

    private Control BuildSearchSuggestions()
    {
        var panel = new StackPanel { Spacing = UiTheme.SectionGap, Margin = new Thickness(0, UiTheme.CompactGap, 0, 0) };
        panel.Children.Add(CollectionText("Explorar", UiTheme.DialogTitle, weight: FontWeight.SemiBold));
        panel.Children.Add(Text("Escolha um tema para começar.", UiTheme.Caption, Muted));
        var suggestions = new WrapPanel { ItemSpacing = UiTheme.CompactGap, LineSpacing = UiTheme.CompactGap };
        foreach (var (label, query) in new[] { ("Gêneros", "jazz soul instrumental music"), ("Foco", "instrumental focus music"),
            ("Energia", "energetic music"), ("Trilhas sonoras", "original soundtrack music") })
        {
            var button = ActionButton(label, async () => { _search.Text = query; await SearchAsync(); });
            button.Classes.Add("pill");
            suggestions.Children.Add(button);
        }
        panel.Children.Add(suggestions);
        return panel;
    }

    private void RefreshMobileSearch()
    {
        var query = _search.Text?.Trim() ?? "";
        var local = _searchScope == "Sua biblioteca";
        var busy = !local && _music.Searching && _mobileSearchAttempt == query;
        var hasResult = !local && query.Length > 0 && _music.ResultsQuery == query;
        var error = !local && !busy && query.Length > 0 && _mobileSearchAttempt == query && _music.SearchError is not null;
        _mobileSearchOnline.Classes.Set("primary", !local);
        _mobileSearchLocal.Classes.Set("primary", local);
        _mobileSearchSubmit.IsEnabled = query.Length > 0 && !busy;
        _mobileSearchSubmit.Classes.Set("primary", _mobileSearchSubmit.IsEnabled);
        _clearSearch.IsVisible = !busy && !string.IsNullOrEmpty(_search.Text);

        var tracks = local && query.Length > 0
            ? _music.Favorites.Where(track => MatchesLibraryQuery($"{track.Title} {track.Artist} {track.Album}", query)).ToArray()
            : hasResult && !busy && !error ? _music.Results.ToArray() : [];
        if (!_mobileSearchTracks.SequenceEqual(tracks))
        {
            _mobileSearchTracks = tracks;
            _mobileSearchItems.ItemsSource = tracks;
            _mobileSearchScroll.Offset = default;
        }
        var notice = error ? _music.SearchError : !busy && tracks.Length > 0 && _music.HasError ? _music.Status : null;
        _mobileSearchError.Text = notice ?? "";
        _mobileSearchError.IsVisible = !string.IsNullOrWhiteSpace(notice);
        _mobileSearchHeading.IsVisible = tracks.Length > 0;
        _mobileSearchScroll.IsVisible = tracks.Length > 0;
        _mobileSearchState.IsVisible = tracks.Length == 0;
        _mobileSearchCount.Text = TrackCount(tracks.Length);
        if (tracks.Length > 0) return;

        var state = busy ? "busy" : query.Length == 0 ? local ? "local" : "explore"
            : error ? "error" : local || hasResult ? "no-results" : _mobileSearchAttempt == query ? "retry" : "ready";
        if (Equals(_mobileSearchState.Tag, state)) return;
        _mobileSearchState.Tag = state;
        _mobileSearchState.Content = state switch
        {
            "busy" => _mobileSearchBusy,
            "explore" => _mobileSearchSuggestions,
            "local" => EmptyState("library", "Busque na sua biblioteca", "Encontre músicas salvas pelo título, artista ou álbum."),
            "error" or "retry" => EmptyState("search", "Busca interrompida", "Tente buscar novamente.", "Tentar novamente", SearchAsync),
            "no-results" => EmptyState("search", "Nenhum resultado", "Tente outro título ou nome de artista."),
            _ => EmptyState("search", "Pronto para buscar", "Toque em Buscar ou use o teclado.")
        };
    }

    private void ShowMobileSearchOptions()
    {
        var tracks = _mobileSearchTracks;
        var panel = new StackPanel { Spacing = UiTheme.CompactGap };
        panel.Children.Add(ActionButton("Tocar resultados", async () => { CloseSheet(); await PlayCollectionAsync(tracks); }));
        panel.Children.Add(ActionButton("Adicionar resultados à fila", () =>
        {
            if (_searchScope == "Online") _music.EnqueueResults();
            else foreach (var track in tracks) _music.Enqueue(track);
            CloseSheet();
            return Task.CompletedTask;
        }));
        ShowSheet("Resultados", panel);
    }
}
