using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Hibike.Core.Models;
namespace Hibike.App.Views;

internal sealed class Artwork : Border
{
    private CancellationTokenSource? _cancel;
    private Bitmap? _bitmap;
    private ArtworkCache.Lease? _lease;
    public Artwork(Track? track, double size, bool fullResolution = false, Stretch stretch = Stretch.UniformToFill, bool naturalAspect = false, bool highQuality = false)
    {
        Width = Height = size; CornerRadius = new CornerRadius(size > UiTheme.ThumbnailSize ? UiTheme.CardRadius : UiTheme.ThumbRadius); ClipToBounds = true;
        Background = UiTheme.Brush(UiTheme.SurfaceElevated);
        BorderBrush = UiTheme.Brush(UiTheme.Outline); BorderThickness = UiTheme.One;
        if (naturalAspect) { Background = Brushes.Transparent; BorderThickness = new Thickness(0); CornerRadius = new(8); }
        var panel = new Grid();
        panel.Children.Add(Icons.Make("wave", UiTheme.Primary, UiTheme.IconSize));
        var picture = new Image { Stretch = stretch, Opacity = 0,
            Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition
            { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } } }; panel.Children.Add(picture);
        Child = panel;
        void Display(Bitmap bitmap)
        {
            if (naturalAspect)
            {
                Height = size * bitmap.PixelSize.Height / bitmap.PixelSize.Width;
                Background = Brushes.Transparent; BorderThickness = new Thickness(0); CornerRadius = new(8);
                panel.Children[0].IsVisible = false;
            }
            _bitmap = bitmap; picture.Source = bitmap; picture.Opacity = 1;
        }
        if (track?.Thumbnail is { } url)
        {
            AttachedToVisualTree += async (_, _) =>
            {
                _cancel?.Dispose(); _cancel = new CancellationTokenSource(); var token = _cancel.Token;
                try
                {
                    // Square crops need extra source width for landscape video thumbnails.
                    var pixels = fullResolution ? 1200 : (int)Math.Clamp(size * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * (highQuality ? 2 : 1), 128, 1200);
                    var imageUrl = fullResolution || highQuality ? PlayerImageUrl(url) : url;
                    var lease = await ArtworkCache.AcquireAsync(imageUrl, url, pixels, token);
                    if (token.IsCancellationRequested || _cancel?.Token != token)
                    { lease.Dispose(); return; }
                    _lease?.Dispose(); _lease = lease;
                    Display(lease.Bitmap);
                }
                catch (Exception) { /* Artwork is optional; playback never depends on it. */ }
            };
        }
        DetachedFromVisualTree += (_, _) =>
        {
            _cancel?.Cancel(); picture.Source = null; _bitmap = null; _lease?.Dispose(); _lease = null;
        };
    }
    public static string PlayerImageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return url;
        if (uri.Host == "i.ytimg.com" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/vi(?:_webp)?/[\w-]+/[\w.-]+$"))
            return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath[..(uri.AbsolutePath.LastIndexOf('/') + 1)]
                + (uri.AbsolutePath.StartsWith("/vi_webp/") ? "maxresdefault.webp" : "maxresdefault.jpg");
        if (uri.Host == "lh3.googleusercontent.com" || uri.Host == "lh3.ggpht.com")
            return System.Text.RegularExpressions.Regex.Replace(url, @"([=\-])([swh])\d+(?=[\-&#?]|$)", "${1}${2}1200");
        return url;
    }
}

// Keep the cover square even when the player's remaining space is rectangular.
internal sealed class SquareArtworkSlot : Decorator
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(availableSize.Width, availableSize.Height);
        return double.IsFinite(side) ? base.MeasureOverride(new Size(side, side)) : base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var side = Math.Min(finalSize.Width, finalSize.Height);
        Child?.Arrange(new Rect((finalSize.Width - side) / 2, (finalSize.Height - side) / 2, side, side));
        return finalSize;
    }
}
