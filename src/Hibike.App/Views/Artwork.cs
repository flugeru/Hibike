using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Hibike.Core.Models;
namespace Hibike.App.Views;

internal sealed class Artwork : Border
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    private CancellationTokenSource? _cancel;
    private Bitmap? _bitmap;
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
                    if (Uri.TryCreate(url, UriKind.Absolute, out var local) && local.IsFile)
                    {
                        var localBitmap = await DecodeAsync(() => File.OpenRead(local.LocalPath), pixels, token);
                        if (token.IsCancellationRequested) { localBitmap.Dispose(); return; }
                        Display(localBitmap); return;
                    }
                    var imageUrl = fullResolution || highQuality ? PlayerImageUrl(url) : url;
                    var fetched = await Client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, token);
                    if (!fetched.IsSuccessStatusCode && imageUrl != url)
                    {
                        fetched.Dispose();
                        fetched = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                    }
                    using var response = fetched;
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is > 4_000_000) return;
                    using var input = await response.Content.ReadAsStreamAsync(token);
                    using var memory = new MemoryStream();
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        if (memory.Length + read > 4_000_000) return;
                        memory.Write(buffer, 0, read);
                    }
                    memory.Position = 0;
                    // Player is resized after construction; never upscale its 52dp placeholder decode.
                    var bytes = memory.ToArray();
                    var bitmap = await DecodeAsync(() => new MemoryStream(bytes, writable: false), pixels, token);
                    if (token.IsCancellationRequested) { bitmap.Dispose(); return; }
                    Display(bitmap);
                }
                catch (Exception) { /* Artwork is optional; playback never depends on it. */ }
            };
        }
        DetachedFromVisualTree += (_, _) =>
        {
            _cancel?.Cancel(); picture.Source = null; _bitmap?.Dispose(); _bitmap = null;
        };
    }
    private static async Task<Bitmap> DecodeAsync(Func<Stream> open, int pixels, CancellationToken token)
    {
        await DecodeSlots.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var stream = open();
                return Bitmap.DecodeToWidth(stream, pixels);
            }, token);
        }
        finally { DecodeSlots.Release(); }
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
