using Avalonia.Media.Imaging;
using System.Net;
using System.Net.Sockets;

namespace Hibike.App.Views;

// Exact decode sizes are separate keys: a player never receives an upscaled thumbnail.
internal static class ArtworkCache
{
    private static readonly object Gate = new();
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly SemaphoreSlim Downloads = new(4), Decodes = new(2);
    private static readonly Dictionary<(string Source, string Fallback, int Pixels), Entry> Images = [];
    private static readonly Dictionary<string, BytesEntry> Bytes = [];
    private const long ImageBudget = 48 * 1024 * 1024, ByteBudget = 16 * 1024 * 1024;
    private static long _clock, _imageBytes, _encodedBytes;

    private sealed class Entry
    {
        public Task<Bitmap> Loading = null!;
        public Bitmap? Bitmap;
        public int Users;
        public bool Abandoned;
        public readonly CancellationTokenSource Cancel = new();
        public long Used, Size;
    }
    private sealed class BytesEntry
    {
        public Task<byte[]> Loading = null!;
        public long Used;
        public int Size, Users;
        public bool Abandoned;
        public readonly CancellationTokenSource Cancel = new();
    }
    internal sealed class Lease(Bitmap bitmap, bool cached, Action release) : IDisposable
    {
        private Action? _release = release;
        public Bitmap Bitmap { get; } = bitmap;
        public bool Cached { get; } = cached;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    public static async Task<Lease> AcquireAsync(string source, string fallback, int pixels, CancellationToken token)
    {
        // Local artwork may have been replaced in place; stat it off the UI thread.
        var version = "";
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            version = await Task.Run(() => { var file = new FileInfo(uri.LocalPath); return $"|{file.Length}|{file.LastWriteTimeUtc.Ticks}"; }, token).ConfigureAwait(false);
        var key = (source + version, fallback, pixels);
        Entry entry;
        bool cached;
        lock (Gate)
        {
            if (!Images.TryGetValue(key, out entry!))
            {
                entry = new Entry(); Images.Add(key, entry);
                entry.Loading = Task.Run(async () =>
                {
                    try
                    {
                        var data = await GetBytesAsync(source, fallback, version, entry.Cancel.Token).ConfigureAwait(false);
                        await Decodes.WaitAsync(entry.Cancel.Token).ConfigureAwait(false);
                        Bitmap bitmap;
                        try { using var stream = new MemoryStream(data, writable: false); bitmap = Bitmap.DecodeToWidth(stream, pixels); }
                        finally { Decodes.Release(); }
                        List<Bitmap> expired = []; bool abandoned;
                        lock (Gate)
                        {
                            abandoned = entry.Abandoned;
                            if (!abandoned)
                            {
                                entry.Bitmap = bitmap; entry.Size = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
                                _imageBytes += entry.Size; expired = TrimImages();
                            }
                        }
                        if (abandoned) { bitmap.Dispose(); throw new OperationCanceledException(entry.Cancel.Token); }
                        Dispose(expired); return bitmap;
                    }
                    catch
                    {
                        lock (Gate)
                        {
                            if (Images.GetValueOrDefault(key) == entry)
                            {
                                Images.Remove(key);
                                if (entry.Bitmap is not null) _imageBytes -= entry.Size;
                            }
                        }
                        throw;
                    }
                });
            }
            cached = entry.Bitmap is not null;
            entry.Users++; entry.Used = ++_clock;
        }
        try
        {
            var bitmap = await entry.Loading.WaitAsync(token).ConfigureAwait(false);
            return new Lease(bitmap, cached, () => Release(key, entry));
        }
        catch { Release(key, entry); throw; }
    }

    private static void Release((string Source, string Fallback, int Pixels) key, Entry entry)
    {
        List<Bitmap> expired; bool cancel = false;
        lock (Gate)
        {
            if (entry.Users > 0) entry.Users--;
            entry.Used = ++_clock;
            if (entry.Users == 0 && entry.Bitmap is null && !entry.Loading.IsCompleted)
            {
                entry.Abandoned = cancel = true;
                if (Images.GetValueOrDefault(key) == entry) Images.Remove(key);
            }
            expired = TrimImages();
        }
        if (cancel) entry.Cancel.Cancel();
        if (entry.Loading.IsCompleted && (entry.Abandoned || entry.Bitmap is not null && !Images.ContainsValue(entry))) entry.Cancel.Dispose();
        Dispose(expired);
    }
    private static List<Bitmap> TrimImages()
    {
        var expired = new List<Bitmap>();
        if (_imageBytes <= ImageBudget && Images.Count <= 128) return expired;
        foreach (var item in Images.Where(p => p.Value.Users == 0 && p.Value.Bitmap is not null).OrderBy(p => p.Value.Used).ToArray())
        {
            if (_imageBytes <= ImageBudget && Images.Count <= 128) break;
            Images.Remove(item.Key); _imageBytes -= item.Value.Size; expired.Add(item.Value.Bitmap!);
            item.Value.Cancel.Dispose();
        }
        return expired;
    }
    private static void Dispose(List<Bitmap> bitmaps)
    {
        if (bitmaps.Count > 0) _ = Task.Run(() => { foreach (var bitmap in bitmaps) bitmap.Dispose(); });
    }

