using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

// Native Windows check. No network, real audio, or changes to the user's library.
internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hibike-ui-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (args.Contains("--queue-only") || args.Contains("--performance-study") || args.Contains("--performance-checks")
            || args.Contains("--collapsing-top-bars"))
        {
            // Keep onboarding and home discovery offline before the view is attached.
            File.WriteAllText(Path.Combine(directory, "personalization.json"), "{\"OnboardingCompleted\":true}");
            File.WriteAllText(Path.Combine(directory, "discovery-cache.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                SavedUtc = DateTime.UtcNow, Region = "BR", Recommendations = Array.Empty<Track>(),
                Related = Array.Empty<Track>(), Seed = "", Regional = (MusicChart?)null, Global = (MusicChart?)null
            }));
        }
        AppServices.Configure(new Catalog(), new Player(), directory);
        AppServices.ConnectYouTubeAccount = () => Task.FromException<YouTubeAccountLibrary>(new OperationCanceledException());
        var result = 1;
        var builder = AppBuilder.Configure<Hibike.App.App>().WithInterFont();
        if (args.Contains("--headless"))
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        else builder.UsePlatformDetect();
        builder.AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
        {
            var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            try
            {
                var window = lifetime.MainWindow!; var view = (MainView)window.Content!;
                if (args.Contains("--collapsing-top-bars"))
                {
                    await CheckCollapsingTopBarsAsync(window);
                    Console.WriteLine("Android collapsing top bar checks passed.");
                    result = 0;
                    return;
                }
                if (args.Contains("--performance-checks"))
                {
                    await RunPerformanceStudyAsync(view, window);
                    await RunOptimizationChecksAsync(view, window);
                    Console.WriteLine("Performance, artwork quality and persistence checks passed.");
                    result = 0;
                    return;
                }
                if (args.Contains("--performance-study"))
                {
                    await RunPerformanceStudyAsync(view, window);
                    result = 0;
                    return;
                }
                if (args.Contains("--queue-only"))
                {
                    await RunQueueRegressionChecksAsync(directory, view, window);
                    await RunRecoveryChecksAsync(directory, view);
                    await CheckHomeInertiaAsync(view);
                    result = 0;
                    Console.WriteLine("Queue, radio and home inertia regression checks passed.");
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-result.txt"), "PASS: queue/radio recovery, concurrent requests, manual queue edits, session restore and desktop/mobile controls.");
                    return;
                }
                await Task.Delay(300);
                var welcomeField = typeof(MainView).GetField("_onboarding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var loginField = typeof(MainView).GetField("_googleWelcome", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Check(loginField.GetValue(view) is Border, "Branded Google welcome missing");
                var initialWidth = window.Width; var initialHeight = window.Height;
                window.Width = 360; window.Height = 800; await Task.Delay(100); Capture(view, "music-onboarding-360");
                var welcome = (Border)loginField.GetValue(view)!;
                Check(welcome.GetVisualDescendants().OfType<Image>().Any(), "Hibike logo missing from login");
                Check(welcomeField.GetValue(view) is null, "Duplicate onboarding login visible");
                Check(!welcome.GetVisualDescendants().OfType<TextBox>().Any(b => b.IsEffectivelyVisible), "Artist stage visible before login choice");
                welcome.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Entrar com Google").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(50);
                Check(welcome.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Login cancelado") == true), "Login cancellation missing");
                welcome.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Continuar sem conta").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(50);
                welcome = (Border)welcomeField.GetValue(view)!;
                var artistCards = welcome.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.ItemSpacing == 18 && p.IsVisible);
                for (var i = 0; i < 8; i++) artistCards.Children.Add(new Button { BorderThickness = new Thickness(i == 0 ? 2 : 0) });
                foreach (var width in new[] { 320, 360, 390, 430 })
                {
                    window.Width = width; await Task.Delay(100);
                    Check(Math.Abs(artistCards.Children[0].Bounds.Y - artistCards.Children[1].Bounds.Y) < 1, "Onboarding lost two columns");
                    Check(artistCards.Children[2].Bounds.Y > artistCards.Children[0].Bounds.Y, "Onboarding did not wrap after two tiles");
                    Check(artistCards.Children.All(c => Math.Abs(c.Bounds.Width - c.Bounds.Height) < 1 && c.Bounds.Right <= artistCards.Bounds.Width + 1), "Onboarding tile overflows or is not square");
                }
                artistCards.Children.Clear(); window.Width = 360;
                Check(!view.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && (b.Content as string)?.Contains("Google") == true), "Google login visible");
                Check(!welcome.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Continuar").IsEnabled, "Continue allowed fewer than three artists");
                welcome.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Pular")
                     .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(50);
                await AppServices.Controller!.FlushPendingSavesAsync();
                var artistsBeforeEdit = AppServices.Controller.FavoriteArtists.ToArray();
                var genresBeforeEdit = AppServices.Controller.FavoriteGenres.ToArray();
                for (var attempt = 0; attempt < 100 && welcomeField.GetValue(view) is not null; attempt++) await Task.Delay(20);
                Check(welcomeField.GetValue(view) is null, "Initial personalization did not finish saving");
                typeof(MainView).GetMethod("ShowMusicOnboarding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, [true]);
                var editPanel = (Border)welcomeField.GetValue(view)!;
                Check(Avalonia.Input.KeyboardNavigation.GetTabNavigation(editPanel) == Avalonia.Input.KeyboardNavigationMode.Cycle, "Personalization focus escapes overlay");
                Check(!editPanel.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Pular"), "Editing exposes destructive skip");
                editPanel.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancelar")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Check(welcomeField.GetValue(view) is null, "Cancel did not close personalization");
                Check(AppServices.Controller.FavoriteArtists.SequenceEqual(artistsBeforeEdit)
                    && AppServices.Controller.FavoriteGenres.SequenceEqual(genresBeforeEdit), "Cancel changed saved preferences");
                var restoreSession = typeof(MainView).GetMethod("RestoreAccountAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var loadingMethod = typeof(MainView).GetMethod("WithHeaderLoadingAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var loadingBanner = (Border)typeof(MainView).GetField("_loadingBanner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                var header = (Grid)loadingBanner.Parent!;
                var refreshContainer = (RefreshContainer)typeof(MainView).GetField("_homeRefreshContainer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                var loadingRing = (ContentControl)typeof(MainView).GetField("_loadingRing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                bool RingAnimating() => loadingRing.Content is Avalonia.Controls.Shapes.Ellipse { Styles.Count: > 0 };
                var loadingLabel = loadingBanner.GetVisualDescendants().OfType<TextBlock>().Single();
                Check(header.Name == "AppHeader" && Grid.GetRow(loadingBanner) == 1 && loadingBanner.ClipToBounds, "Loading indicator is not contained in the app header");
                Check(refreshContainer.Visualizer?.Opacity == 0, "Default refresh spinner still visible");
                var minimumWindowWidth = window.MinWidth;
                window.MinWidth = 320;
                window.Width = 320;
                view.UpdateLayout();
                var homeBounds = refreshContainer.Bounds;
                var headerBounds = header.Bounds;
                var pendingLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var loadingTask = (Task)loadingMethod.Invoke(view, ["Atualizando dados…", (Func<Task>)(() => pendingLoad.Task), false])!;
                view.UpdateLayout();
                Check(loadingRing.Bounds.Width == 16 && loadingBanner.IsVisible && !loadingBanner.IsHitTestVisible && RingAnimating(), "Animated loading missing or blocks navigation");
                Check(header.Bounds == headerBounds && refreshContainer.Bounds == homeBounds, "Loading displaced the header or home content");
                Check(loadingLabel.TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis && loadingLabel.DesiredSize.Width <= loadingLabel.Bounds.Width && loadingLabel.Bounds.Right <= loadingBanner.Bounds.Width, "Loading text overflows the header");

                var restoreLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var restoreTask = (Task)loadingMethod.Invoke(view, ["Restaurando conta Google…", (Func<Task>)(() => restoreLoad.Task), false])!;
                restoreLoad.SetResult(); await restoreTask;
                Check(loadingBanner.IsVisible && loadingLabel.Text == "Atualizando dados…", "Overlapping loading cleared the active refresh");
                await Task.Delay(200);
                Capture(view, "circular-header-320");
                pendingLoad.SetResult(); await loadingTask;
                Check(!loadingBanner.IsVisible && !RingAnimating(), "Completed loading or animation stuck");

                AppServices.Controller!.SetReduceMotion(true);
                var reducedLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var reducedTask = (Task)loadingMethod.Invoke(view, ["Atualizando dados…", (Func<Task>)(() => reducedLoad.Task), false])!;
                Check(loadingBanner.IsVisible && !RingAnimating() && loadingRing.Content is Avalonia.Controls.Shapes.Ellipse, "Reduced motion did not use a static indicator");
                reducedLoad.SetException(new IOException("Expected test error"));
                try { await reducedTask; } catch (IOException) { }
                Check(!loadingBanner.IsVisible, "Failed loading message stuck");
                AppServices.Controller.SetReduceMotion(false);
                window.MinWidth = minimumWindowWidth;
                window.Width = 360;
                var restoreAttempted = typeof(MainView).GetField("_accountRestoreAttempted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                restoreAttempted.SetValue(view, false);
                AppServices.RestoreYouTubeAccount = null;
                await (Task)restoreSession.Invoke(view, null)!;
                Check(!(bool)restoreAttempted.GetValue(view)!, "Missing storage hook permanently consumed restore attempt");
                var restoreCalls = 0;
                AppServices.RestoreYouTubeAccount = () => { restoreCalls++; return Task.FromResult<YouTubeAccountLibrary?>(null); };
                await (Task)restoreSession.Invoke(view, null)!;
                Check(restoreCalls == 1, "Restore did not retry after platform storage became ready");
                Check(header.Bounds.Height <= 48, "App header exceeds compact height");
                Check(header.GetVisualDescendants().OfType<Image>().Any(i => i.Width == 22 && i.Height == 22), "Compact header logo missing");
                var profileButton = header.Children.OfType<Button>().Single(b => b.IsVisible);
                Check(profileButton.Bounds.Height == 32, "Desktop header not compact");
                for (var attempt = 0; attempt < 100 && welcomeField.GetValue(view) is not null; attempt++) await Task.Delay(20);
                Check(File.ReadAllText(Path.Combine(directory, "personalization.json")).Contains("\"OnboardingCompleted\":true"), "Onboarding choice not persisted");
                var welcomeMethod = typeof(MainView).GetMethod("ShowMusicOnboarding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                welcomeMethod.Invoke(view, [false]); Check(welcomeField.GetValue(view) is null, "Onboarding repeats after completion");
                var profileDirectory = Path.Combine(directory, "music-profile");
                var profile = new MusicController(new Catalog(), new Player(), new LibraryStore(profileDirectory), profileDirectory);
                profile.Volume = .37;
                var volumeRestored = new MusicController(new Catalog(), new Player(), new LibraryStore(profileDirectory), profileDirectory);
                Check(Math.Abs(volumeRestored.Volume - .37) < .000001, "User volume did not survive restart");
                volumeRestored.Volume = 0;
                var mutedRestored = new MusicController(new Catalog(), new Player(), new LibraryStore(profileDirectory), profileDirectory);
                Check(mutedRestored.Volume == 0, "Muted volume did not survive restart");
                mutedRestored.Volume = double.NaN; Check(mutedRestored.Volume == 0, "Invalid volume accepted");
                var artists = new[] { new MusicArtist("UCabcdefghijklmnopqrstuv", "Artist A", null), new MusicArtist("UCbcdefghijklmnopqrstuvw", "Artist B", null), new MusicArtist("UCcdefghijklmnopqrstuvwx", "Artist C", null) };
                var invalidSelection = false;
                try { await profile.CompleteOnboardingAsync(artists.Take(2), []); } catch (ArgumentException) { invalidSelection = true; }
                Check(invalidSelection && !profile.OnboardingCompleted, "Invalid artist selection accepted");
                await profile.CompleteOnboardingAsync(artists, ["Rock", "J-Pop"]); profile.StopObserving(); await profile.FlushPendingSavesAsync();
                var restoredProfile = new MusicController(new Catalog(), new Player(), new LibraryStore(profileDirectory), profileDirectory);
                Check(restoredProfile.OnboardingCompleted && restoredProfile.FavoriteArtists.Count == 3 && restoredProfile.FavoriteGenres.Contains("J-Pop"), "Music profile did not survive restart");
                restoredProfile.StopObserving(); await restoredProfile.FlushPendingSavesAsync();
                var blockedPath = Path.Combine(directory, "blocked-profile");
                File.WriteAllText(blockedPath, "not a directory");
                var blockedProfile = new MusicController(new Catalog(), new Player(), new LibraryStore(blockedPath), blockedPath);
                var saveFailed = false;
                try { await blockedProfile.CompleteOnboardingAsync(artists, []); } catch (IOException) { saveFailed = true; }
                Check(saveFailed && !blockedProfile.OnboardingCompleted && blockedProfile.FavoriteArtists.Count == 0, "Failed onboarding save did not roll back");
                blockedProfile.StopObserving();
                try { await blockedProfile.FlushPendingSavesAsync(); } catch (IOException) { }
                await CheckStorageAndMotionAsync(directory, view);
                window.Width = initialWidth; window.Height = initialHeight; await Task.Delay(100);
                var toolsEnabled = (bool)typeof(MainView).Assembly.GetType("Hibike.App.DeveloperFeatures")!.GetProperty("Enabled")!.GetValue(null)!;
                typeof(MainView).GetMethod("ShowSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                await Task.Delay(100);
                Check(typeof(MainView).GetField("_overlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view) is null, "Settings still use modal menu");
                Check(!view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Região dos rankings · ZZ = mundo"), "Retired chart settings visible");
                Check(!view.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && b.Content as string == "Limpar feedback Não tenho interesse"), "Retired feedback reset visible");
                Check(view.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b) == "Diagnósticos"), "Diagnostics missing from settings");
                Check(view.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b) == "Laboratório experimental") == toolsEnabled, "Developer lab visibility incorrect");
                Check(header.IsEffectivelyVisible && header.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible
                    && Avalonia.Automation.AutomationProperties.GetName(button) == "Voltar"), "Settings lost the top bar or its back button");
                Check(view.GetVisualDescendants().OfType<TextBlock>().Count(label => label.IsEffectivelyVisible && label.Text == "Configurações") == 1,
                    "Settings title duplicated or missing");
                foreach (var width in new[] { 360, 1260 })
                {
                    window.Width = width; await Task.Delay(80);
                    Capture(view, $"settings-page-{width}");
                    Check(view.GetVisualDescendants().OfType<Slider>().Any(s => s.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(s) == "Volume nas configurações"), "Settings playback controls missing");
                }
                window.MinWidth = 320;
                window.Width = 320;
                var tokenStorage = AppServices.DiscordTokenStorage;
                AppServices.DiscordTokenStorage = (() => null, _ => { }, () => { }, () => true);
                var showDiscord = typeof(MainView).GetMethod("ShowDiscordAccountAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                await (Task)showDiscord.Invoke(view, null)!;
                await Task.Delay(200);
                var discordSheet = (Border)typeof(MainView).GetField("_overlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                var discordControls = discordSheet.GetVisualDescendants().OfType<Button>().ToArray();
                var connectDiscord = discordControls.Single(button => Equals(button.Content, "Conectar"));
                var savedDiscord = discordControls.Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Usar token salvo");
                var disconnectDiscord = discordControls.Single(button => Equals(button.Content, "Desconectar e remover token"));
                Capture(view, "discord-320");
                Check(Math.Abs(connectDiscord.Bounds.Width - savedDiscord.Bounds.Width) < 2 && connectDiscord.Bounds.Y == savedDiscord.Bounds.Y
                    && Math.Abs(connectDiscord.Bounds.Height - savedDiscord.Bounds.Height) < 2 && Math.Abs(disconnectDiscord.Bounds.Width
                        - connectDiscord.Bounds.Width - savedDiscord.Bounds.Width - 8) < 2,
                    $"Discord action grid: connect {connectDiscord.Bounds}, saved {savedDiscord.Bounds}, disconnect {disconnectDiscord.Bounds}");
                Check(AppServices.BackHandler!(), "Discord sheet cannot close");
                AppServices.DiscordTokenStorage = tokenStorage;
                window.MinWidth = minimumWindowWidth;
                window.Width = 1260;
                await Task.Delay(200);
                PlaybackDiagnostics.Write(DiagnosticSector.Discord, "Teste", "Evento Discord para cópia.");
                PlaybackDiagnostics.Write(DiagnosticSector.Google, "Teste", "Evento Google para cópia.");
                var diagnosticsLink = view.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible
                    && Avalonia.Automation.AutomationProperties.GetName(button) == "Diagnósticos");
                diagnosticsLink.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                var diagnosticsOverlay = (Border)typeof(MainView).GetField("_overlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                var sectorSelector = diagnosticsOverlay.GetVisualDescendants().OfType<ComboBox>().Single();
                sectorSelector.SelectedItem = "Conta Discord";
                var report = diagnosticsOverlay.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
                Check(report.Text?.Contains("Evento Discord para cópia.") == true && !report.Text.Contains("Evento Google"),
                    "Diagnostics did not isolate the selected sector");
                if (args.Contains("--headless") && window.Clipboard is { } clipboard)
                {
                    var copySector = diagnosticsOverlay.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Copiar setor"));
                    copySector.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(50);
                    var copied = await clipboard.TryGetTextAsync();
                    Check(copied?.Contains("Evento Discord para cópia.") == true && !copied.Contains("Evento Google"),
                        "Copy sector included unrelated diagnostics");
                    var copyAll = diagnosticsOverlay.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Copiar todos"));
                    copyAll.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(50);
                    copied = await clipboard.TryGetTextAsync();
                    Check(copied?.Contains("Evento Discord para cópia.") == true && copied.Contains("Evento Google para cópia."),
                        "Copy all omitted a sector");
                }
                Capture(view, "diagnostics");
                Check(AppServices.BackHandler!(), "Diagnostics cannot close");
                await Task.Delay(200);
                AppServices.BackHandler!();
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Hibike"), "Leaving settings did not restore home");
                Check(!view.GetVisualDescendants().OfType<Slider>().Any(s => s.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(s) == "Volume nas configurações"), "Settings leaked into home");
                await (Task)typeof(MainView).GetMethod("ShowAccountAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null)!;
                await Task.Delay(80); Capture(view, "account-page");
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Sem conta conectada"), "Account page missing");
                Check(typeof(MainView).GetField("_overlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view) is null, "Account still uses modal menu");
                Check(AppServices.BackHandler!(), "Account cannot return home");
                typeof(MainView).GetMethod("ShowSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                var homeNavigation = view.GetVisualDescendants().OfType<Button>().First(b => b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "home" && b.IsEffectivelyVisible);
                homeNavigation.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Check(!view.GetVisualDescendants().OfType<Slider>().Any(s => s.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(s) == "Volume nas configurações"), "Home navigation left settings visible");
                var refreshingHome = typeof(MainView).GetField("_refreshingHome", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var accountRestoreFailed = typeof(MainView).GetField("_accountRestoreFailed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var pendingAccount = new TaskCompletionSource<YouTubeAccountLibrary?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pullCalls = 0;
                AppServices.RestoreYouTubeAccount = () => { pullCalls++; return pendingAccount.Task; };
                accountRestoreFailed.SetValue(view, true);
                refreshContainer.RequestRefresh();
                for (var attempt = 0; pullCalls == 0 && attempt < 50; attempt++) await Task.Delay(10);
                Check(pullCalls == 1 && loadingBanner.IsVisible && RingAnimating(), "Pull did not start the animated header refresh");
                Check(refreshContainer.Visualizer!.GetValue(RefreshVisualizer.RefreshVisualizerStateProperty) == RefreshVisualizerState.Idle, "Pull stayed active while waiting for the network");
                refreshContainer.RequestRefresh();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Check(pullCalls == 1 && (bool)refreshingHome.GetValue(view)!, "Repeated pull did not share the pending refresh");
                pendingAccount.SetException(new HttpRequestException("Expected offline error"));
                for (var attempt = 0; (bool)refreshingHome.GetValue(view)! && attempt < 50; attempt++) await Task.Delay(10);
                Check(!(bool)refreshingHome.GetValue(view)! && !loadingBanner.IsVisible, "Failed pull remained busy");

                AppServices.RestoreYouTubeAccount = () => { pullCalls++; return Task.FromResult<YouTubeAccountLibrary?>(null); };
                refreshContainer.RequestRefresh();
                for (var attempt = 0; (pullCalls == 1 || (bool)refreshingHome.GetValue(view)!) && attempt < 50; attempt++) await Task.Delay(10);
                Check(pullCalls == 2 && !(bool)refreshingHome.GetValue(view)! && !loadingBanner.IsVisible, "Pull did not recover after the failure");
                await CheckOfflineWorkspaceAsync(view, directory);
                window.Width = initialWidth;
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
                primaryRadio.StopObserving(); await primaryRadio.FlushPendingSavesAsync();
                Check(!(bool)sameRadioSong.Invoke(null, [seedSong, new Track("other", "The Manor - Our Painted Family", "Lorien Testard", "other")])!, "Radio rejected different song by same artist");
                var radioDirectory = Path.Combine(directory, "radio"); Directory.CreateDirectory(radioDirectory);
                var radio = new MusicController(new RadioCatalog(), new Player(), new LibraryStore(radioDirectory), radioDirectory);
                await radio.SearchAsync("seed"); await radio.PlayAsync(radio.Results[0]);
                Check(radio.Queue.Count == 11, "Radio did not add ten tracks from empty upcoming queue");
                Check(radio.Queue.Skip(1).Zip(radio.Queue.Skip(2)).All(pair => pair.First.Artist != pair.Second.Artist), "Radio failed to diversify adjacent artists");
                for (var i = 0; i < 10; i++) await radio.NextAsync();
                Check(radio.Queue.Count == 21 && radio.Queue.Select(t => t.Source).Distinct().Count() == 21, "Radio refill repeated tracks or failed");
                radio.ClearQueue(); await radio.NextAsync();
                Check(radio.Queue.Count == 1, "Radio ignored explicit clear queue"); radio.StopObserving(); await radio.FlushPendingSavesAsync();
                var singleRadio = new MusicController(new RadioCatalog { FailLaterSearches = true }, new Player(), new LibraryStore(radioDirectory), radioDirectory);
                var single = new Track("single", "Seed", "Artist", "https://example.org/single", 180);
                singleRadio.Enqueue(single);
                await singleRadio.PlayAsync(single);
                Check(singleRadio.Queue.Count == 11, "Single queued track outside search results did not receive partial radio suggestions");
                singleRadio.StopObserving(); await singleRadio.FlushPendingSavesAsync();
                singleRadio.CreatePlaylist("Test mix");
                var localPlaylist = singleRadio.Playlists.Single();
                singleRadio.AddToPlaylist(localPlaylist, single); singleRadio.AddToPlaylist(localPlaylist, single);
                singleRadio.PinPlaylist(localPlaylist); singleRadio.TogglePin(single);
                singleRadio.RenamePlaylist(localPlaylist, "Renamed mix");
                Check(localPlaylist.Tracks.Count == 1 && localPlaylist.Pinned && singleRadio.Pins.Count == 1, "Playlist deduplication/pins failed");
                await singleRadio.FlushPendingSavesAsync();
                var restoredPersonal = new MusicController(new RadioCatalog(), new Player(), new LibraryStore(radioDirectory), radioDirectory);
                Check(restoredPersonal.Playlists.Single().Name == "Renamed mix" && restoredPersonal.Pins.Count == 1, "Personalization persistence failed");
                restoredPersonal.StopObserving(); await restoredPersonal.FlushPendingSavesAsync();
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
                using (var ready = System.Text.Json.JsonDocument.Parse("""
                    {"d":{"user":{"id":"123456789012345678","username":"listener","global_name":"Listener","avatar":"00112233445566778899aabbccddeeff"}}}
                    """))
                {
                    var discordProfile = presenceType.GetMethod("ReadProfile", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(null, [ready.RootElement])!;
                    Check(Equals(discordProfile.GetType().GetProperty("Username")!.GetValue(discordProfile), "listener")
                        && Equals(discordProfile.GetType().GetProperty("Avatar")!.GetValue(discordProfile),
                            "https://cdn.discordapp.com/avatars/123456789012345678/00112233445566778899aabbccddeeff.png?size=128"),
                        "Discord READY did not expose the authenticated profile");
                }
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
                const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var artistProfile = new ArtistProfile("UCabcdefghijklmnopqrstuv", "Layout artist", null, [],
                    Enumerable.Range(0, 8).Select(i => new ArtistCollection("MPRE" + i, "Album " + i, "Álbum", null)).ToArray(), []);
                typeof(MainView).GetMethod("ShowArtistCatalog", hidden)!.Invoke(view, [new MusicArtist(artistProfile.Id, artistProfile.Name, null), artistProfile]);
                await Task.Delay(100);
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Álbuns"), "Artist album shelf missing");
                Check(view.GetVisualDescendants().OfType<ScrollViewer>().Any(s => s.IsEffectivelyVisible && s.HorizontalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden), "Artist horizontal shelf missing");
                Capture(view, "artist-catalog");
                var route = typeof(MainView).GetField("_page", hidden)!;
                var loadingQuery = (TextBox)typeof(MainView).GetField("_search", hidden)!.GetValue(view)!;
                loadingQuery.Text = "teste";
                route.SetValue(view, "explore");
                typeof(MainView).GetField("_mobileSearchAttempt", hidden)!.SetValue(view, loadingQuery.Text);
                var searchingField = typeof(MusicController).GetField("<Searching>k__BackingField", hidden)!;
                searchingField.SetValue(AppServices.Controller, true);
                typeof(MainView).GetMethod("Refresh", hidden)!.Invoke(view, null);
                await Task.Delay(100);
                var searchState = (ContentControl)typeof(MainView).GetField("_mobileSearchState", hidden)!.GetValue(view)!;
                Check(Equals(searchState.Tag, "busy") && searchState.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Any(), "Search loading missing");
                Capture(view, "search-loading");
                typeof(MainView).GetField("_artistResultsQuery", hidden)!.SetValue(view, loadingQuery.Text);
                typeof(MainView).GetField("_foundArtists", hidden)!.SetValue(view, new[] { new MusicArtist(artistProfile.Id, artistProfile.Name, null) });
                typeof(MainView).GetMethod("SelectSearchKind", hidden)!.Invoke(view, ["Artistas"]);
                await Task.Delay(100);
                Check(!searchState.IsVisible && view.GetVisualDescendants().OfType<Button>().Any(b =>
                    b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b) == "Layout artist · Artista"),
                    "A pending track request hid the available artist result");
                typeof(MainView).GetMethod("SelectSearchKind", hidden)!.Invoke(view, ["Tudo"]);
                searchingField.SetValue(AppServices.Controller, false);
                loadingQuery.Text = "";
                route.SetValue(view, "home");
                typeof(MainView).GetMethod("Refresh", hidden)!.Invoke(view, null);
                Check(!searchState.IsEffectivelyVisible, "Search loading remained after completion");
                Check(!view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Continuar ouvindo" && t.IsEffectivelyVisible), "Empty history shelf visible");
                AppServices.Controller!.History.Add(new("home-check", "Faixa recente", "Artista", "test"));
                AppServices.Controller.SetStatus("Home check");
                await Task.Delay(100);
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Continuar ouvindo"), "Home listening history shelf missing");
                foreach (var homeWidth in new[] { 360, 1260 })
                {
                    window.Width = homeWidth; await Task.Delay(150); Capture(view, $"home-{homeWidth}");
                    if (homeWidth == 360)
                    {
                        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Hibike"), "Compact Windows missing app header");
                        Check(view.GetVisualDescendants().OfType<Button>().Count(b => b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b) == "Perfil · sem conta") == 1, "Compact Windows missing single profile button");
                        foreach (var tab in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item4))
                        {
                            var content = (StackPanel)tab.Content!;
                            var indicator = content.Children[0]; var caption = content.Children[1];
                            Check(Math.Abs(indicator.Bounds.Center.X - caption.Bounds.Center.X) < 1, "Mobile navigation label not centered under icon");
                        }
                    }
                    if (homeWidth == 1260)
                    {
                        var import = view.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(b) == "Importar músicas");
                        var playlist = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Nova playlist");
                        Check(import.Bounds.Width == playlist.Bounds.Width && import.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Center, "Sidebar actions not aligned");
                        var cache = typeof(MainView).Assembly.GetType("Hibike.App.Views.ArtworkCache")!;
                        var acquire = cache.GetMethod("AcquireAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
                        var url = new Uri(Path.Combine(AppContext.BaseDirectory, "home-1260.png")).AbsoluteUri;
                        var load = (Task)acquire.Invoke(null, [url, url, 128, CancellationToken.None])!;
                        await load;
                        using var lease = (IDisposable)load.GetType().GetProperty("Result")!.GetValue(load)!;
                        var thumbnail = (Bitmap)lease.GetType().GetProperty("Bitmap")!.GetValue(lease)!;
                        Check(thumbnail.PixelSize.Width == 128, "Artwork decode ignored thumbnail size");
                        var artworkType = typeof(MainView).Assembly.GetType("Hibike.App.Views.Artwork")!;
                        var sample = new Track("aspect", "Aspect", "Test", "aspect", Thumbnail: new Uri(Path.Combine(AppContext.BaseDirectory, "home-1260.png")).AbsoluteUri);
                        var art = (Border)Activator.CreateInstance(artworkType, [sample, 160d, false, Avalonia.Media.Stretch.Uniform, true, false])!;
                        var homeSections = (System.Collections.ObjectModel.ObservableCollection<Control>)typeof(MainView).GetField("_homeSections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        homeSections.Insert(0, art); await Task.Delay(300);
                        Check(Math.Abs(art.Height - 160d * 820 / 1260) < 2 && art.BorderThickness == default, "Natural artwork kept square frame");
                        homeSections.Remove(art);
                    }
                    var card = view.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Faixa recente — Artista");
                    Check(!view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text is "Em alta no Brasil" or "Em alta no mundo" or "Bombando agora" or "Tendências para você"), "Retired chart shelf visible");
                    var cardHost = (Grid)card.Parent!;
                    var shelf = cardHost.GetVisualAncestors().OfType<StackPanel>().First(p => p.Tag is ValueTuple<string, string>);
                    typeof(MainView).GetField("_homeFingerprint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, null);
                    AppServices.Controller.SetStatus("Home stable refresh"); await Task.Delay(50);
                    Check(cardHost.GetVisualAncestors().Contains(shelf), "Unchanged home shelf recreated on refresh");
                    var cardArtwork = card.GetVisualDescendants().OfType<Border>().Single(b => b.GetType().Name == "Artwork");
                    Check(cardArtwork.Width == cardArtwork.Height && cardArtwork.CornerRadius == new CornerRadius(16) && cardArtwork.BorderThickness == default, "Home artwork not square with subtle corners");
                    Check(card.GetVisualDescendants().OfType<Image>().All(image => image.Stretch == Avalonia.Media.Stretch.UniformToFill), "Home artwork does not fill square");
                    Check(cardHost.Margin.Right == 18, "Home carousel spacing inconsistent");
                    if (view.Bounds.Width == 360)
                    {
                        var toggleTopMenu = typeof(MainView).GetMethod("ToggleTopMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        toggleTopMenu.Invoke(view, null); await Task.Delay(300);
                        var topMenu = (Grid)typeof(MainView).GetField("_topMenuLayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        Check(topMenu.IsVisible && topMenu.Children[0].Bounds.Height > 100 && topMenu.Children[0].Bounds.Width <= view.Bounds.Width, "Top menu clipped or collapsed");
                        var menuNames = topMenu.GetVisualDescendants().OfType<Button>().Select(Avalonia.Automation.AutomationProperties.GetName).ToArray();
                        Check(menuNames.Length >= 6 && menuNames.Contains("Personalizar meu som") && menuNames.Contains("Atualizar recomendações"),
                            "Home actions missing from the profile menu");
                        Capture(view, "top-menu-360"); Check(AppServices.BackHandler!(), "Top menu ignores back"); await Task.Delay(300);
                        Check(!topMenu.IsVisible, "Top menu did not close");
                    }
                    Check(cardHost.Children.OfType<Button>().Where(b => b != card).All(b => !b.IsVisible), "Card menu visible without hover/focus");
                    var cardActions = cardHost.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("cardAction")).ToArray();
                    Check(cardActions.Length == 2 && cardActions.All(b => b.Width == 48 && b.Height == 48 && b.Padding == default && b.BorderThickness == default && b.CornerRadius == new CornerRadius(22)), "Card actions differ in size or circle style");
                    Check(card.Bounds.Width >= 160 && card.IsEffectivelyVisible, "Home history cover card missing");
                    foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && !b.GetVisualAncestors().OfType<Slider>().Any()))
                        Check(button.Bounds.Height >= 48 || ReferenceEquals(button, profileButton), "Home touch target below 48");
                }
                typeof(MainView).GetField("_page", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, "explore");
                var searchField = (TextBox)typeof(MainView).GetField("_search", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                searchField.Text = "test";
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
                        Check(button.Bounds.Height >= 48 || ReferenceEquals(button, profileButton), $"Touch target below 48: {button.Content} {button.Classes} {button.Bounds}");
                    var list = (ItemsControl)typeof(MainView).GetField("_mobileSearchItems", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
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
                    for (var attempt = 0; attempt < 20; attempt++)
                    {
                        scrollingList.Offset = new Vector(0, scrollingList.Extent.Height);
                        await Task.Delay(20);
                        if (scrollingList.Offset.Y >= scrollingList.Extent.Height - scrollingList.Viewport.Height - 1) break;
                    }
                    await Task.Delay(100);
                    mask = (Avalonia.Media.LinearGradientBrush)scrollingList.OpacityMask!;
                    Check(mask.GradientStops[0].Color.A == 64 && mask.GradientStops[^1].Color.A == 255, "List fade incorrect at end");
                    scrollingList.Offset = default; await Task.Delay(50);
                    var search = searchField;
                    search.Text = "test"; await Task.Delay(30);
                    var clear = (Control)search.InnerRightContent!;
                    Check(clear.IsVisible, "Search clear button missing");
                    search.Text = ""; await Task.Delay(30);
                    Check(!clear.IsVisible, "Empty search has clear button");
                    await AppServices.Controller.PlayAsync(AppServices.Controller.Results[0]);
                    await Task.Delay(100); Capture(view, $"mini-{width}");
                    if (width < 1000)
                    {
                        var mini = (Border)typeof(MainView).GetField("_mini", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        var main = (Border)typeof(MainView).GetField("_main", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        Check(Grid.GetRowSpan(main) == 2 && main.Bounds.Bottom >= mini.Bounds.Bottom,
                            "Mini player reserves an opaque bottom strip instead of overlaying content");
                        Check(scrollingList.Content is Control scrollContent && scrollContent.Margin.Bottom >= mini.Bounds.Height + mini.Margin.Bottom,
                            "Floating mini player has no scroll-end clearance");
                        if (width == 360)
                        {
                            search.Text = "test";
                            await AppServices.Controller.SearchAsync("test");
                            await Task.Delay(100);
                            for (var attempt = 0; attempt < 20; attempt++)
                            {
                                scrollingList.Offset = new Vector(0, scrollingList.Extent.Height);
                                await Task.Delay(20);
                            }
                            var lastRow = list.GetRealizedContainers().Last();
                            var lastBottom = lastRow.TranslatePoint(new Point(0, lastRow.Bounds.Height), view)!.Value.Y;
                            var miniTop = mini.TranslatePoint(default, view)!.Value.Y;
                            Check(lastBottom <= miniTop + 1, $"Last search row hidden by mini player: {lastBottom} > {miniTop}");
                            Check(scrollingList.TranslatePoint(new Point(0, scrollingList.Bounds.Height), view)!.Value.Y > miniTop,
                                "Search viewport stops above floating mini player");
                            Capture(view, "mini-scroll-end-360");
                            scrollingList.Offset = default;
                            await Task.Delay(50);
                            Capture(view, "mini-scroll-behind-360");
                            search.Text = "";
                        }
                        var cover = (ContentControl)typeof(MainView).GetField("_nowArt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        var title = (TextBlock)typeof(MainView).GetField("_nowTitle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        var miniTimeline = mini.GetVisualDescendants().OfType<ProgressBar>().Single();
                        var artOrigin = cover.TranslatePoint(default, mini)!.Value;
                        var titleOrigin = title.TranslatePoint(default, mini)!.Value;
                        var progressOrigin = miniTimeline.TranslatePoint(default, mini)!.Value;
                        Check(artOrigin.X <= 1 && artOrigin.Y <= 1 && Math.Abs(cover.Bounds.Height - mini.Bounds.Height) <= 2
                            && cover.Bounds.Width == cover.Bounds.Height, "Mini player cover does not fill its square");
                        Check(Math.Abs(titleOrigin.X - progressOrigin.X) <= 1 && progressOrigin.X + miniTimeline.Bounds.Width < mini.Bounds.Width,
                            "Mini player timeline is not aligned to the text and right inset");
                    }
                    if (width >= 1000)
                    {
                        var main = (Border)typeof(MainView).GetField("_main", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        Check(Grid.GetRowSpan(main) == 1 && Equals(((Control)scrollingList.Content!).Resources["MiniPlayerInset"] ?? 0d, 0d),
                            "Desktop dock retains floating mini player clearance");
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
                    var qualityButton = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Content is TextBlock && Avalonia.Automation.AutomationProperties.GetName(b) == "Informações do áudio");
                    qualityButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
                    Check((bool)typeof(MainView).GetField("_showingPlayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!, "Audio info closed expanded player");
                    Check(AppServices.BackHandler!(), "Audio info ignores back");
                    var fakePlayer = (Player)AppServices.Controller.Player;
                    fakePlayer.Position = 56;
                    await Task.Delay(650);
                    var detailTime = (TextBlock)typeof(MainView).GetField("_detailTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                    Check(detailTime.Text == "0:56", "Expanded player clock stopped");
                    fakePlayer.Position = 61;
                    await Task.Delay(650);
                    Check(detailTime.Text == "1:01", "Expanded player clock did not advance without reopening");
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
                        var slot = (Decorator)artwork.Parent!;
                        var split = (Grid)slot.Parent!;
                        Check(Math.Abs(slot.Bounds.Width - Math.Min(500, (split.Bounds.Width - split.ColumnSpacing) / 2)) < 2, "Desktop player cover ignores column or size cap");
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
                        var info = view.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content is not TextBlock && Avalonia.Automation.AutomationProperties.GetName(b) == "Informações do áudio");
                        info.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
                        Check((bool)typeof(MainView).GetField("_showingPlayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!, "Audio info closed fullscreen player");
                        var overlay = (Border)typeof(MainView).GetField("_overlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        Check(Math.Abs(overlay.Bounds.Width - view.Bounds.Width) < 2, "Dialog scrim does not cover full window");
                        var sheet = (Border)overlay.Child!;
                        Check(Math.Abs(sheet.Bounds.Center.X - overlay.Bounds.Width / 2) < 2, "Dialog not centered");
                        Check(view.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Text == "160 kb/s"), "Audio bitrate missing from details");
                        Capture(view, "audio-info"); Check(AppServices.BackHandler!(), "Audio details cannot close");
                    }
                    var playerShowing = (bool)typeof(MainView).GetField("_showingPlayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                    if (playerShowing) Check(AppServices.BackHandler!(), "Player cannot close");
                    await Task.Delay(300);
                    var more = view.GetVisualDescendants().OfType<Button>().First(b => Avalonia.Automation.AutomationProperties.GetName(b)?.StartsWith("Opções de ") == true);
                    more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                    var songPopup = (Avalonia.Controls.Primitives.Popup)typeof(MainView).GetField("_songPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                    Check(songPopup.IsOpen && songPopup.Child!.Bounds.Width <= 302, "Song menu not compact");
                    Capture(view, $"dialog-{width}"); Check(AppServices.BackHandler!(), "Dialog cannot close");
                }
                var music = AppServices.Controller!;
                var transferQueue = typeof(MainView).GetField("_downloadQueue", hidden)!.GetValue(view)!;
                var transferJobs = (System.Collections.IList)transferQueue.GetType().GetProperty("Jobs")!.GetValue(transferQueue)!;
                var jobType = typeof(MainView).Assembly.GetType("Hibike.App.Downloads.DownloadJob")!;
                var stageType = typeof(MainView).Assembly.GetType("Hibike.App.Downloads.TransferStage")!;
                for (var i = 0; i < 25; i++)
                {
                    var job = Activator.CreateInstance(jobType, new Track($"transfer-{i}",
                        "The Reacher - Vers le Sommet · Long title for narrow screen", "Lorien Testard & Alice Duport-Percier", $"https://example.test/{i}"))!;
                    jobType.GetMethod("ChangeStage", hidden)!.Invoke(job, [Enum.Parse(stageType, i == 0 ? "Transferring" : "Queued"), null]);
                    if (i == 0) jobType.GetMethod("UpdateProgress", hidden)!.Invoke(job, [new AudioDownloadProgress(2097152, 10485760, 204800)]);
                    transferJobs.Add(job);
                }
                foreach (var width in new[] { 320, 360, 390, 430 })
                {
                    window.Width = width;
                    window.Height = 720;
                    var downloadsNav = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible
                        && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "downloads");
                    downloadsNav.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(100);
                    var downloadsPage = (Grid)typeof(MainView).GetField("_downloadsPage", hidden)!.GetValue(view)!;
                    var device = (Button)typeof(MainView).GetField("_deviceTab", hidden)!.GetValue(view)!;
                    device.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(50);
                    var search = (TextBox)typeof(MainView).GetField("_downloadFilter", hidden)!.GetValue(view)!;
                    var searchPosition = search.TranslatePoint(default, downloadsPage)!.Value;
                    Check(search.Bounds.Width >= width - 80, "Downloads search squeezed");
                    Check(searchPosition.Y + search.Bounds.Height <= downloadsPage.Bounds.Height, "Downloads search clipped");
                    var playDownloads = (Button)typeof(MainView).GetField("_playDownloads", hidden)!.GetValue(view)!;
                    Check(((Control)playDownloads.Content!).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Stroke is Avalonia.Media.ISolidColorBrush stroke
                        && playDownloads.Foreground is Avalonia.Media.ISolidColorBrush foreground && stroke.Color == foreground.Color, "Downloads play icon lacks contrast on primary background");
                    var rows = (ItemsControl)typeof(MainView).GetField("_downloadItems", hidden)!.GetValue(view)!;
                    var scroll = rows.GetVisualAncestors().OfType<ScrollViewer>().First();
                    Check(scroll.Bounds.Height >= downloadsPage.Bounds.Height * .35, "Downloads tools consume list viewport");
                    Capture(view, $"downloads-device-{width}");
                    var transfers = (Button)typeof(MainView).GetField("_transfersTab", hidden)!.GetValue(view)!;
                    transfers.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(50);
                    Check(!search.IsEffectivelyVisible, "Device tools leak into transfers");
                    var queue = (ItemsControl)typeof(MainView).GetField("_downloadQueueItems", hidden)!.GetValue(view)!;
                    Check(queue.GetVisualAncestors().OfType<ScrollViewer>().First().Bounds.Height > downloadsPage.Bounds.Height * .5,
                        "Transfer list lacks bounded viewport");
                    Check(queue.GetRealizedContainers().Count() < transferJobs.Count, "Transfer list lost virtualization");
                    foreach (var button in queue.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                    {
                        var position = button.TranslatePoint(default, queue)!.Value;
                        Check(button.Bounds.Height >= 48 && position.X >= 0 && position.X + button.Bounds.Width <= queue.Bounds.Width + 1,
                            "Transfer action squeezed or overflows narrow screen");
                    }
                    Capture(view, $"downloads-transfers-{width}");
                }
                transferJobs.Clear();
                await music.ToggleFavoriteAsync(music.Results[0]);
                await music.ToggleFavoriteAsync(new("local-filter", "Local favorite", "Local artist", "test", IsLocal: true));
                foreach (var width in new[] { 360, 1260 })
                {
                    window.Width = width;
                    var libraryNav = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "library");
                    libraryNav.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
                    var filter = view.GetVisualDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Buscar na biblioteca");
                    filter.Text = "Local favorite"; await Task.Delay(100);
                    var filtered = view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.IsEffectivelyVisible);
                    Check(filtered.ItemsSource!.Cast<Track>().Single().Id == "local-filter", "Library filter did not isolate local favorite");
                    Check(filtered.GetVisualAncestors().OfType<ScrollViewer>().First().OpacityMask is null, "Non-scrolling content faded");
                    Capture(view, $"library-{width}"); filter.Text = "";
                    var brand = (Image)typeof(MainView).GetField("_brandIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                    Check(!brand.IsVisible, "Library incorrectly displays app brand");
                    if (width == 360) music.CreatePlaylist("Library visual check");
                    var libraryTabs = (List<Button>)typeof(MainView).GetField("_mobileLibraryTabs", hidden)!.GetValue(view)!;
                    libraryTabs[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(150); Capture(view, $"library-playlists-{width}");
                    var libraryGrid = (ItemsControl)typeof(MainView).GetField("_mobileLibraryGroups", hidden)!.GetValue(view)!;
                    Check(libraryGrid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Library visual check"), "Library playlist cover missing");
                    var layoutToggle = (Button)typeof(MainView).GetField("_mobileLibraryViewMode", hidden)!.GetValue(view)!;
                    layoutToggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(100);
                    Check(Equals(layoutToggle.Content, "Grade") && libraryGrid.GetVisualDescendants().OfType<Button>().Any(b =>
                        Avalonia.Automation.AutomationProperties.GetName(b) == "Library visual check · Playlist"),
                        "Library list mode lost its playlist");
                    Capture(view, $"library-list-{width}");
                    layoutToggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    libraryTabs[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    if (width == 360)
                    {
                        var first = new Track("live-artist-1", "First", "Live artist", "live-artist-1", Album: "Live album");
                        var second = first with { Id = "live-artist-2", Source = "live-artist-2", Title = "Second" };
                        await music.ToggleFavoriteAsync(first);
                        libraryTabs[3].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        await Task.Delay(100);
                        var group = libraryGrid.ItemsSource!.Cast<Array>().SelectMany(row => row.Cast<object>())
                            .Single(item => (string)item.GetType().GetProperty("Title")!.GetValue(item)! == "Live artist");
                        await (Task)typeof(MainView).GetMethod("OpenMobileLibraryGroupAsync", hidden)!.Invoke(view, [group])!;
                        await music.ToggleFavoriteAsync(second);
                        var liveTracks = (ItemsControl)typeof(MainView).GetField("_mobileLibraryTracks", hidden)!.GetValue(view)!;
                        Check(liveTracks.ItemsSource!.Cast<Track>().Count() == 2, "Open artist collection missed added favorite");
                        await music.ToggleFavoriteAsync(first);
                        Check(liveTracks.ItemsSource!.Cast<Track>().Single().Source == second.Source, "Open artist collection retained removed favorite");
                        typeof(MainView).GetMethod("CloseMobileLibraryGroup", hidden)!.Invoke(view, null);
                        libraryTabs[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    }
                    var queueNav = view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "queue");
                    music.Enqueue(new Track("queue-visible", "Queue visible", "Artist", "queue-visible", 267.8));
                    queueNav.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150); Capture(view, $"queue-{width}");
                    if (width == 360) Check(!brand.IsVisible, "Queue incorrectly displays app brand");
                    if (width == 360)
                    {
                        window.Height = 640; await Task.Delay(150);
                        var upcoming = (ItemsControl)typeof(MainView).GetField("_mobileQueueItems", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                        var queueScroll = upcoming.GetVisualAncestors().OfType<ScrollViewer>().First();
                        Check(queueScroll.Bounds.Height >= 120, "Portrait queue has no room for upcoming tracks");
                        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "Tocando agora"), "Mobile queue current track missing");
                        Check(upcoming.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "4:28"), "Mobile queue missing library-style duration");
                        Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == "A seguir"), "Mobile queue heading missing");
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
                var collectionCard = (Control)typeof(MainView).GetMethod("CoverCard", hidden)!.Invoke(view,
                    [nextA, "Artista", (Func<Task>)(() => Task.CompletedTask), false, false, null, false])!;
                Check(!collectionCard.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("cardAction") && b.IsHitTestVisible == false), "Collection card contains play overlay");
                var artistCache = (Dictionary<string, ArtistProfile>)typeof(MainView).GetField("_artistProfiles", hidden)!.GetValue(view)!;
                artistCache[artists[0].Id] = artistProfile with { Id = artists[0].Id, Name = artists[0].Name };
                var previousRoute = route.GetValue(view);
                await (Task)typeof(MainView).GetMethod("ShowArtistAsync", hidden)!.Invoke(view, [artists[0], false])!;
                var detailHost = (Border)typeof(MainView).GetField("_collectionPage", hidden)!.GetValue(view)!;
                var artistScreen = detailHost.Child;
                Check(typeof(MainView).GetField("_page", hidden)!.GetValue(view) as string == "collection", "Artist did not open dedicated page");
                Check(typeof(MainView).GetField("_overlay", hidden)!.GetValue(view) is null, "Artist opened modal");
                typeof(MainView).GetMethod("OpenCollection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, ["Playlist de teste", new Track[] { nextA, nextB }, "Playlist", null, null, null, false, false, null]);
                await Task.Delay(150); Capture(view, "collection-1260");
                Check(view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Playlist de teste" && t.IsEffectivelyVisible), "Collection page missing");
                var queueCount = music.Queue.Count;
                var addCollection = view.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content as string == "Adicionar à fila");
                addCollection.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                addCollection.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Check(music.Queue.Count == queueCount, "Adding collection duplicated existing queued tracks");
                Check(AppServices.BackHandler!() && ReferenceEquals(detailHost.Child, artistScreen),
                    "Nested collection did not return to the artist");
                Check(AppServices.BackHandler!() && Equals(route.GetValue(view), previousRoute),
                    "Artist did not return to the originating screen");
                var paged = typeof(MainView).GetMethod("ShowPagedCollectionAsync", hidden, null,
                    [typeof(string), typeof(string), typeof(Track), typeof(string), typeof(bool)], null)!;
                var playlistArt = new Track("paged-check", "Paged check", "Test artist", "");
                await (Task)paged.Invoke(view, ["Paged check", "https://www.youtube.com/playlist?list=PagedCheck", playlistArt, "Playlist", false])!;
                await Task.Delay(100);
                ItemsControl PlaylistRows() => detailHost.GetVisualDescendants().OfType<ItemsControl>().Single();
                var pageRows = PlaylistRows();
                var loadMore = pageRows.Items.OfType<Button>().Single();
                Check(pageRows.ItemCount == 51 && !pageRows.GetRealizedContainers().Contains(loadMore),
                    "Load more appeared before the end of the playlist");
                pageRows.ScrollIntoView(50);
                await Task.Delay(100);
                Check(pageRows.GetRealizedContainers().Contains(loadMore), "Load more did not appear at the end");
                loadMore.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Check(ReferenceEquals(pageRows, PlaylistRows()) && Equals(loadMore.Content, "Tentar carregar mais") && loadMore.IsEnabled,
                    "Failed page lost playlist rows or the retry button");
                loadMore.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                pageRows = PlaylistRows();
                Check(pageRows.Items.OfType<Track>().Count() == 52 && !pageRows.Items.OfType<Button>().Any(),
                    "Retry skipped the page, duplicated tracks or kept a button after the end");
                Check(pageRows.GetVisualAncestors().OfType<ScrollViewer>().First().Offset.Y > 0,
                    "Loading more reset the playlist to the top");
                Check(AppServices.BackHandler!(), "Paged playlist cannot return to its origin");
                music.SaveSession(); await music.FlushPendingSavesAsync();
                var restored = new MusicController(new Catalog(), new Player(), new LibraryStore(directory), directory);
                await restored.InitializeAsync();
                Check(restored.Current == AppServices.Controller.Current && restored.Queue.SequenceEqual(AppServices.Controller.Queue), "Playback session not restored");
                Check(!restored.Player.IsPlaying, "Restored session autoplayed");
                restored.ClearQueue(); Check(restored.Queue.Count == 1 && restored.Queue[0] == restored.Current, "Clear queue lost current track");
                restored.StopObserving(); await restored.FlushPendingSavesAsync();
                Console.WriteLine("UI checks passed: 360/390/430, typography, touch targets, virtualization, player, session restore and clear queue.");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-result.txt"), "PASS: 360/390/430, typography, touch targets, virtualization, player.");
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-check-result.txt"), error.ToString()); }
            finally { AppServices.Controller?.FlushPendingSaves(); lifetime.Shutdown(result); }
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
        private int _nextPageAttempts;

        public Task<IReadOnlyList<Track>> BrowsePageAsync(string source, int page, CancellationToken token)
        {
            if (page == 1 && _nextPageAttempts++ == 0) throw new HttpRequestException("Simulated page failure");
            var indices = page switch { 0 => Enumerable.Range(0, 50), 1 => Enumerable.Range(49, 3), _ => [] };
            return Task.FromResult<IReadOnlyList<Track>>(indices.Select(index =>
                new Track("paged-" + index, "Track " + index, "Test artist", "https://example.org/paged/" + index)).ToArray());
        }
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
        public double Position { get; set; }
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
