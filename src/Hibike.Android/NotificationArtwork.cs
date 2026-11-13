using System.Net;
using System.Net.Sockets;

namespace Hibike.Android;

internal static class NotificationArtwork
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(4) };
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
    {
        var current = uri;
        for (var hop = 0; hop < 5; hop++)
        {
            if (!IsSafeRemote(current)) return null;
            using var response = await Client.GetAsync(current, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or (HttpStatusCode)308)
            { if (response.Headers.Location is not { } location) return null; current = location.IsAbsoluteUri ? location : new Uri(current, location); continue; }
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 4_000_000) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            return bytes.Length <= 4_000_000 ? new MemoryStream(bytes, false) : null;
        }
        return null;
    }
    private static bool IsSafeRemote(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || (!uri.IsDefaultPort && uri.Port != 443)) return false;
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6) return IPAddress.TryParse(uri.Host, out var ip) && !IsPrivate(ip);
        return !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork) return b[0] is 0 or 10 or 127 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] >= 224;
        return ip.AddressFamily != AddressFamily.InterNetworkV6 || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || b[0] is 0xfc or 0xfd or 0xff;
    }
}
