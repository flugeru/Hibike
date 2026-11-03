using Avalonia.Media.Imaging;

namespace Hibike.App.Views;

// Exact decode sizes are separate keys: a player never receives an upscaled thumbnail.
internal static class ArtworkCache
{
    private static readonly object Gate = new();
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
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
    internal sealed class Lease(Bitmap bitmap, Action release) : IDisposable
    {
        private Action? _release = release;
        public Bitmap Bitmap { get; } = bitmap;
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
                    catch { lock (Gate) { if (Images.GetValueOrDefault(key) == entry) Images.Remove(key); } throw; }
                });
            }
            entry.Users++; entry.Used = ++_clock;
        }
        try
        {
            var bitmap = await entry.Loading.WaitAsync(token).ConfigureAwait(false);
            return new Lease(bitmap, () => Release(key, entry));
        }
        catch { Release(key, entry); throw; }
    }

    private static void Release((string Source, string Fallback, int Pixels) key, Entry entry)
    {
        List<Bitmap> expired; bool cancel = false;
        lock (Gate)
        {
            entry.Users--; entry.Used = ++_clock;
            if (entry.Users == 0 && entry.Bitmap is null && !entry.Loading.IsCompleted)
            {
                entry.Abandoned = cancel = true;
                if (Images.GetValueOrDefault(key) == entry) Images.Remove(key);
            }
            expired = TrimImages();
        }
        if (cancel) entry.Cancel.Cancel();
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
                            foreach (var old in Bytes.Where(p => p.Value.Size > 0).OrderBy(p => p.Value.Used).ToArray())
                            {
                                if (_encodedBytes <= ByteBudget && Bytes.Count <= 128) break;
                                Bytes.Remove(old.Key); _encodedBytes -= old.Value.Size;
                            }
                        }
                        return data;
                    }
                    catch { lock (Gate) { if (Bytes.GetValueOrDefault(key) == entry) Bytes.Remove(key); } throw; }
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
                if (--entry.Users == 0 && entry.Size == 0 && !entry.Loading.IsCompleted)
                {
                    entry.Abandoned = cancel = true;
                    if (Bytes.GetValueOrDefault(key) == entry) Bytes.Remove(key);
                }
            }
            if (cancel) entry.Cancel.Cancel();
        }
    }

    private static async Task<byte[]> ReadAsync(string source, string fallback, CancellationToken token)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var file) && file.IsFile)
            return await File.ReadAllBytesAsync(file.LocalPath, token).ConfigureAwait(false);
        await Downloads.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var fetched = await Client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!fetched.IsSuccessStatusCode && source != fallback)
            { fetched.Dispose(); fetched = await Client.GetAsync(fallback, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); }
            using var response = fetched;
            response.EnsureSuccessStatusCode();
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
}
