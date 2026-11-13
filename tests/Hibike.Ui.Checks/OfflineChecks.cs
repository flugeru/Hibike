using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hibike.App;
using Hibike.App.Views;
using Hibike.Core.Models;
using Hibike.Core.Services;

internal static partial class Program
{
    private static async Task CheckOfflineWorkspaceAsync(MainView view, string directory)
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var previousNetwork = AppServices.IsNetworkAvailable;
        var online = true;
        AppServices.IsNetworkAvailable = () => online;
        var controller = AppServices.Controller!;
        var observe = typeof(MusicController).GetMethod("ObserveConnectivity", privateInstance)!;
        var nextCheck = typeof(MusicController).GetField("_nextNetworkCheck", privateInstance)!;

        void DetectConnectivity()
        {
            nextCheck.SetValue(controller, DateTime.MinValue);
            observe.Invoke(controller, null);
        }

        try
        {
            online = false;
            DetectConnectivity();
            await Task.Delay(100);
            view.UpdateLayout();
            Check(!controller.NetworkAvailable && !controller.CanUseOnline, "Connectivity loss did not disable online actions");
            Check(view.GetVisualDescendants().OfType<TextBlock>().Any(label => label.IsEffectivelyVisible
                && label.Text == "Sem conexão"), "Offline screen did not explain connection loss");
            var accept = view.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible
                && Equals(button.Content, "Ativar modo offline"));
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(controller.OfflineMode, "Offline screen did not enter offline mode");
            var downloads = view.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible
                && button.Tag is ValueTuple<string, string, string, bool> tag && tag.Item1 == "downloads");
            downloads.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(50);
            Check(view.GetVisualDescendants().OfType<TextBlock>().Any(label => label.IsEffectivelyVisible && label.Text == "Downloads"),
                "Downloads tab missing");

            online = true;
            DetectConnectivity();
            await Task.Delay(50);
            Check(controller.NetworkAvailable && controller.OfflineMode && !controller.CanUseOnline, "Reconnection left offline mode without consent");
            var leave = view.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible
                && Equals(button.Content, "Voltar ao online"));
            leave.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(controller.CanUseOnline, "Offline screen did not return to online mode");
            AppServices.BackHandler!();

            var legacyDirectory = Path.Combine(directory, "legacy-offline");
            var audio = Path.Combine(legacyDirectory, "audio");
            var downloadsDirectory = Path.Combine(legacyDirectory, "downloads");
            Directory.CreateDirectory(audio);
            Directory.CreateDirectory(downloadsDirectory);
            var imported = new Track("local-old", "Importada", "Artista", Path.Combine(audio, "test.mp3"), IsLocal: true);
            var downloaded = new Track("download-old", "Baixada", "Artista", Path.Combine(downloadsDirectory, "test.m4a"), IsLocal: true);
            var remote = new Track("online-only", "Online", "Artista", "https://example.org/song");
            File.WriteAllText(imported.Source, "offline fixture");
            File.WriteAllText(downloaded.Source, "offline fixture");
            var library = new LibraryStore(legacyDirectory);
            await library.SaveAsync([imported, downloaded, remote]);
            var legacy = new MusicController(new Catalog(), new Player(), library, legacyDirectory);
            await legacy.InitializeAsync();
            Check(legacy.OfflineTracks.Count == 2, "Previously imported and downloaded tracks were not migrated");
            await legacy.ToggleFavoriteAsync(legacy.OfflineTracks.Single(track => track.Id == imported.Id));
            legacy.SetOfflineMode(true);
            legacy.ReplaceQueue([imported, remote, downloaded]);
            await legacy.PlayAsync(imported, 0);
            await legacy.NextAsync();
            Check(legacy.Current?.Source == downloaded.Source && legacy.Player.IsPlaying, "Offline queue did not skip a network-only track");
            legacy.StopObserving();
            await legacy.FlushPendingSavesAsync();

            var reopened = new MusicController(new Catalog(), new Player(), library, legacyDirectory);
            await reopened.InitializeAsync();
            Check(reopened.OfflineMode && reopened.OfflineTracks.Any(track => track.Id == imported.Id),
                "Offline selection or imported file depended on favorites after restart");
            var remove = typeof(MusicController).GetMethod("RemoveOfflineTrackAsync", privateInstance)!;
            var removal = reopened.OfflineTracks.Single(track => track.Id == imported.Id);
            await (Task)remove.Invoke(reopened, [removal])!;
            Check(!File.Exists(imported.Source) && File.Exists(downloaded.Source) && reopened.OfflineTracks.Count == 1,
                "Deleting an imported copy affected other files or left a stale entry");
            await (Task)remove.Invoke(reopened, [reopened.OfflineTracks.Single()])!;
            Check(!File.Exists(downloaded.Source) && reopened.Current is null && reopened.OfflineTracks.Count == 0,
                "Deleting the selected offline file left a selected track or a stale download");
            reopened.StopObserving();
            await reopened.FlushPendingSavesAsync();
        }
        finally
        {
            AppServices.IsNetworkAvailable = previousNetwork;
            DetectConnectivity();
        }
    }
}
