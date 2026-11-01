namespace Hibike.Android;

internal static class NotificationArtwork
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(4) };
    public static async Task<byte[]?> LoadAsync(string? url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        try
        {
            using var input = uri.IsFile ? File.OpenRead(uri.LocalPath) : await OpenRemoteAsync(uri, token);
            if (input is null) return null;
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + count > 4_000_000) return null;
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is IOException or HttpRequestException or UnauthorizedAccessException)
        { return null; }
    }

    private static async Task<Stream?> OpenRemoteAsync(Uri uri, CancellationToken token)
        => uri.Scheme == "https" ? await Client.GetStreamAsync(uri, token) : null;
}
