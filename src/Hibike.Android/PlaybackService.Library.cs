using System.Security.Cryptography;
using System.Text;
using Android.OS;
using Android.Runtime;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Avalonia.Threading;
using Google.Common.Util.Concurrent;
using Hibike.App;
using Hibike.Core.Models;
using MediaLibrarySession = AndroidX.Media3.Session.MediaLibraryService.MediaLibrarySession;
using LibraryParams = AndroidX.Media3.Session.MediaLibraryService.LibraryParams;
using ControllerInfo = AndroidX.Media3.Session.MediaSession.ControllerInfo;

namespace Hibike.Android;

public sealed partial class PlaybackService
{
    private const string ToggleFavoriteAction = "com.flugel.hibike.FAVORITE";
    private const string LibraryRoot = "hibike:library";
    private const string FavoritesId = "hibike:favorites";
    private const string DownloadsId = "hibike:downloads";
    private bool? _favoriteActionState;

    private void RefreshFavoriteAction()
    {
        if (_session is null) return;
        var controller = AppServices.Controller;
        bool? favorite = controller?.Current is { } track ? controller.IsFavorite(track) : null;
        if (favorite == _favoriteActionState) return;
        _favoriteActionState = favorite;
        if (favorite is not { } saved)
        {
            _session.SetMediaButtonPreferences(Array.Empty<CommandButton>());
            return;
        }
        var button = new CommandButton.Builder(saved ? CommandButton.IconHeartFilled : CommandButton.IconHeartUnfilled)
            .SetDisplayName(saved ? "Remover dos favoritos" : "Salvar nos favoritos")!
            .SetSessionCommand(new SessionCommand(ToggleFavoriteAction, Bundle.Empty))!
            .SetSlots(CommandButton.SlotForwardSecondary, CommandButton.SlotOverflow)!.Build();
        if (button is not null) _session.SetMediaButtonPreferences(new[] { button });
    }

    private static string LibraryTrackId(Track track) =>
        "hibike:track:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(track.Source)));

    private static Track? FindLibraryTrack(MusicController controller, string id) =>
        controller.Favorites.Concat(controller.OfflineTracks).FirstOrDefault(track => LibraryTrackId(track) == id);

    private static MediaItem LibraryItem(string id, string title, bool browsable, string? artist = null)
    {
        var metadata = new MediaMetadata.Builder().SetTitle(title)!.SetIsBrowsable(Java.Lang.Boolean.ValueOf(browsable))!
            .SetIsPlayable(Java.Lang.Boolean.ValueOf(!browsable))!;
        if (!string.IsNullOrWhiteSpace(artist)) metadata.SetArtist(artist);
        return new MediaItem.Builder().SetMediaId(id)!.SetMediaMetadata(metadata.Build())!.Build()!;
    }

    private sealed class LibraryCallback : Java.Lang.Object, MediaLibrarySession.ICallback
    {
        public MediaSession.ConnectionResult? OnConnect(MediaSession? session, ControllerInfo? controller)
        {
            if (session is null) return MediaSession.ConnectionResult.Reject();
            var result = new MediaSession.ConnectionResult.AcceptedResultBuilder(session);
            if (controller?.IsTrusted == true || session.IsMediaNotificationController(controller))
            {
                var commands = MediaSession.ConnectionResult.DefaultSessionAndLibraryCommands?.BuildUpon();
                commands?.Add(new SessionCommand(ToggleFavoriteAction, Bundle.Empty));
                result.SetAvailableSessionCommands(commands?.Build());
            }
            return result.Build();
        }

        public IListenableFuture? OnCustomCommand(MediaSession? session, ControllerInfo? controller, SessionCommand? command, Bundle? args)
        {
            if (session is null || command?.CustomAction != ToggleFavoriteAction ||
                (controller?.IsTrusted != true && !session.IsMediaNotificationController(controller)))
                return Immediate(new SessionResult(-6));
            Dispatcher.UIThread.Post(async () =>
            {
                if (AppServices.Controller is { Current: { } track } music) await music.ToggleFavoriteAsync(track);
            });
            return Immediate(new SessionResult(0));
        }

        public IListenableFuture? OnGetLibraryRoot(MediaLibrarySession? session, ControllerInfo? browser, LibraryParams? parameters) =>
            Immediate(browser?.IsTrusted == true
                ? LibraryResult.OfItem(LibraryItem(LibraryRoot, "Hibike", true), parameters)
                : LibraryResult.OfError(-4));

