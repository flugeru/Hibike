using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hibike.Core.Models;
using Hibike.Core.Services;
namespace Hibike.App.Views;

internal sealed class Artwork : Border
{
    private CancellationTokenSource? _cancel;
    private Bitmap? _bitmap;
    private ArtworkCache.Lease? _lease;
    public bool DeferOffscreen { get; init; }

    public Artwork(Track? track, double size, bool fullResolution = false, Stretch stretch = Stretch.UniformToFill, bool naturalAspect = false, bool highQuality = false)
    {
        Width = Height = size; CornerRadius = new CornerRadius(size > UiTheme.ThumbnailSize ? UiTheme.CardRadius : UiTheme.ThumbRadius); ClipToBounds = true;
        Background = UiTheme.Brush(UiTheme.SurfaceElevated);
        BorderBrush = UiTheme.Brush(UiTheme.Outline); BorderThickness = UiTheme.One;
        if (naturalAspect) { Background = Brushes.Transparent; BorderThickness = new Thickness(0); CornerRadius = new(8); }
        var panel = new Grid();
        panel.Children.Add(Icons.Make("wave", UiTheme.Primary, UiTheme.IconSize));
        var picture = new Image { Stretch = stretch, Opacity = 0, Transitions = Fade() };
        panel.Children.Add(picture);
        static Avalonia.Animation.Transitions Fade() => new() { new Avalonia.Animation.DoubleTransition
        { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } };
        Child = panel;
        void Display(Bitmap bitmap, bool cached)
        {
            if (naturalAspect)
            {
                Height = size * bitmap.PixelSize.Height / bitmap.PixelSize.Width;
                Background = Brushes.Transparent; BorderThickness = new Thickness(0); CornerRadius = new(8);
                panel.Children[0].IsVisible = false;
            }
            if (cached || UiMotion.Reduced) picture.Transitions = null;
            _bitmap = bitmap; picture.Source = bitmap; picture.Opacity = 1;
        }
        if (track is not null && (!string.IsNullOrWhiteSpace(track.Thumbnail) || track.IsArtist))
        {
            async void Load()
            {
                if (_cancel is not null) return;
                _cancel = new CancellationTokenSource(); var token = _cancel.Token;
                try
                {
                    var url = track.Thumbnail;
                    if (track.IsArtist)
                    {
                        var id = System.Text.RegularExpressions.Regex.IsMatch(track.Id, "^UC[\\w-]{22}$") ? track.Id
                            : new Uri(track.Source).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                        if (id is not null && System.Text.RegularExpressions.Regex.IsMatch(id, "^UC[\\w-]{22}$"))
                        {
                            try
                            {
                                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                                var profile = await MusicDiscovery.ArtistAsync(id, timeout.Token);
                                if (!string.IsNullOrWhiteSpace(profile.Image)) url = profile.Image;
                            }
                            catch (Exception e) when (e is not OutOfMemoryException) { PlaybackDiagnostics.Write("Imagem do artista", e.Message); }
                        }
                    }
                    if (string.IsNullOrWhiteSpace(url)) return;
                    // Square crops need extra source width for landscape video thumbnails.
                    var pixels = fullResolution ? 1200 : (int)Math.Clamp(size * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * (highQuality ? 2 : 1), 128, 1200);
                    var imageUrl = PlayerImageUrl(url);
                    var lease = await ArtworkCache.AcquireAsync(imageUrl, url, pixels, token);
                    if (token.IsCancellationRequested || _cancel?.Token != token)
                    { lease.Dispose(); return; }
                    _lease?.Dispose(); _lease = lease;
                    Display(lease.Bitmap, lease.Cached);
                }
                catch (Exception) { /* Artwork is optional; playback never depends on it. */ }
            }
            void OnViewportChanged(object? sender, Avalonia.Layout.EffectiveViewportChangedEventArgs e)
            {
                var viewport = e.EffectiveViewport;
                var margin = size / 2;
                if (viewport.Width > 0 && viewport.Height > 0
                    && viewport.Right > -margin && viewport.X < Bounds.Width + margin
                    && viewport.Bottom > -margin && viewport.Y < Bounds.Height + margin)
                {
                    EffectiveViewportChanged -= OnViewportChanged;
                    Load();
                }
            }
            AttachedToVisualTree += (_, _) =>
            {
                if (DeferOffscreen) EffectiveViewportChanged += OnViewportChanged;
                else Load();
            };
            DetachedFromVisualTree += (_, _) => EffectiveViewportChanged -= OnViewportChanged;
        }
        DetachedFromVisualTree += (_, _) =>
        {
            var request = _cancel;
            _cancel = null;
            request?.Cancel();
            request?.Dispose();
            picture.Transitions = null;
            picture.Opacity = 0;
            picture.Transitions = Fade();
            picture.Source = null; _bitmap = null; _lease?.Dispose(); _lease = null;
        };
    }
    public static string PlayerImageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return url;
        if (uri.Host == "i.ytimg.com" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/vi(?:_webp)?/[\w-]+/[\w.-]+$"))
            return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath[..(uri.AbsolutePath.LastIndexOf('/') + 1)]
                + (uri.AbsolutePath.StartsWith("/vi_webp/") ? "maxresdefault.webp" : "maxresdefault.jpg");
        if (uri.Host is "lh3.googleusercontent.com" or "lh3.ggpht.com" or "yt3.ggpht.com" or "yt3.googleusercontent.com")
            return System.Text.RegularExpressions.Regex.Replace(url, @"([=\-])([swh])\d+(?=[\-&#?]|$)", "${1}${2}1200");
        return url;
    }
}

// Keep the cover square even when the player's remaining space is rectangular.
internal sealed class SquareArtworkSlot : Decorator
{
    public SquareArtworkSlot() => ClipToBounds = true;
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(availableSize.Width, availableSize.Height);
        if (!double.IsFinite(side)) return base.MeasureOverride(availableSize);
        Child?.Measure(new Size(side, side));
        return new Size(side, side);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var side = Math.Min(finalSize.Width, finalSize.Height);
        Child?.Arrange(new Rect((finalSize.Width - side) / 2, (finalSize.Height - side) / 2, side, side));
        return finalSize;
    }
}