    private static async Task<byte[]> GetBytesAsync(string source, string fallback, string version, CancellationToken token)
    {
        var key = source + version + "|" + fallback;
        BytesEntry entry;
        lock (Gate)
        {
            if (!Bytes.TryGetValue(key, out entry!))
            {
                entry = new BytesEntry(); Bytes.Add(key, entry);
                entry.Loading = Task.Run(async () =>
                {
                    try
                    {
                        var data = await ReadAsync(source, fallback, entry.Cancel.Token).ConfigureAwait(false);
                        if (data.Length == 0) throw new InvalidDataException("Capa vazia.");
                        lock (Gate)
                        {
                            if (entry.Abandoned) throw new OperationCanceledException(entry.Cancel.Token);
                            entry.Size = data.Length; _encodedBytes += data.Length;
                            TrimBytes();
                        }
                        return data;
                    }
                    catch
                    {
                        lock (Gate)
                        {
                            if (Bytes.GetValueOrDefault(key) == entry)
                            {
                                Bytes.Remove(key);
                                if (entry.Size > 0) _encodedBytes -= entry.Size;
                            }
                        }
                        throw;
                    }
                });
            }
            entry.Users++; entry.Used = ++_clock;
        }
        try { return await entry.Loading.WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            bool cancel = false;
            lock (Gate)
            {
                if (entry.Users > 0) entry.Users--;
                entry.Used = ++_clock;
                if (entry.Users == 0 && entry.Size == 0 && !entry.Loading.IsCompleted)
                {
                    entry.Abandoned = cancel = true;
                    if (Bytes.GetValueOrDefault(key) == entry) Bytes.Remove(key);
                }
                TrimBytes();
            }
            if (cancel) entry.Cancel.Cancel();
            if (entry.Loading.IsCompleted && (entry.Abandoned || entry.Size > 0 && !Bytes.ContainsValue(entry))) entry.Cancel.Dispose();
        }
    }

    private static void TrimBytes()
    {
        foreach (var old in Bytes.Where(p => p.Value.Users == 0 && p.Value.Size > 0).OrderBy(p => p.Value.Used).ToArray())
        {
            if (_encodedBytes <= ByteBudget && Bytes.Count <= 128) break;
            Bytes.Remove(old.Key); _encodedBytes -= old.Value.Size; old.Value.Cancel.Dispose();
        }
    }

    internal static async Task<string?> SaveOfflineCoverAsync(string? source, string audioPath, CancellationToken token)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https") return source;
        var coverPath = Path.ChangeExtension(audioPath, ".cover");
        var temporary = coverPath + ".part";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var bytes = await GetBytesAsync(Artwork.PlayerImageUrl(source!), source!, "", timeout.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(temporary, bytes, timeout.Token).ConfigureAwait(false);
            File.Move(temporary, coverPath);
            return new Uri(coverPath).AbsoluteUri;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return source;
        }
    }

    private static async Task<byte[]> ReadAsync(string source, string fallback, CancellationToken token)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var file) && file.IsFile)
            return await File.ReadAllBytesAsync(file.LocalPath, token).ConfigureAwait(false);
        if (AppServices.Controller?.CanUseOnline == false) throw new IOException("Capa remota indisponível offline.");
        await Downloads.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var fetched = await FetchAsync(source, timeout.Token).ConfigureAwait(false);
            if (fetched is null && source != fallback) fetched = await FetchAsync(fallback, timeout.Token).ConfigureAwait(false);
            using var response = fetched ?? throw new HttpRequestException("Capa remota inválida.");
            if (response.Content.Headers.ContentLength is > 4_000_000) throw new IOException("Capa excede o limite de carregamento.");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var memory = new MemoryStream(); var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > 4_000_000) throw new IOException("Capa excede o limite de carregamento.");
                memory.Write(buffer, 0, read);
            }
            return memory.ToArray();
        }
        finally { Downloads.Release(); }
    }

    private static async Task<HttpResponseMessage?> FetchAsync(string url, CancellationToken token)
    {
        if (!IsSafeRemote(url, out var current)) return null;
        for (var hop = 0; hop < 5; hop++)
        {
            var response = await Client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or (HttpStatusCode)308)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location is null) return null;
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!IsSafeRemote(current.AbsoluteUri, out current)) return null;
                continue;
            }
            return response.IsSuccessStatusCode ? response : Dispose(response);
        }
        return null;
    }
    private static HttpResponseMessage? Dispose(HttpResponseMessage response) { response.Dispose(); return null; }
    private static bool IsSafeRemote(string? url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps || (!parsed.IsDefaultPort && parsed.Port != 443)) return false;
        if (parsed.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 && (!IPAddress.TryParse(parsed.Host, out var ip) || IsPrivate(ip))) return false;
        if (parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || parsed.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || parsed.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        uri = parsed; return true;
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
