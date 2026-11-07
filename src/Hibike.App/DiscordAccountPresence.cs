using System.Net.WebSockets;
using System.Text.Json;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App;

internal sealed class DiscordAccountPresence : IDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _send = new(1);
    private readonly MusicController _music;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    private bool _heartbeatPending;
    private int? _sequence;
    private object[] _activities = [];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly Dictionary<string, string?> _covers = new();
    private bool _updating;
    private string? _lastKey;
    private double _lastPosition;
    private bool _lastTitleMode;
    private DateTimeOffset _lastUpdate;
    public bool IsClosed => Volatile.Read(ref _disposed) != 0;
    public bool CredentialsRejected { get; private set; }
    public string Status { get; private set; } = "Desconectado";
    internal sealed record DiscordProfile(string Id, string Name, string Username, string? Avatar);
    public DiscordProfile? Profile { get; private set; }
    public Track? PublishedTrack { get; private set; }
    public event Action? Changed;

    public DiscordAccountPresence(MusicController music) { _music = music; }

    public async Task ConnectAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace) || token.Length > 512)
        {
            CredentialsRejected = true;
            Status = "Formato do token inválido.";
            PlaybackDiagnostics.Write(DiagnosticSector.Discord, "Conexão", Status);
            Dispose();
            throw new ArgumentException("Token inválido.");
        }
        try
        {
            _stop.CancelAfter(TimeSpan.FromSeconds(60));
            await _socket.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), _stop.Token);
            using var hello = await ReceiveAsync();
            if (hello.RootElement.GetProperty("op").GetInt32() != 10) throw new InvalidOperationException("Gateway sem HELLO.");
            var interval = hello.RootElement.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
            if (interval < 1000 || interval > 120000) throw new InvalidOperationException("Intervalo Gateway inválido.");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
            await SendAsync(new { op = 2, d = new { token, capabilities = 65, compress = false, large_threshold = 100,
                properties = new { os = "Android", browser = "Discord Client", device = "Hibike" } } });
            token = "";
            Status = "Conectando";
            _ = RunAsync(interval);
            await _ready.Task.WaitAsync(_stop.Token);
            _stop.CancelAfter(Timeout.InfiniteTimeSpan);
            _music.Changed += Snapshot; _music.Tick += Snapshot;
            Snapshot();
        }
        catch (Exception error)
        {
            if (_ready.Task.IsFaulted)
                Status = _ready.Task.Exception!.GetBaseException().Message;
            else if (Status == "Conectando" || Status == "Desconectado")
                Status = error is OperationCanceledException ? "Tempo esgotado aguardando READY do Discord." : "Falha de conexão TLS/WebSocket com Discord.";
            PlaybackDiagnostics.Write(DiagnosticSector.Discord, "Conectar", Status);
            Dispose(); throw new InvalidOperationException(Status);
        }
    }

    private async void Snapshot()
    {
        if (_updating || _stop.IsCancellationRequested) return;
        _updating = true;
        try
        {
            var track = _music.Current;
            if (track is null || !_music.Player.IsPlaying || _music.LoadingAudio)
            {
                if (_activities.Length == 0) return;
                await SendAsync(new { op = 3, d = Presence([]) });
                _activities = []; _lastKey = null;
                PublishedTrack = null;
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                var position = _music.Player.Position;
                var cover = _music.CurrentSource?.Artwork ?? track.Thumbnail;
                var titleAsName = _music.DiscordUseTrackTitleAsName;
                var key = track.Source + "\n" + track.Title + "\n" + track.Artist + "\n" + cover + "\n" + titleAsName;
                if (key == _lastKey && Math.Abs(position - _lastPosition - (now - _lastUpdate).TotalSeconds) < 3) return;
                // Discord limits presence updates; coalesce rapid seeks on subsequent player ticks.
                if ((now - _lastUpdate).TotalSeconds < 5 && titleAsName == _lastTitleMode) return;
                var image = await ResolveArtworkAsync(cover);
                if (_stop.IsCancellationRequested || _music.Current != track || !_music.Player.IsPlaying
                    || _music.DiscordUseTrackTitleAsName != titleAsName) return;
                now = DateTimeOffset.UtcNow; position = _music.Player.Position;
                var duration = _music.Player.Duration > 0 ? _music.Player.Duration : track.DurationSeconds;
                object[] activities = [BuildActivity(track, position, duration, image, now, titleAsName)];
                await SendAsync(new { op = 3, d = Presence(activities) });
                _activities = activities;
                _lastKey = key; _lastTitleMode = titleAsName; _lastPosition = position; _lastUpdate = now;
                PublishedTrack = track;
            }
            if (_stop.IsCancellationRequested) { PublishedTrack = null; return; }
            Status = "Conectado";
            Changed?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Status != "Falha ao enviar atividade")
                PlaybackDiagnostics.WriteFailure(DiagnosticSector.Discord, "Enviar atividade", error);
            Status = "Falha ao enviar atividade";
            Changed?.Invoke();
        }
        finally { _updating = false; }
    }

    internal static object BuildActivity(Track track, double position, double duration, string? image, DateTimeOffset now, bool titleAsName = false)
    {
        static string Limit(string value)
        {
            var text = new System.Text.StringBuilder(); var bytes = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 128) break;
                text.Append(rune); bytes += rune.Utf8SequenceLength;
            }
            return text.Length == 0 ? "—" : text.ToString();
        }
        position = double.IsFinite(position) ? Math.Max(0, position) : 0;
        duration = double.IsFinite(duration) ? Math.Max(0, duration) : 0;
        // ponytail: invalid multi-year media times omitted; support longer recordings if needed.
        position = Math.Min(position, 31_536_000); duration = Math.Min(duration, 31_536_000);
        var title = track.IsLocal ? track.Title : TrackTextFormatter.Title(track.Title, track.Artist);
        var youtube = !track.IsLocal && Uri.TryCreate(track.Source, UriKind.Absolute, out var source)
            && source.Scheme == "https" && source.Host is "www.youtube.com" or "youtube.com" or "youtu.be";
        return new
        {
            application_id = "1566415890596241459", name = Limit(titleAsName ? title : "Hibike"), type = 2,
            details = Limit(title), state = Limit(track.IsLocal ? track.Artist : TrackTextFormatter.Artist(track.Artist)),
            timestamps = new { start = now.AddSeconds(-position).ToUnixTimeMilliseconds(), end = duration > position ? (long?)now.AddSeconds(duration - position).ToUnixTimeMilliseconds() : null },
            assets = image is null ? null : new { large_image = image, large_text = Limit(track.Album ?? title) },
            buttons = youtube ? new[] { "Ouvir no YouTube" } : null,
            metadata = youtube ? new { button_urls = new[] { track.Source } } : null
        };
    }

    private async Task<string?> ResolveArtworkAsync(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || Uri.CheckHostName(uri.Host) != UriHostNameType.Dns || uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (_covers.TryGetValue(url!, out var cached)) return cached;
        try
        {
            using var body = new StringContent(JsonSerializer.Serialize(new { urls = new[] { url } }), System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("https://discord.com/api/v10/applications/1566415890596241459/external-assets", body, _stop.Token);
            if (!response.IsSuccessStatusCode) return _covers[url!] = null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_stop.Token));
            var path = json.RootElement[0].GetProperty("external_asset_path").GetString();
            return _covers[url!] = string.IsNullOrEmpty(path) ? null : "mp:" + path;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException or IndexOutOfRangeException)
        { return null; }
    }

    private static object Presence(object[] activities) => new { since = (long?)null, activities, status = "online", afk = false };

    internal static DiscordProfile? ReadProfile(JsonElement ready)
    {
        if (!ready.TryGetProperty("d", out var data) || !data.TryGetProperty("user", out var user)) return null;
        string? Read(string key) => user.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        var id = Read("id");
        var username = Read("username");
        if (string.IsNullOrEmpty(id) || id.Length > 20 || !id.All(char.IsAsciiDigit) || string.IsNullOrWhiteSpace(username)) return null;
        var name = Read("global_name");
        var hash = Read("avatar");
        var avatar = hash is not null && System.Text.RegularExpressions.Regex.IsMatch(hash, "^(a_)?[a-fA-F0-9]{32}$")
            ? $"https://cdn.discordapp.com/avatars/{id}/{hash}.png?size=128" : null;
        return new(id, string.IsNullOrWhiteSpace(name) ? username : name, username, avatar);
    }

    private async Task RunAsync(int interval)
    {
        Task heartbeat = Task.CompletedTask;
        try
        {
            heartbeat = HeartbeatAsync(interval);
                while (!_stop.IsCancellationRequested)
                {
                    using var message = await ReceiveAsync();
                    var root = message.RootElement;
                    if (root.TryGetProperty("s", out var sequence) && sequence.ValueKind == JsonValueKind.Number) _sequence = sequence.GetInt32();
                    var op = root.GetProperty("op").GetInt32();
                    if (op == 7) throw new InvalidOperationException("Discord pediu reconexão (op 7).");
                    if (op == 9) throw new InvalidOperationException("Discord rejeitou sessão (op 9). Reconecte; se persistir, confira token.");
                    if (op == 11) _heartbeatPending = false;
                    if (op == 1) await SendAsync(new { op = 1, d = _sequence });
                    if (root.TryGetProperty("t", out var type) && type.GetString() == "READY")
                    {
                        Profile = ReadProfile(root);
                        Status = "Conectado";
                        PlaybackDiagnostics.Write(DiagnosticSector.Discord, "Gateway", "Sessão autenticada.");
                        _ready.TrySetResult();
                        Changed?.Invoke();
                    }
                }
        }
        catch (Exception error)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                Status = error is InvalidOperationException ? error.Message : "Conexão Discord interrompida (rede, TLS ou timeout).";
                PlaybackDiagnostics.Write(DiagnosticSector.Discord, "Gateway", Status);
            }
            _ready.TrySetException(new InvalidOperationException(Status));
        }
        finally { Dispose(); await heartbeat; }
    }

    private async Task HeartbeatAsync(int interval)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (_heartbeatPending) throw new InvalidOperationException("Gateway sem confirmação de heartbeat.");
                _heartbeatPending = true;
                await SendAsync(new { op = 1, d = _sequence });
                await Task.Delay(interval, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch
        {
            Status = "Gateway sem confirmação de heartbeat ou envio interrompido.";
            PlaybackDiagnostics.Write(DiagnosticSector.Discord, "Heartbeat", Status);
            _ready.TrySetException(new InvalidOperationException(Status));
            _stop.Cancel(); _socket.Abort();
        }
    }

    private async Task SendAsync(object payload)
    {
        await _send.WaitAsync(_stop.Token);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            try { await _socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, _stop.Token); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { _send.Release(); }
    }

    private async Task<JsonDocument> ReceiveAsync()
    {
        using var data = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if ((int?)result.CloseStatus == 4004) CredentialsRejected = true;
                throw new InvalidOperationException(CloseMessage((int?)result.CloseStatus));
            }
            ValidateFrame(result.MessageType, data.Length + result.Count);
            data.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(data.ToArray());
    }

    internal static void ValidateFrame(WebSocketMessageType type, long length)
    {
        // ponytail: 16 MiB per Gateway event; larger accounts need a streaming JSON reader.
        if (length > 16 * 1024 * 1024)
            throw new InvalidOperationException("Mensagem Gateway excedeu limite de 16 MiB do Hibike.");
        if (type != WebSocketMessageType.Text)
            throw new InvalidOperationException("Gateway enviou mensagem binária; Hibike solicitou JSON sem compressão.");
    }

    internal static string CloseMessage(int? code) => code switch
    {
        4004 => "Discord recusou autenticação (4004). Token inválido ou expirado.",
        4008 => "Discord limitou requisições (4008). Aguarde antes de reconectar.",
        4009 => "Sessão Discord expirou (4009). Reconecte.",
        4002 => "Discord rejeitou formato enviado (4002). Falha de protocolo do Hibike.",
        _ => $"Discord encerrou conexão (código {code?.ToString() ?? "ausente"}). Reconecte."
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _music.Changed -= Snapshot;
        _music.Tick -= Snapshot;
        _http.DefaultRequestHeaders.Clear(); _http.Dispose();
        _stop.Cancel(); _socket.Abort(); _socket.Dispose();
        PublishedTrack = null;
        if (Status is "Conectado" or "Conectando") Status = "Desconectado";
        Changed?.Invoke();
    }
}
