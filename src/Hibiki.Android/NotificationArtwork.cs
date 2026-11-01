using Android.Graphics;

namespace Hibiki.Android;

/// <summary>
/// Prepares compact, square artwork for Android's native Media3 media controls.
/// Covers are optional and never prevent audio from starting.
/// </summary>
internal static class NotificationArtwork
{
    private const int MaxDownloadBytes = 1_500_000;
    private const int CoverSize = 384;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Lazy<byte[]> DefaultCover = new(CreateDefaultCover);

    public static byte[] Fallback => DefaultCover.Value;

    public static async Task<byte[]?> LoadAsync(string? address, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        try
        {
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaxDownloadBytes)
                return null;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > MaxDownloadBytes)
                    return null;
                buffer.Write(chunk, 0, count);
            }
            return MakeSquareCover(buffer.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException
                                   or ArgumentException)
        {
            // The track still plays if a thumbnail server is slow or unavailable.
            return null;
        }
    }

    private static byte[]? MakeSquareCover(byte[] data)
    {
        if (data.Length == 0) return null;
        try
        {
            using var decoded = BitmapFactory.DecodeByteArray(data, 0, data.Length);
            if (decoded is null || decoded.Width < 1 || decoded.Height < 1) return null;
            var edge = Math.Min(decoded.Width, decoded.Height);
            using var cropped = Bitmap.CreateBitmap(decoded, (decoded.Width - edge) / 2,
                (decoded.Height - edge) / 2, edge, edge);
            using var scaled = Bitmap.CreateScaledBitmap(cropped, CoverSize, CoverSize, true);
            return Encode(scaled, Bitmap.CompressFormat.Jpeg, 86);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or Java.Lang.IllegalArgumentException)
        {
            return null;
        }
    }

    private static byte[] CreateDefaultCover()
    {
        using var bitmap = Bitmap.CreateBitmap(CoverSize, CoverSize, Bitmap.Config.Argb8888)!;
        using var canvas = new Canvas(bitmap);
        using var paint = new Paint(PaintFlags.AntiAlias);
        canvas.DrawColor(Color.Rgb(24, 19, 36));

        paint.Color = Color.Rgb(62, 42, 89);
        canvas.DrawRoundRect(24, 24, CoverSize - 24, CoverSize - 24, 72, 72, paint);
        paint.Color = Color.Rgb(105, 69, 155);
        canvas.DrawCircle(CoverSize / 2f, CoverSize / 2f, 118, paint);
        paint.Color = Color.Rgb(224, 211, 255);
        var heights = new[] { 66f, 154f, 104f, 184f, 80f };
        for (var index = 0; index < heights.Length; index++)
        {
            var x = 111 + index * 34;
            var half = heights[index] / 2;
            canvas.DrawRoundRect(x, 192 - half, x + 18, 192 + half, 9, 9, paint);
        }
        return Encode(bitmap, Bitmap.CompressFormat.Png, 100) ?? Array.Empty<byte>();
    }

    private static byte[]? Encode(Bitmap bitmap, Bitmap.CompressFormat format, int quality)
    {
        using var output = new MemoryStream();
        return bitmap.Compress(format, quality, output) ? output.ToArray() : null;
    }
}
