using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

// Native Windows check. No network, real audio, or changes to the user's library.
internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hibike-ui-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        AppServices.Configure(new Catalog(), new Player(), directory);
        var result = 1;
        var builder = AppBuilder.Configure<Hibike.App.App>().UsePlatformDetect().WithInterFont();
        builder.AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
        {
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            try
            {
                var window = lifetime.MainWindow!; var view = (MainView)window.Content!;
                await Task.Delay(300);
                var welcomeField = typeof(MainView).GetField("_googleWelcome", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Check(welcomeField.GetValue(view) is Border, "First-run Google welcome missing");
                var initialWidth = window.Width; var initialHeight = window.Height;
                window.Width = 360; window.Height = 800; await Task.Delay(100); Capture(view, "google-welcome-360");
                Check(AppServices.BackHandler!(), "Welcome ignores back");
                Check(File.Exists(Path.Combine(directory, "google-welcome-seen")), "Welcome choice not persisted");
                var welcomeMethod = typeof(MainView).GetMethod("ShowGoogleWelcome", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                welcomeMethod.Invoke(view, [true]); Check(welcomeField.GetValue(view) is null, "Welcome repeats after first run");
                welcomeMethod.Invoke(view, [false]); Check(welcomeField.GetValue(view) is Border, "Manual reconnect welcome missing");
                AppServices.ConnectYouTubeAccount = () => Task.FromException<YouTubeAccountLibrary>(new InvalidOperationException("OAuth configuration check"));
                await Task.Delay(50);
                var welcome = (Border)welcomeField.GetValue(view)!;
                welcome.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Entrar com Google")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(50);
                Check(welcome.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "OAuth configuration check"), "Login hides actionable error");
                AppServices.ConnectYouTubeAccount = null;
                AppServices.BackHandler!();
                window.Width = initialWidth; window.Height = initialHeight; await Task.Delay(100);
                var toolsEnabled = (bool)typeof(MainView).Assembly.GetType("Hibike.App.DeveloperFeatures")!.GetProperty("Enabled")!.GetValue(null)!;
                typeof(MainView).GetMethod("ShowSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                await Task.Delay(100);
                Check(view.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && b.Content as string == "Diagnóstico de reprodução") == toolsEnabled, "Developer diagnostics visibility incorrect");
                Check(view.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && b.Content as string == "Laboratório experimental") == toolsEnabled, "Developer lab visibility incorrect");
                AppServices.BackHandler!();
                var sameRadioSong = typeof(MusicController).GetMethod("SameRadioSong", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                var seedSong = new Track("seed-song", "Yellow Forest - Numbers the Hours", "Lorien Testard", "seed-song", 260);
                foreach (var title in new[] { "Clair Obscur: Expedition 33 - Yellow Forest - Numbers the Hours (Extended)", "Yellow Forest - Numbers the Hours (Official Audio)", "Clair Obscur : Expedition 33 — 081. Yellow Forest - Numbers The Hours" })
                    Check((bool)sameRadioSong.Invoke(null, [seedSong, new Track(title, title, "Different uploader", title, 260)])!, "Radio allowed reupload of seed song");
                var personal = new Track("personal", "PERSONAL", "女王蜂", "local-personal", 275, IsLocal: true);
                foreach (var title in new[] { "『PERSONAL』Official MV", "Hell's Paradise Season 2 - Ending FULL PERSONAL", "【MAD】地獄楽 二期 PERSONAL-女王蜂", "【中+日+羅歌詞】地獄楽 第二期ED『PERSONAL』女王蜂" })
                    Check((bool)sameRadioSong.Invoke(null, [personal, new Track(title, title, "Uploader", title, 276)])!, "PERSONAL duplicate allowed");
                Check(!(bool)sameRadioSong.Invoke(null, [personal, new Track("homonym", "PERSONAL", "Other artist", "homonym", 275)])!, "Different artist homonym rejected");
                Check(!(bool)sameRadioSong.Invoke(null, [personal, new Track("duration", "Different song", "女王蜂", "duration", 275)])!, "Duration alone matched song");
                Check((bool)sameRadioSong.Invoke(null, [personal with { EquivalentVideoIds = ["translated"] }, new Track("translated", "完全翻訳", "Unknown", "translated")])!, "Explicit counterpart ignored");
                var radioJson = """{"playlistPanelVideoWrapperRenderer":{"primaryRenderer":{"playlistPanelVideoRenderer":{"videoId":"abcdefghijk","title":{"simpleText":"PERSONAL"},"lengthText":{"simpleText":"4:35"},"longBylineText":{"runs":[{"text":"女王蜂","navigationEndpoint":{"browseEndpoint":{"browseId":"UCartist"}}}]}}},"counterpart":[{"videoId":"lmnopqrstuv"}]}}""";
                var parsedRadio = YouTubeMusicRadio.Parse(radioJson).Single();
                Check(parsedRadio.DurationSeconds == 275 && parsedRadio.MusicArtist == "女王蜂" && parsedRadio.EquivalentVideoIds!.Contains("lmnopqrstuv"), "Radio metadata/counterpart parsing failed");
                var primaryCatalog = new RadioCatalog { UseRadio = true };
                var primaryDirectory = Path.Combine(directory, "primary-radio");
                var primaryRadio = new MusicController(new CachedMusicCatalog(primaryCatalog), new Player(), new LibraryStore(primaryDirectory), primaryDirectory);
                await primaryRadio.InitializeAsync();
                await primaryRadio.PlayAsync(new Track("primary", "Seed", "Seed Artist", "https://example.org/seed", 180));
                await primaryRadio.NextAsync();
                Check(primaryCatalog.RadioCalls > 0 && primaryCatalog.SearchCalls == 0 && primaryRadio.Queue.Count > 1, "Radio did not prefer provider queue over search");
                primaryRadio.StopObserving();
                Check(!(bool)sameRadioSong.Invoke(null, [seedSong, new Track("other", "The Manor - Our Painted Family", "Lorien Testard", "other")])!, "Radio rejected different song by same artist");
                var radioDirectory = Path.Combine(directory, "radio"); Directory.CreateDirectory(radioDirectory);
                var radio = new MusicController(new RadioCatalog(), new Player(), new LibraryStore(radioDirectory), radioDirectory);
                await radio.SearchAsync("seed"); await radio.PlayAsync(radio.Results[0]);
                Check(radio.Queue.Count == 11, "Radio did not add ten tracks from empty upcoming queue");
                Check(radio.Queue.Skip(1).Zip(radio.Queue.Skip(2)).All(pair => pair.First.Artist != pair.Second.Artist), "Radio failed to diversify adjacent artists");
                for (var i = 0; i < 10; i++) await radio.NextAsync();
                Check(radio.Queue.Count == 21 && radio.Queue.Select(t => t.Source).Distinct().Count() == 21, "Radio refill repeated tracks or failed");
                radio.ClearQueue(); await radio.NextAsync();
                Check(radio.Queue.Count == 1, "Radio ignored explicit clear queue"); radio.StopObserving();
                var singleRadio = new MusicController(new RadioCatalog { FailLaterSearches = true }, new Player(), new LibraryStore(radioDirectory), radioDirectory);
                var single = new Track("single", "Seed", "Artist", "https://example.org/single", 180);
                singleRadio.Enqueue(single);
                await singleRadio.PlayAsync(single);
                Check(singleRadio.Queue.Count == 11, "Single queued track outside search results did not receive partial radio suggestions");
                singleRadio.StopObserving();
                singleRadio.CreatePlaylist("Test mix");
                var localPlaylist = singleRadio.Playlists.Single();
                singleRadio.AddToPlaylist(localPlaylist, single); singleRadio.AddToPlaylist(localPlaylist, single);
                singleRadio.PinPlaylist(localPlaylist); singleRadio.TogglePin(single);
                singleRadio.RenamePlaylist(localPlaylist, "Renamed mix");
                Check(localPlaylist.Tracks.Count == 1 && localPlaylist.Pinned && singleRadio.Pins.Count == 1, "Playlist deduplication/pins failed");
                var restoredPersonal = new MusicController(new RadioCatalog(), new Player(), new LibraryStore(radioDirectory), radioDirectory);
                Check(restoredPersonal.Playlists.Single().Name == "Renamed mix" && restoredPersonal.Pins.Count == 1, "Personalization persistence failed");
                restoredPersonal.StopObserving();
                singleRadio.PlayNext(new Track("manual", "Manual", "Other", "manual"));
                Check(singleRadio.Queue[1].Source == "manual", "Play next did not take priority over radio");
                singleRadio.SetAutoplay(false); Check(!singleRadio.Autoplay, "Autoplay toggle failed");
                var loadingField = typeof(MusicController).GetField("<LoadingAudio>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var refreshPlay = typeof(MainView).GetMethod("RefreshPlayButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var loadingButton = new Button();
                loadingField.SetValue(AppServices.Controller, true);
                refreshPlay.Invoke(view, [loadingButton]);
                Check(loadingButton.Content is Avalonia.Controls.Shapes.Ellipse ring && ring.Styles.Count > 0, "Playback loading animation missing");
                loadingField.SetValue(AppServices.Controller, false);
                refreshPlay.Invoke(view, [loadingButton]);
                Check(loadingButton.Content is not Avalonia.Controls.Shapes.Ellipse, "Playback loading animation not cleared");
                var presenceType = typeof(MainView).Assembly.GetType("Hibike.App.DiscordAccountPresence")!;
                var presence = (IDisposable)Activator.CreateInstance(presenceType, [AppServices.Controller!])!;
                foreach (var invalidToken in new[] { "", "bad token", new string('x', 513) })
                {
                    var rejected = false;
                    try { await (Task)presenceType.GetMethod("ConnectAsync")!.Invoke(presence, [invalidToken])!; }
                    catch (ArgumentException) { rejected = true; }
                    Check(rejected, "Discord accepted malformed token");
                }
                presence.Dispose(); presence.Dispose();
                var validateFrame = presenceType.GetMethod("ValidateFrame", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                validateFrame.Invoke(null, [System.Net.WebSockets.WebSocketMessageType.Text, 2_000_000L]);
                foreach (var frame in new[] { (System.Net.WebSockets.WebSocketMessageType.Text, 16L * 1024 * 1024 + 1), (System.Net.WebSockets.WebSocketMessageType.Binary, 100L) })
                {
                    var rejected = false;
                    try { validateFrame.Invoke(null, [frame.Item1, frame.Item2]); }
                    catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
                    Check(rejected, "Discord frame limit/type validation missing");
                }
                var closeMessage = presenceType.GetMethod("CloseMessage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                Check(((string)closeMessage.Invoke(null, [4004])!).Contains("4004"), "Discord authentication close code hidden");
                Check(((string)closeMessage.Invoke(null, [4002])!).Contains("protocolo"), "Discord protocol failure mislabeled as bad token");
                var now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
                using (var activity = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
                    presenceType.GetMethod("BuildActivity", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(null, [new Track("rpc", "Song", "Artist", "test"), 30d, 180d, "mp:external/test", now]))))
                {
                    var root = activity.RootElement;
                    Check(root.GetProperty("application_id").GetString() == "1566415890596241459", "Discord application mismatch");
                    Check(root.GetProperty("assets").GetProperty("large_image").GetString() == "mp:external/test", "Discord artwork missing");
                    Check(root.GetProperty("timestamps").GetProperty("start").GetInt64() == now.ToUnixTimeMilliseconds() - 30_000, "Discord playback start incorrect");
                    Check(root.GetProperty("timestamps").GetProperty("end").GetInt64() == now.ToUnixTimeMilliseconds() + 150_000, "Discord playback end incorrect");
                }
                await Task.Delay(200);
                var searchingField = typeof(MusicController).GetField("<Searching>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                searchingField.SetValue(AppServices.Controller, true);
                typeof(MainView).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                await Task.Delay(100);
                var searchLoading = (Border)typeof(MainView).GetField("_searchLoading", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                Check(searchLoading.IsEffectivelyVisible && searchLoading.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Any(), "Centered search loading missing");
                Capture(view, "search-loading");
                searchingField.SetValue(AppServices.Controller, false);
                typeof(MainView).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                Check(!searchLoading.IsVisible, "Search loading remained after completion");
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Ouvir de novo" && t.IsEffectivelyVisible), "Home listening history shelf missing");
                AppServices.Controller!.History.Add(new("home-check", "Faixa recente", "Artista", "test"));
                AppServices.Controller.SetStatus("Home check");
                foreach (var homeWidth in new[] { 360, 1260 })
                {
                    window.Width = homeWidth; await Task.Delay(150); Capture(view, $"home-{homeWidth}");
                    if (homeWidth == 360)
                    {
                        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Hibiki"), "Compact Windows missing app header");
                        Check(view.GetVisualDescendants().OfType<Button>().Count(b => b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b) == "Menu do aplicativo") == 1, "Compact Windows missing single menu button");
                        foreach (var tab in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item4))
                        {
                            var content = (StackPanel)tab.Content!;
                            var indicator = content.Children[0]; var caption = content.Children[1];
                            Check(Math.Abs(indicator.Bounds.Center.X - caption.Bounds.Center.X) < 1, "Mobile navigation label not centered under icon");
                        }
                    }
                    if (homeWidth == 1260)
                    {
                        var import = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Importar músicas");
                        var playlist = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Nova playlist");
                        Check(import.Bounds.Width == playlist.Bounds.Width && import.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Center, "Sidebar actions not aligned");
                        var decode = typeof(MainView).Assembly.GetType("Hibike.App.Views.Artwork")!.GetMethod("DecodeAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                        Func<Stream> open = () => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "home-1260.png"));
                        using var thumbnail = await (Task<Bitmap>)decode.Invoke(null, [open, 128, CancellationToken.None])!;
                        Check(thumbnail.PixelSize.Width == 128, "Artwork decode ignored thumbnail size");
                        var artworkType = typeof(MainView).Assembly.GetType("Hibike.App.Views.Artwork")!;
                        var sample = new Track("aspect", "Aspect", "Test", "aspect", Thumbnail: new Uri(Path.Combine(AppContext.BaseDirectory, "home-1260.png")).AbsoluteUri);
                        var art = (Border)Activator.CreateInstance(artworkType, [sample, 160d, false, Avalonia.Media.Stretch.Uniform, true, false])!;
                        var homePanel = (StackPanel)typeof(MainView).GetField("_home", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        homePanel.Children.Add(art); await Task.Delay(300);
                        Check(Math.Abs(art.Height - 160d * 820 / 1260) < 2 && art.BorderThickness == default, "Natural artwork kept square frame");
                        homePanel.Children.Remove(art);
                    }
                    var card = view.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Faixa recente — Artista");
                    var cardHost = (Grid)card.Parent!;
                    var shelf = cardHost.Parent!.Parent!.Parent!;
                    typeof(MainView).GetField("_homeFingerprint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, null);
                    AppServices.Controller.SetStatus("Home stable refresh"); await Task.Delay(50);
                    Check(ReferenceEquals(cardHost.Parent?.Parent?.Parent, shelf), "Unchanged home shelf recreated on refresh");
                    var cardArtwork = card.GetVisualDescendants().OfType<Border>().Single(b => b.GetType().Name == "Artwork");
                    Check(cardArtwork.Width == cardArtwork.Height && cardArtwork.CornerRadius == new CornerRadius(16) && cardArtwork.BorderThickness == default, "Home artwork not square with subtle corners");
                    Check(card.GetVisualDescendants().OfType<Image>().All(image => image.Stretch == Avalonia.Media.Stretch.UniformToFill), "Home artwork does not fill square");
                    Check(((StackPanel)cardHost.Parent!).Spacing == 18, "Home carousel spacing inconsistent");
                    if (view.Bounds.Width == 360)
                    {
                        var toggleTopMenu = typeof(MainView).GetMethod("ToggleTopMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        toggleTopMenu.Invoke(view, null); await Task.Delay(300);
                        var topMenu = (Grid)typeof(MainView).GetField("_topMenuLayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        Check(topMenu.IsVisible && topMenu.Children[0].Bounds.Height > 100 && topMenu.Children[0].Bounds.Width <= view.Bounds.Width, "Top menu clipped or collapsed");
                        Check(topMenu.GetVisualDescendants().OfType<Button>().Count() >= 4, "Top menu missing actions");
                        Capture(view, "top-menu-360"); Check(AppServices.BackHandler!(), "Top menu ignores back"); await Task.Delay(300);
                        Check(!topMenu.IsVisible, "Top menu did not close");
                    }
                    Check(cardHost.Children.OfType<Button>().Where(b => b != card).All(b => !b.IsVisible), "Card menu visible without hover/focus");
                    var cardActions = cardHost.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("cardAction")).ToArray();
                    Check(cardActions.Length == 2 && cardActions.All(b => b.Width == 48 && b.Height == 48 && b.Padding == default && b.BorderThickness == default && b.CornerRadius == new CornerRadius(22)), "Card actions differ in size or circle style");
                    Check(card.Bounds.Width >= 160 && card.IsEffectivelyVisible, "Home history cover card missing");
                    foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && !b.GetVisualAncestors().OfType<Slider>().Any()))
                        Check(button.Bounds.Height >= 48, "Home touch target below 48");
                }
                typeof(MainView).GetField("_page", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, "explore");
                var imageUrl = typeof(MainView).Assembly.GetType("Hibike.App.Views.Artwork")!.GetMethod("PlayerImageUrl")!;
                Check((string)imageUrl.Invoke(null, ["https://i.ytimg.com/vi/abc/hqdefault.jpg"])! == "https://i.ytimg.com/vi/abc/maxresdefault.jpg", "Player did not request high-resolution YouTube artwork");
                Check((string)imageUrl.Invoke(null, ["https://example.org/cover.jpg"])! == "https://example.org/cover.jpg", "Unrelated artwork URL rewritten");
                await AppServices.Controller!.SearchAsync("test");
                foreach (var width in new[] { 360, 390, 430, 1000, 1260, 1920 })
                {
                    window.Width = width; window.Height = 820;
                    await Task.Delay(150);
                    Check(view.GetVisualDescendants().OfType<TextBlock>().All(t => t.FontSize >= 12), "Text below 12");
                    foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && !b.GetVisualAncestors().OfType<Slider>().Any()))
                        Check(button.Bounds.Height >= 48, $"Touch target below 48: {button.Content} {button.Classes} {button.Bounds}");
                    var list = view.GetVisualDescendants().OfType<ItemsControl>().First(c => c.ItemsSource is System.Collections.ObjectModel.ObservableCollection<Track>);
                    Check(list.GetVisualDescendants().OfType<Button>().Count() < 100, "List not virtualized");
                    Capture(view, $"search-{width}");
                    var scrollingList = list.GetVisualAncestors().OfType<ScrollViewer>().First();
                    var mask = (Avalonia.Media.LinearGradientBrush)scrollingList.OpacityMask!;
                    Check(mask.GradientStops[0].Color.A == 255 && mask.GradientStops[^1].Color.A == 64, "List fade incorrect at start");
                    scrollingList.Offset = new Vector(0, 100); await Task.Delay(50);
                    mask = (Avalonia.Media.LinearGradientBrush)scrollingList.OpacityMask!;
                    Check(mask.GradientStops[0].Color.A == 64 && mask.GradientStops[^1].Color.A == 64, "List fade missing in middle");
                    scrollingList.Offset = new Vector(0, 150); await Task.Delay(50);
                    Check(ReferenceEquals(mask, scrollingList.OpacityMask), "Scrolling rebuilt unchanged edge mask");
                    scrollingList.Offset = new Vector(0, scrollingList.Extent.Height); await Task.Delay(50);
                    mask = (Avalonia.Media.LinearGradientBrush)scrollingList.OpacityMask!;
                    Check(mask.GradientStops[0].Color.A == 64 && mask.GradientStops[^1].Color.A == 255, "List fade incorrect at end");
                    scrollingList.Offset = default; await Task.Delay(50);
                    var search = view.GetVisualDescendants().OfType<TextBox>().First();
                    search.Text = "test"; await Task.Delay(30);
                    var clear = ((StackPanel)search.InnerRightContent!).Children[0];
                    Check(clear.IsVisible, "Search clear button missing");
                    search.Text = ""; await Task.Delay(30);
                    Check(!clear.IsVisible, "Empty search has clear button");
                    await AppServices.Controller.PlayAsync(AppServices.Controller.Results[0]);
                    await Task.Delay(100); Capture(view, $"mini-{width}");
                    if (width >= 1000)
                    {
                        var dockPlay = (Button)typeof(MainView).GetField("_dockPlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        var playIcon = dockPlay.Content;
                        typeof(MainView).GetMethod("RefreshDock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                        Check(ReferenceEquals(playIcon, dockPlay.Content), "Playback tick rebuilt unchanged icon");
                        var dockSeek = view.GetVisualDescendants().OfType<Slider>().Single(s => Avalonia.Automation.AutomationProperties.GetName(s) == "Posição da reprodução");
                        var dockCenter = dockSeek.TranslatePoint(new Point(dockSeek.Bounds.Width / 2, 0), view)!.Value.X;
                        Check(Math.Abs(dockCenter - view.Bounds.Width / 2) < 2, "Dock timeline not centered in window");
                        Check(dockSeek.Bounds.Width <= 720, "Dock timeline stretched beyond width cap");
                        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.Contains("160 kb/s") == true), "Dock audio quality missing");
                        foreach (var label in new[] { "Aleatório: desligado", "Repetir: desligado", AppServices.Controller.IsFavorite(AppServices.Controller.Current!) ? "Remover dos favoritos" : "Adicionar aos favoritos", "Volume da barra" })
                        {
                            var control = view.GetVisualDescendants().OfType<Control>().Single(c => c.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(c) == label);
                            var point = control.TranslatePoint(default, view)!.Value;
                            Check(point.X >= 0 && point.X + control.Bounds.Width <= view.Bounds.Width, "Desktop dock control clipped: " + label);
                        }
                    }
                    AppServices.OpenPlayerView!(); await Task.Delay(300);
                    if (width == 360)
                    {
                        Button PlayerButton(string name) => (Button)typeof(MainView).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        async Task ClickPlayer(string name)
                        {
                            PlayerButton(name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                        }
                        void Synced(string kind) => Check(Equals(PlayerButton("_dock" + kind).Tag, PlayerButton("_detail" + kind).Tag), "Player state diverged: " + kind);
                        await ClickPlayer("_detailShuffle"); Check(AppServices.Controller.Shuffle, "Expanded shuffle did not enable"); Synced("Shuffle");
                        await ClickPlayer("_dockShuffle"); Check(!AppServices.Controller.Shuffle, "Dock shuffle did not disable"); Synced("Shuffle");
                        await ClickPlayer("_detailRepeat"); Check(AppServices.Controller.RepeatQueue && !AppServices.Controller.Repeat, "Repeat queue mode missing"); Synced("Repeat");
                        await ClickPlayer("_dockRepeat"); Check(!AppServices.Controller.RepeatQueue && AppServices.Controller.Repeat, "Repeat one mode missing"); Synced("Repeat");
                        await ClickPlayer("_detailRepeat"); Check(!AppServices.Controller.RepeatQueue && !AppServices.Controller.Repeat, "Repeat did not turn off"); Synced("Repeat");
                        var wasFavorite = AppServices.Controller.IsFavorite(AppServices.Controller.Current!);
                        await ClickPlayer("_detailFavorite"); Check(AppServices.Controller.IsFavorite(AppServices.Controller.Current!) != wasFavorite, "Expanded favorite did not toggle"); Synced("Favorite");
                        await ClickPlayer("_dockFavorite"); Check(AppServices.Controller.IsFavorite(AppServices.Controller.Current!) == wasFavorite, "Dock favorite did not toggle"); Synced("Favorite");
                    }
                    Capture(view, $"player-{width}");
                    Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.Contains("160 kb/s") == true), "Expanded player audio badge missing");
                    Check(view.GetVisualDescendants().OfType<Slider>().Any(s => s.Bounds.Width > 0 && s.Bounds.Width <= width), "Player slider clipped");
                    var slider = view.GetVisualDescendants().OfType<Slider>().Single(s => s.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(s) != "Volume");
                    Check(slider.TranslatePoint(default, view)!.Value.Y + slider.Bounds.Height <= view.Bounds.Height, "Player slider below viewport");
                    if (width >= 1260)
                    {
                        var artwork = view.GetVisualDescendants().OfType<Border>().Single(b => b.GetType().Name == "Artwork" && b.Bounds.Width > 100 && b.IsEffectivelyVisible);
                        Check(artwork.Bounds.Width <= 500, "Desktop artwork exceeds size cap");
                        var slot = (Grid)artwork.Parent!;
                        var split = (Grid)slot.Parent!;
                        Check(Math.Abs(slot.Bounds.Width - (split.Bounds.Width - split.ColumnSpacing) / 2) < 2, "Desktop player does not split space equally");
                        Check(split.Bounds.Width >= view.Bounds.Width - 320, "Desktop player content shrank to artwork width");
                        Check(Math.Abs(artwork.Bounds.Center.Y - slot.Bounds.Height / 2) < 1, "Artwork not vertically centered");
                        Check(Math.Abs(artwork.Bounds.Center.X - slot.Bounds.Width / 2) < 1, "Artwork not horizontally centered");
                    }
                    if (width == 1260)
                    {
                        var marquee = view.GetVisualDescendants().OfType<Canvas>().Single(c => c.GetType().Name == "ScrollingTitle");
                        var movingTitle = (TextBlock)marquee.Children[0];
                        await Task.Delay(2300);
                        Check(((Avalonia.Media.TranslateTransform)movingTitle.RenderTransform!).X < 0, "Overflow title did not scroll");
                        window.Width = 390; await Task.Delay(300); Capture(view, "player-resized-mobile");
                        window.Width = width; await Task.Delay(300); Capture(view, "player-resized-desktop");
                        var artwork = view.GetVisualDescendants().OfType<Border>().Single(b => b.GetType().Name == "Artwork" && b.Bounds.Width > 100 && b.IsEffectivelyVisible);
                        window.Width = width + 1; await Task.Delay(300);
                        Check(Math.Abs(artwork.Bounds.Width - artwork.Bounds.Height) < 1, "Player artwork not square");
                        Check(artwork.GetVisualDescendants().OfType<Image>().Single().Stretch == Avalonia.Media.Stretch.UniformToFill, "Player artwork not filling square");
                        Capture(view, "player-widescreen-artwork");
                        var longPlay = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("largePlay"));
                        var longControls = (Grid)longPlay.Parent!;
                        var longX = longControls.TranslatePoint(default, view)!.Value.X;
                        Check(AppServices.BackHandler!(), "Long-title player cannot close");
                        await AppServices.Controller.PlayAsync(AppServices.Controller.Results[1]);
                        AppServices.OpenPlayerView!(); await Task.Delay(300);
                        var shortPlay = view.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("largePlay"));
                        var shortControls = (Grid)shortPlay.Parent!;
                        var transport = (Grid)shortControls.Parent!;
                        Check(Math.Abs(shortControls.Bounds.Center.X - transport.Bounds.Width / 2) < 3, "Short-title controls not centered within pixel rounding");
                        Check(longX > shortControls.TranslatePoint(default, view)!.Value.X + 20, "Long title did not push controls right");
                        Capture(view, "player-short-title");
                        var volumeButton = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Ajustar volume");
                        var volumeSlider = view.GetVisualDescendants().OfType<Slider>().Single(s => Avalonia.Automation.AutomationProperties.GetName(s) == "Volume");
                        Check(volumeSlider.IsEffectivelyVisible && volumeSlider.Bounds.Width == 100 && volumeSlider.Parent is StackPanel, "Volume not always displayed inline");
                        volumeSlider.Value = .35;
                        Check(Math.Abs(AppServices.Controller.Player.Volume - .35) < .001, "Volume slider not connected to player");
                        volumeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        Check(volumeSlider.IsEffectivelyVisible && volumeSlider.Bounds.Width == 100, "Volume button hid slider");
                        var volumeCenter = volumeSlider.TranslatePoint(new Point(0, volumeSlider.Bounds.Height / 2), view)!.Value.Y;
                        var buttonCenter = volumeButton.TranslatePoint(new Point(0, volumeButton.Bounds.Height / 2), view)!.Value.Y;
                        Check(Math.Abs(volumeCenter - buttonCenter) < 2, "Volume not vertically centered");
                        Check(volumeButton.GetVisualAncestors().Contains(shortControls), "Volume outside transport controls");
                        Capture(view, "player-inline-volume");
                        volumeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        var info = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Informações do áudio");
                        info.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
                        var overlay = (Border)typeof(MainView).GetField("_overlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        Check(Math.Abs(overlay.Bounds.Width - view.Bounds.Width) < 2, "Dialog scrim does not cover full window");
                        var sheet = (Border)overlay.Child!;
                        Check(Math.Abs(sheet.Bounds.Center.X - overlay.Bounds.Width / 2) < 2, "Dialog not centered");
                        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "160 kb/s"), "Audio bitrate missing from details");
                        Capture(view, "audio-info"); Check(AppServices.BackHandler!(), "Audio details cannot close");
                    }
                    Check(AppServices.BackHandler!(), "Player cannot close");
                    await Task.Delay(300);
                    var more = view.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Opções de ") == true);
                    more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                    var songPopup = (Avalonia.Controls.Primitives.Popup)typeof(MainView).GetField("_songPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                    Check(songPopup.IsOpen && songPopup.Child!.Bounds.Width <= 302, "Song menu not compact");
                    Capture(view, $"dialog-{width}"); Check(AppServices.BackHandler!(), "Dialog cannot close");
                }
                var music = AppServices.Controller!;
                await music.ToggleFavoriteAsync(music.Results[0]);
                await music.ToggleFavoriteAsync(new("local-filter", "Local favorite", "Local artist", "test", IsLocal: true));
                foreach (var width in new[] { 360, 1260 })
                {
                    window.Width = width;
                    var libraryNav = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "library");
                    libraryNav.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
                    var filter = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == "Filtrar título ou artista");
                    filter.Text = "Local favorite"; await Task.Delay(100);
                    var filtered = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.IsEffectivelyVisible);
                    Check(filtered.ItemsSource!.Cast<Track>().Single().Id == "local-filter", "Library filter did not isolate local favorite");
                    Check(filtered.GetVisualAncestors().OfType<ScrollViewer>().First().OpacityMask is null, "Non-scrolling content faded");
                    Capture(view, $"library-{width}"); filter.Text = "";
                    var brand = (Image)typeof(MainView).GetField("_brandIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                    Check(!brand.IsVisible, "Library incorrectly displays app brand");
                    if (width == 360) music.CreatePlaylist("Library visual check");
                    var sectionField = typeof(MainView).GetField("_librarySection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    sectionField.SetValue(view, "Playlists");
                    typeof(MainView).GetMethod("BuildListTools", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                    typeof(MainView).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                    await Task.Delay(150); Capture(view, $"library-playlists-{width}");
                    var libraryGrid = (WrapPanel)typeof(MainView).GetField("_libraryGroups", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                    Check(libraryGrid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Library visual check"), "Library playlist cover missing");
                    sectionField.SetValue(view, "Músicas");
                    var queueNav = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "queue");
                    queueNav.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150); Capture(view, $"queue-{width}");
                    if (width == 360) Check(!brand.IsVisible, "Queue incorrectly displays app brand");
                    if (width == 360)
                    {
                        window.Height = 640; await Task.Delay(150);
                        var upcoming = (ItemsControl)typeof(MainView).GetField("_upNext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        var queueScroll = upcoming.GetVisualAncestors().OfType<ScrollViewer>().First();
                        Check(queueScroll.Bounds.Height >= 120, "Portrait queue has no room for upcoming tracks");
                        var currentCard = (ContentControl)typeof(MainView).GetField("_queueCurrent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        Check(currentCard.IsVisible && currentCard.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "TOCANDO AGORA"), "Mobile queue current track missing");
                        Check(upcoming.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "4:28"), "Mobile queue missing library-style duration");
                        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Próximas músicas"), "Mobile queue heading missing");
                        foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Reproduzir da fila:") == true))
                            Check(button.Bounds.Width > 100 && button.TranslatePoint(default, view)!.Value.X + button.Bounds.Width <= view.Bounds.Width, "Portrait queue row clipped");
                        Capture(view, "queue-360-short"); window.Height = 820;
                    }
                }
                var extra = new Track("queue-edit", "Queue edit", "Artist", "queue-edit"); music.Enqueue(extra);
                music.EditQueue(extra, -1); Check(music.Queue.IndexOf(extra) == music.Queue.Count - 2, "Queue move failed");
                music.EditQueue(extra, 0); Check(!music.Queue.Contains(extra), "Queue remove failed");
                var current = music.Current!;
                var nextA = extra with { Id = "next-a", Source = "next-a" };
                var nextB = extra with { Id = "next-b", Source = "next-b" };
                music.ReplaceQueue([current, nextA, nextB]);
                music.EditQueue(nextA, -1); Check(music.Queue[0] == current && music.Queue[1] == nextA, "Upcoming track moved before current");
                music.ReorderQueue(true); Check(music.Queue[0] == current, "Shuffle displaced current track");
                typeof(MainView).GetMethod("OpenCollection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, ["Playlist de teste", new Track[] { nextA, nextB }]);
                await Task.Delay(150); Capture(view, "collection-1260");
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Playlist de teste" && t.IsEffectivelyVisible), "Collection page missing");
                Check(AppServices.BackHandler!(), "Collection cannot return home");
                music.SaveSession();
                var restored = new MusicController(new Catalog(), new Player(), new LibraryStore(directory), directory);
                await restored.InitializeAsync();
                Check(restored.Current == AppServices.Controller.Current && restored.Queue.SequenceEqual(AppServices.Controller.Queue), "Playback session not restored");
                Check(!restored.Player.IsPlaying, "Restored session autoplayed");
                restored.ClearQueue(); Check(restored.Queue.Count == 1 && restored.Queue[0] == restored.Current, "Clear queue lost current track");
                restored.StopObserving();
                Console.WriteLine("UI checks passed: 360/390/430, typography, touch targets, virtualization, player, session restore and clear queue.");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-result.txt"), "PASS: 360/390/430, typography, touch targets, virtualization, player.");
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-result.txt"), error.ToString()); }
            finally { lifetime.Shutdown(result); }
        }));
        builder.StartWithClassicDesktopLifetime([]);
        Directory.Delete(directory, true);
        return result;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Capture(Control control, string name)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height));
        bitmap.Render(control); bitmap.Save(Path.Combine(AppContext.BaseDirectory, name + ".png"));
    }
    private sealed class Catalog : IMusicCatalog
    {
        public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Track>>(Enumerable.Range(0, 1000).Select(i => new Track(i.ToString(), i == 1 ? "Artist - Short title" : "Artist - Clair Obscur: Expedition 33 (Original Soundtrack) — A deliberately long title for overflow and responsive layout checks (Official Video)", "Artist - Topic", "test", 267.8)).ToArray());
        public Task<AudioSource> ResolveAsync(Track track, CancellationToken cancellationToken) => Task.FromResult(new AudioSource("test", new Dictionary<string, string>(), BitrateKbps: 160, Codec: "opus", SampleRateHz: 48000, Channels: 2, Format: "251", Container: "webm"));
    }
    private sealed class RadioCatalog : IMusicCatalog
    {
        private int _search;
        public bool UseRadio { get; init; }
        public int RadioCalls { get; private set; }
        public int SearchCalls => _search;
        public Task<IReadOnlyList<Track>> RadioAsync(Track seed, CancellationToken token)
        {
            RadioCalls++;
            return Task.FromResult<IReadOnlyList<Track>>(UseRadio ? Enumerable.Range(0, 20).Select(i => new Track("radio" + i, "Radio song " + i, "Artist " + i % 4, "https://example.org/radio" + i, 180)).ToArray() : []);
        }
        public bool FailLaterSearches { get; init; }
        public Task<IReadOnlyList<Track>> SearchAsync(string query, CancellationToken token)
        {
            if (FailLaterSearches && _search > 0) throw new HttpRequestException("Simulated recommendation search failure");
            var start = _search++ * 20;
            return Task.FromResult<IReadOnlyList<Track>>(Enumerable.Range(start, 20)
                .Select(i => new Track(i.ToString(), "Song " + i, "Artist " + i % 4, "https://example.org/" + i, 180)).ToArray());
        }
        public Task<AudioSource> ResolveAsync(Track track, CancellationToken token) => Task.FromResult(new AudioSource("test", new Dictionary<string, string>()));
    }
    private sealed class Player : IAudioPlayer
    {
        public bool IsPlaying { get; private set; }
        public double Position => 0;
        public double Duration => 267.7;
        public double Volume { get; set; }
        public event EventHandler? Ended { add { } remove { } }
        public event EventHandler<string>? Failed { add { } remove { } }
        public Task PlayAsync(AudioSource source, CancellationToken cancellationToken) { IsPlaying = true; return Task.CompletedTask; }
        public void Pause() => IsPlaying = false;
        public void Resume() => IsPlaying = true;
        public void Seek(double seconds) { }
        public void Dispose() { }
    }
}
