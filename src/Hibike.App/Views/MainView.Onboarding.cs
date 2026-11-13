using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private Border? _onboarding;
    private CancellationTokenSource? _artistSearch;
    private readonly List<MusicArtist> _selectedArtists = [];
    private readonly HashSet<string> _selectedGenres = [];
    private const string OnboardingAccent = "#B784F7";
    private static readonly string[] Genres = ["Pop", "Rock", "Metal", "Hip-Hop", "Rap", "Eletrônica", "Jazz", "Clássica", "J-Pop", "K-Pop", "Anime", "Indie", "Funk", "Sertanejo", "MPB"];
    private void ShowMusicOnboarding(bool edit = false)
    {
        if (_onboarding is not null || !edit && _music.OnboardingCompleted) return;
        ClosePlayer(); CloseSheet(); CloseTopMenu();
        _selectedArtists.Clear(); _selectedArtists.AddRange(_music.FavoriteArtists);
        _selectedGenres.Clear(); _selectedGenres.UnionWith(_music.FavoriteGenres);
        var genreStage = false;
        var layout = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), MaxWidth = 960, Margin = new Thickness(_androidLayout ? 16 : 24), RowSpacing = 14 };
        var top = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = UiTheme.CompactGap };
        var heading = Text("Hibike", 18);
        heading.FontWeight = FontWeight.SemiBold;
        heading.TextWrapping = TextWrapping.Wrap;
        heading.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(heading);
        var dismiss = ActionButton(edit ? "Cancelar" : "Pular", () =>
        {
            if (_savingOnboarding) return Task.CompletedTask;
            if (!edit) return FinishOnboardingAsync(!genreStage);
            _artistSearch?.Cancel();
            if (_onboarding is not null) _root.Children.Remove(_onboarding);
            _onboarding = null;
            _preferencesBack.Focus();
            return Task.CompletedTask;
        });
        Put(top, dismiss, 0, 1); layout.Children.Add(top);
        var intro = new StackPanel { Spacing = 14 };
        var headline = new StackPanel { Spacing = 6 };
        var title = Text("Escolha seus artistas", 27); title.FontWeight = FontWeight.SemiBold;
        title.TextWrapping = TextWrapping.Wrap; headline.Children.Add(title);
        var description = Text("Selecione pelo menos 3 para personalizar seu Início.", 14, Muted);
        description.TextWrapping = TextWrapping.Wrap; headline.Children.Add(description);
        intro.Children.Add(headline);
        var search = UiTheme.Field("Buscar artistas"); AddSearchIcon(search); intro.Children.Add(search);
        var selected = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        var choices = UiTheme.FadingScroll(selected, horizontal: true);
        intro.Children.Add(choices);
        var body = new StackPanel { Spacing = 12 };
        var state = Text("Artistas", 16, Muted); state.FontWeight = FontWeight.SemiBold; state.TextWrapping = TextWrapping.Wrap; body.Children.Add(state);
        var cards = new WrapPanel { ItemWidth = 112, ItemHeight = 132, ItemSpacing = 12, LineSpacing = 12 }; body.Children.Add(cards);
        var genres = new WrapPanel { ItemSpacing = 12, LineSpacing = 12, IsVisible = false }; body.Children.Add(genres);
        cards.SizeChanged += (_, e) =>
        {
            var width = e.NewSize.Width;
            if (e.PreviousSize.Width == width || width <= 0) return;
            var columns = width < 500 ? 2 : width < 750 ? 3 : 4;
            var scaling = TopLevel.GetTopLevel(cards)?.RenderScaling ?? 1;
            cards.ItemWidth = Math.Max(1, Math.Floor((width - (columns - 1) * cards.ItemSpacing) * scaling / columns) / scaling);
        };
        var scroll = UiTheme.FadingScroll(body);
        Put(layout, intro, 1, 0);
        Put(layout, scroll, 2, 0);
        var footer = new StackPanel { Spacing = 8 }; var count = Text("", UiTheme.Caption, Muted); footer.Children.Add(count);
        Button? continueButton = null;
        var updateCards = new List<Action>();
        continueButton = ActionButton("Continuar", async () =>
        {
            if (genreStage) { await FinishOnboardingAsync(false); return; }
            genreStage = true; _artistSearch?.Cancel();
            title.Text = "Quais estilos musicais combinam com você?"; description.Text = "Etapa opcional. Seus estilos complementam seus artistas favoritos.";
            search.IsVisible = choices.IsVisible = cards.IsVisible = false; cards.Children.Clear(); updateCards.Clear(); genres.IsVisible = true;
            state.Text = "Escolha quantos estilos quiser, ou continue sem selecionar.";
            foreach (var genre in Genres)
            {
                var button = ActionButton(genre, () => Task.CompletedTask);
                void Style() { button.BorderBrush = UiTheme.Brush(_selectedGenres.Contains(genre) ? OnboardingAccent : UiTheme.Outline); button.BorderThickness = UiTheme.One; }
                button.Click += (_, _) => { if (!_selectedGenres.Add(genre)) _selectedGenres.Remove(genre); Style(); };
                Style(); genres.Children.Add(button);
            }
            count.Text = "Gêneros opcionais"; continueButton!.Content = "Começar a ouvir"; SetContinueState(true);
            scroll.Offset = default;
        });
        continueButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        continueButton.Classes.Add("onboardingContinue");
        continueButton.CornerRadius = new CornerRadius(12);
        footer.Children.Add(continueButton);
        Put(layout, footer, 3, 0);
        void SetContinueState(bool ready)
        {
            continueButton.IsEnabled = ready;
            continueButton.Opacity = 1;
            continueButton.Background = UiTheme.Brush(ready ? OnboardingAccent : "#262626");
            continueButton.Foreground = UiTheme.Brush(ready ? "#151019" : "#AAAAAA");
        }
        void UpdateSelection()
        {
            var remaining = Math.Max(0, 3 - _selectedArtists.Count);
            count.Text = $"{_selectedArtists.Count} de 20 selecionados" +
                (remaining > 0 ? $" · {(remaining == 1 ? "Falta" : "Faltam")} {remaining}" : "");
            SetContinueState(remaining == 0);
            selected.Children.Clear();
            foreach (var artist in _selectedArtists)
            {
                var chip = ActionButton(artist.Name + " ×", () => { _selectedArtists.RemoveAll(a => a.Id == artist.Id); UpdateSelection(); return Task.CompletedTask; });
                chip.Background = UiTheme.Brush("#24182E");
                chip.Foreground = UiTheme.Brush("#DFC4FF");
                selected.Children.Add(chip);
            }
            choices.IsVisible = selected.Children.Count > 0;
            foreach (var update in updateCards) update();
        }
        IReadOnlyList<MusicArtist> results = [];
        void Render()
        {
            cards.Children.Clear();
            updateCards.Clear();
            foreach (var artist in results)
            {
                var art = new Track(artist.Id, artist.Name, artist.Name, "https://music.youtube.com/channel/" + artist.Id, Thumbnail: artist.Image);
                var image = new Artwork(art, 70, highQuality: true) { CornerRadius = new(35), HorizontalAlignment = HorizontalAlignment.Center };
                var tile = new StackPanel { Spacing = 6 };
                tile.Children.Add(image);
                var label = Text(artist.Name, 13);
                label.FontWeight = FontWeight.SemiBold;
                label.TextAlignment = TextAlignment.Center;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                tile.Children.Add(label);
                var check = Text("", 12, Muted);
                check.TextAlignment = TextAlignment.Center;
                tile.Children.Add(check);
                var button = ActionButton("", () =>
                {
                    if (_selectedArtists.Any(a => a.Id == artist.Id)) _selectedArtists.RemoveAll(a => a.Id == artist.Id);
                    else if (_selectedArtists.Count < 20) _selectedArtists.Add(artist);
                    else state.Text = "Limite de 20 artistas. Remova um para escolher outro.";
                    UpdateSelection(); return Task.CompletedTask;
                });
                button.Content = tile; button.Padding = new Thickness(10); button.CornerRadius = new(14); button.ClipToBounds = true;
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Center;
                button.BorderThickness = new Thickness(2);
                bool? wasChosen = null;
                void UpdateCard()
                {
                    var chosen = _selectedArtists.Any(a => a.Id == artist.Id);
                    if (chosen == wasChosen) return;
                    wasChosen = chosen;
                    check.Text = chosen ? "✓ Selecionado" : "+ Selecionar";
                    button.Background = UiTheme.Brush(chosen ? "#24182E" : "#151515");
                    check.Foreground = UiTheme.Brush(chosen ? "#DFC4FF" : Muted);
                    button.BorderBrush = UiTheme.Brush(chosen ? OnboardingAccent : UiTheme.Outline);
                    Avalonia.Automation.AutomationProperties.SetName(button, artist.Name + (chosen ? ", selecionado" : ", selecionar"));
                }
                updateCards.Add(UpdateCard);
                UpdateCard();
                cards.Children.Add(button);
            }
        }
        search.TextChanged += async (_, _) =>
        {
            _artistSearch?.Cancel(); var request = new CancellationTokenSource(); _artistSearch = request;
            try
            {
                var query = search.Text?.Trim() ?? "";
                if (query.Length < 2) { results = []; Render(); state.Text = "Digite pelo menos 2 caracteres."; return; }
                await Task.Delay(400, request.Token); state.Text = "Buscando artistas…";
                var found = await _music.SearchArtistsAsync(query, request.Token);
                if (!ReferenceEquals(_artistSearch, request) || genreStage) return;
                results = found; Render(); scroll.Offset = default;
                state.Text = found.Count == 0 ? "Nenhum artista encontrado. Tente outro nome." : "Resultados";
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (ReferenceEquals(_artistSearch, request)) state.Text = "Não foi possível buscar. Verifique conexão ou pule por enquanto."; }
            finally { if (ReferenceEquals(_artistSearch, request)) _artistSearch = null; request.Dispose(); }
        };
        UpdateSelection();
        _onboarding = new Border { Background = Brushes.Black, Child = layout };
        KeyboardNavigation.SetTabNavigation(_onboarding, KeyboardNavigationMode.Cycle);
        _onboarding.KeyDown += (_, e) =>
        {
            if (edit && e.Key == Key.Escape && !_savingOnboarding)
            {
                dismiss.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        };
        Grid.SetColumnSpan(_onboarding, _root.ColumnDefinitions.Count); Grid.SetRowSpan(_onboarding, 3); _root.Children.Add(_onboarding); Animate(body);
        dismiss.Focus();
        async Task LoadSuggestionsAsync()
        {
            var request = new CancellationTokenSource(TimeSpan.FromSeconds(20)); _artistSearch = request;
            try
            {
                var seeds = new[] { "Aimer", "YOASOBI", "Linkin Park", "Marília Mendonça", "Anitta", "Djavan", "Billie Eilish", "Daft Punk" };
                var matches = await Task.WhenAll(seeds.Select(async name =>
                {
                    var artists = await _music.SearchArtistsAsync(name, request.Token);
                    return artists.FirstOrDefault(artist => string.Equals(artist.Name, name, StringComparison.OrdinalIgnoreCase));
                }));
                var found = matches.OfType<MusicArtist>().DistinctBy(artist => artist.Id).ToArray();
                if (!ReferenceEquals(_artistSearch, request) || genreStage || _onboarding is null) return;
                results = found; Render();
                if (found.Length == 0) state.Text = "Busque um artista pelo nome.";
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (ReferenceEquals(_artistSearch, request)) state.Text = "Sugestões indisponíveis. Busque um artista ou pule por enquanto."; }
            finally { if (ReferenceEquals(_artistSearch, request)) _artistSearch = null; request.Dispose(); }
        }
        _ = LoadSuggestionsAsync();
    }
    private bool _savingOnboarding;
    private async Task FinishOnboardingAsync(bool skip)
    {
        if (_savingOnboarding) return;
        _savingOnboarding = true;
        try
        {
            await _music.CompleteOnboardingAsync(skip ? [] : _selectedArtists, skip ? [] : _selectedGenres);
            _artistSearch?.Cancel();
            if (_onboarding is not null) _root.Children.Remove(_onboarding); _onboarding = null;
            _ = _music.RefreshRecommendationsAsync(true);
        }
        catch (Exception e) { _music.SetStatus("Não foi possível salvar suas escolhas: " + e.Message); }
        finally { _savingOnboarding = false; }
    }
    private async Task ShowArtistAsync(MusicArtist artist, bool replace = false)
    {
        var artwork = new Track(artist.Id, artist.Name, artist.Name, "https://music.youtube.com/channel/" + artist.Id, Thumbnail: artist.Image);
        if (!_music.CanUseOnline) { await OpenTrackArtistAsync(artwork, replace); return; }
        OpenCollection(artist.Name, [], "Artista", artwork, "Carregando perfil…", replace: replace, artistPage: true);
        var page = _collectionPage.Child;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            var profile = await GetArtistProfileAsync(artist.Id, timeout.Token);
            if (_page != "collection" || !ReferenceEquals(_collectionPage.Child, page)) return;
            ShowArtistCatalog(artist, profile);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (_page == "collection" && ReferenceEquals(_collectionPage.Child, page))
                OpenCollection(artist.Name, [], "Artista", artwork, "Catálogo indisponível", () => ShowArtistAsync(artist, replace: true), replace: true, artistPage: true);
        }
    }
    private Task ShowChannelAsync(Track channel)
    {
        if (!Uri.TryCreate(channel.Source, UriKind.Absolute, out var source)) return OpenTrackArtistAsync(channel);
        var id = System.Text.RegularExpressions.Regex.IsMatch(channel.Id, "^UC[\\w-]{22}$")
            ? channel.Id : source.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        return id is not null && System.Text.RegularExpressions.Regex.IsMatch(id, "^UC[\\w-]{22}$")
            ? ShowArtistAsync(new MusicArtist(id, channel.Title, channel.Thumbnail))
            : ShowPagedCollectionAsync(channel.Title, channel.Source.TrimEnd('/') + "/videos", channel, "Vídeos");
    }
}