        public IListenableFuture? OnGetChildren(MediaLibrarySession? session, ControllerInfo? browser, string? parentId, int page, int pageSize, LibraryParams? parameters)
        {
            if (browser?.IsTrusted != true) return Immediate(LibraryResult.OfError(-4));
            if (page < 0 || pageSize <= 0) return Immediate(LibraryResult.OfError(-3));
            if (parentId == LibraryRoot)
                return Immediate(LibraryResult.OfItemList(new[] { LibraryItem(FavoritesId, "Favoritos", true), LibraryItem(DownloadsId, "Downloads", true) }, parameters));
            if (parentId is not (FavoritesId or DownloadsId)) return Immediate(LibraryResult.OfError(-3));
            return AfterLibraryLoaded(music =>
            {
                var tracks = parentId == DownloadsId ? music.OfflineTracks : music.Favorites.ToArray();
                var items = tracks.Skip((int)Math.Min(int.MaxValue, (long)page * pageSize)).Take(Math.Min(pageSize, 200))
                    .Select(track => LibraryItem(LibraryTrackId(track), track.Title, false, track.Artist)).ToArray();
                return LibraryResult.OfItemList(items, parameters);
            });
        }

        public IListenableFuture? OnGetItem(MediaLibrarySession? session, ControllerInfo? browser, string? mediaId)
        {
            if (browser?.IsTrusted != true || mediaId is null) return Immediate(LibraryResult.OfError(-4));
            return AfterLibraryLoaded(music => FindLibraryTrack(music, mediaId) is { } track
                ? LibraryResult.OfItem(LibraryItem(mediaId, track.Title, false, track.Artist), null)
                : LibraryResult.OfError(-3));
        }

        public IListenableFuture? OnSetMediaItems(MediaSession? session, ControllerInfo? controller, IList<MediaItem>? items, int startIndex, long startPositionMs)
        {
            if (controller?.IsTrusted != true || items is not { Count: > 0 })
                return Immediate(new MediaSession.MediaItemsWithStartPosition(Array.Empty<MediaItem>(), 0, 0));
            return AfterLibraryLoaded(music =>
            {
                var item = items[Math.Clamp(startIndex, 0, items.Count - 1)];
                if (item.MediaId is not { } id || FindLibraryTrack(music, id) is not { } track)
                    return new MediaSession.MediaItemsWithStartPosition(Array.Empty<MediaItem>(), 0, 0);
                return new MediaSession.MediaItemsWithStartPosition(new[] { LibraryItem(id, track.Title, false, track.Artist) }, 0, 0);
            });
        }

        // The binding exposes ListenableFuture but omits Guava's factory methods.
        private static IListenableFuture? Immediate(Java.Lang.Object? result)
        {
            var type = JNIEnv.FindClass("com/google/common/util/concurrent/Futures");
            try
            {
                var method = JNIEnv.GetStaticMethodID(type, "immediateFuture", "(Ljava/lang/Object;)Lcom/google/common/util/concurrent/ListenableFuture;");
                var handle = JNIEnv.CallStaticObjectMethod(type, method, new[] { new JValue(result?.Handle ?? IntPtr.Zero) });
                return Java.Lang.Object.GetObject<IListenableFuture>(handle, JniHandleOwnership.TransferLocalRef);
            }
            finally { JNIEnv.DeleteGlobalRef(type); }
        }

        private static IListenableFuture? AfterLibraryLoaded(Func<MusicController, Java.Lang.Object?> build)
        {
            var type = JNIEnv.FindClass("com/google/common/util/concurrent/SettableFuture");
            IListenableFuture? future;
            IntPtr setter;
            try
            {
                var factory = JNIEnv.GetStaticMethodID(type, "create", "()Lcom/google/common/util/concurrent/SettableFuture;");
                setter = JNIEnv.GetMethodID(type, "set", "(Ljava/lang/Object;)Z");
                future = Java.Lang.Object.GetObject<IListenableFuture>(JNIEnv.CallStaticObjectMethod(type, factory), JniHandleOwnership.TransferLocalRef);
            }
            finally { JNIEnv.DeleteGlobalRef(type); }
            if (future is null) return null;
            Dispatcher.UIThread.Post(async () =>
            {
                Java.Lang.Object? result;
                try
                {
                    var controller = AppServices.CreateController();
                    await controller.InitializeAsync();
                    result = build(controller);
                }
                catch (Exception)
                {
                    result = LibraryResult.OfError(-1);
                }
                JNIEnv.CallBooleanMethod(future.Handle, setter, new[] { new JValue(result?.Handle ?? IntPtr.Zero) });
            });
            return future;
        }
    }
}
