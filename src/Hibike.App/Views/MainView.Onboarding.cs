using Avalonia;
using Avalonia.Controls;
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
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), MaxWidth = 1000, Margin = new Thickness(20, 24) };
        var top = new Grid { ColumnDefinitions = new("*,Auto") };
        top.Children.Add(Text("Hibike · Seu som, seu espaço", UiTheme.Body));
        Put(top, ActionButton("Pular", () => FinishOnboardingAsync(!genreStage)), 0, 1); layout.Children.Add(top);
        var body = new StackPanel { Spacing = 18, Margin = new Thickness(0, 24) };
        var title = Text("Quais artistas você mais gosta?", UiTheme.DialogTitle); title.TextWrapping = TextWrapping.Wrap; body.Children.Add(title);
        var description = Text("Escolha pelo menos 3 artistas para personalizarmos suas recomendações.", UiTheme.Body, Muted); description.TextWrapping = TextWrapping.Wrap; body.Children.Add(description);
        var search = UiTheme.Field("Buscar artistas"); body.Children.Add(search);
        var selected = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 }; body.Children.Add(selected);
        var state = Text("Busque artistas no catálogo YouTube Music. Conta opcional.", UiTheme.Caption, Muted); state.TextWrapping = TextWrapping.Wrap; body.Children.Add(state);
        var cards = new WrapPanel { ItemWidth = 112, ItemHeight = 112, ItemSpacing = 18, LineSpacing = 18 }; body.Children.Add(cards);
        var genres = new WrapPanel { ItemSpacing = 18, LineSpacing = 18, IsVisible = false }; body.Children.Add(genres);
        cards.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width == e.PreviousSize.Width || e.NewSize.Width <= 0) return;
            var width = e.NewSize.Width;
            var columns = width < 500 ? 2 : width < 750 ? 3 : 4;
            // Size the whole tile, including the selection border, inside the scroll viewport.
            var scaling = TopLevel.GetTopLevel(cards)?.RenderScaling ?? 1;
            var size = Math.Floor((width - (columns - 1) * cards.ItemSpacing) * scaling / columns) / scaling;
            cards.ItemWidth = cards.ItemHeight = Math.Clamp(size, 1, 220);
        };
        var content = new StackPanel(); content.Children.Add(body);
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; Put(layout, scroll, 1, 0);
        var footer = new StackPanel { Spacing = 8 }; var count = Text("", UiTheme.Caption, Muted); footer.Children.Add(count);
        Button? continueButton = null;
        var updateCards = new List<Action>();
        continueButton = ActionButton("Continuar", async () =>
        {
            if (genreStage) { await FinishOnboardingAsync(false); return; }
            genreStage = true; _artistSearch?.Cancel();
            title.Text = "Quais estilos musicais combinam com você?"; description.Text = "Etapa opcional. Seus estilos complementam seus artistas favoritos.";
            search.IsVisible = selected.IsVisible = cards.IsVisible = false; cards.Children.Clear(); updateCards.Clear(); genres.IsVisible = true;
            state.Text = "Escolha quantos estilos quiser, ou continue sem selecionar.";
            foreach (var genre in Genres)
            {
                var button = ActionButton(genre, () => Task.CompletedTask);
                void Style() { button.BorderBrush = UiTheme.Brush(_selectedGenres.Contains(genre) ? OnboardingAccent : UiTheme.Outline); button.BorderThickness = new Thickness(2); }
                button.Click += (_, _) => { if (!_selectedGenres.Add(genre)) _selectedGenres.Remove(genre); Style(); };
                Style(); genres.Children.Add(button);
            }
            count.Text = "Gêneros opcionais"; continueButton!.Content = "Começar a ouvir"; continueButton.IsEnabled = true;
            scroll.Offset = default;
        });
        continueButton.Classes.Add("primary"); continueButton.Background = UiTheme.Brush(OnboardingAccent); continueButton.Foreground = Brushes.Black; continueButton.HorizontalAlignment = HorizontalAlignment.Stretch; footer.Children.Add(continueButton); Put(layout, footer, 2, 0);
        void UpdateSelection()
        {
            count.Text = $"{_selectedArtists.Count} de 20 artistas selecionados"; continueButton.IsEnabled = _selectedArtists.Count >= 3;
            selected.Children.Clear();
            foreach (var artist in _selectedArtists.ToArray()) selected.Children.Add(ActionButton(artist.Name + " ×", () => { _selectedArtists.RemoveAll(a => a.Id == artist.Id); UpdateSelection(); return Task.CompletedTask; }));
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
                var image = new Artwork(art, 220, highQuality: true) { Width = double.NaN, Height = double.NaN, CornerRadius = new(16) };
                var overlay = new Grid(); overlay.Children.Add(image);
                var label = Text(artist.Name, UiTheme.Body); label.FontWeight = FontWeight.SemiBold; label.TextWrapping = TextWrapping.Wrap; label.MaxLines = 2;
                overlay.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), Padding = new Thickness(10), VerticalAlignment = VerticalAlignment.Bottom, Child = label });
                var check = Text("○", UiTheme.DialogTitle, UiTheme.OnPrimary); check.HorizontalAlignment = HorizontalAlignment.Right; check.Margin = new Thickness(8); overlay.Children.Add(check);
                var button = ActionButton("", () =>
                {
                    if (_selectedArtists.Any(a => a.Id == artist.Id)) _selectedArtists.RemoveAll(a => a.Id == artist.Id);
                    else if (_selectedArtists.Count < 20) _selectedArtists.Add(artist);
                    else state.Text = "Limite de 20 artistas. Remova um para escolher outro.";
                    UpdateSelection(); return Task.CompletedTask;
                });
                button.Content = overlay; button.Padding = new Thickness(0); button.CornerRadius = new(16); button.ClipToBounds = true;
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.VerticalContentAlignment = VerticalAlignment.Stretch;
                button.BorderThickness = new Thickness(2);
                bool? wasChosen = null;
                void UpdateCard()
                {
                    var chosen = _selectedArtists.Any(a => a.Id == artist.Id);
                    if (chosen == wasChosen) return;
                    wasChosen = chosen;
                    check.Text = chosen ? "✓" : "○";
                    check.Foreground = UiTheme.Brush(chosen ? OnboardingAccent : UiTheme.OnPrimary);
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
                results = found; Render(); state.Text = found.Count == 0 ? "Nenhum artista encontrado. Tente outro nome." : "Toque para selecionar. Suas escolhas permanecem ao buscar novamente.";
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (ReferenceEquals(_artistSearch, request)) state.Text = "Não foi possível buscar. Verifique conexão ou pule por enquanto."; }
            finally { if (ReferenceEquals(_artistSearch, request)) _artistSearch = null; request.Dispose(); }
        };
        UpdateSelection();
        _onboarding = new Border { Background = Brushes.Black, Child = layout };
        Grid.SetColumnSpan(_onboarding, _root.ColumnDefinitions.Count); Grid.SetRowSpan(_onboarding, 3); _root.Children.Add(_onboarding); Animate(body);
        async Task LoadSuggestionsAsync()
        {
            var request = new CancellationTokenSource(TimeSpan.FromSeconds(20)); _artistSearch = request;
            try
            {
                var found = await _music.SearchArtistsAsync("Brasil", request.Token);
                if (!ReferenceEquals(_artistSearch, request) || genreStage || _onboarding is null) return;
                results = found; Render();
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
