using System.Text.RegularExpressions;

namespace Hibike.Core.Services;

public enum DiagnosticSector
{
    Playback,
    Discord,
    Google,
    Catalog,
    Storage,
    App
}

public sealed record DiagnosticEntry(DiagnosticSector Sector, DateTimeOffset Time, string Stage, string Message)
{
    public override string ToString() => $"{Time:HH:mm:ss} [{Stage}] {Message}";
}

public static partial class PlaybackDiagnostics
{
    private const int EntriesPerSector = 80;
    private static readonly Dictionary<DiagnosticSector, Queue<(long Order, DiagnosticEntry Entry)>> Entries = new();
    private static readonly object Gate = new();
    private static long _order;

    public static void Write(string stage, string message) => Write(stage switch
    {
        "Discord" => DiagnosticSector.Discord,
        "Login web" or "Login web Android" => DiagnosticSector.Google,
        "Busca" or "Busca no catálogo" or "Rádio" or "Descoberta" or "Imagem do artista" or "Perfil do artista"
            or "Discografia" or "Catálogo paginado" or "Playlists do canal" => DiagnosticSector.Catalog,
        "Sessão" or "Salvar sessão" or "Personalização" or "Áudio local" or "Arquivo local"
            or "Metadados locais" or "Metadados Android" => DiagnosticSector.Storage,
        "Reprodução" or "Erro" or "Player" or "VLC" or "Extrator" or "Cache" or "YT-DLP" or "Probe"
            or "Motor de áudio" or "Áudio Android" or "Mídia Windows" or "Atualizar reprodução Android" => DiagnosticSector.Playback,
        _ => DiagnosticSector.App
    }, stage, message);

    public static void Write(DiagnosticSector sector, string stage, string message)
    {
        var entry = new DiagnosticEntry(sector, DateTimeOffset.Now, Redact(stage), Redact(message));
        lock (Gate)
        {
            if (!Entries.TryGetValue(sector, out var queue)) Entries[sector] = queue = new();
            queue.Enqueue((++_order, entry));
            if (queue.Count > EntriesPerSector) queue.Dequeue();
        }
    }

    // Provider exception bodies can contain account details. Keep only the failure type and HTTP status.
    public static void WriteFailure(DiagnosticSector sector, string stage, Exception error)
    {
        var detail = error is HttpRequestException { StatusCode: { } status }
            ? $"HTTP {(int)status}" : error.GetType().Name;
        Write(sector, stage, $"Falha · {detail}.");
    }

    public static IReadOnlyList<DiagnosticEntry> Capture(DiagnosticSector? sector = null)
    {
        lock (Gate)
        {
            if (sector is { } selected)
                return Entries.TryGetValue(selected, out var queue) ? queue.Select(item => item.Entry).ToArray() : [];
            return Entries.Values.SelectMany(queue => queue).OrderBy(item => item.Order).Select(item => item.Entry).ToArray();
        }
    }

    public static string Snapshot() => string.Join(Environment.NewLine, Capture());

    private static string Redact(string value)
    {
        if (value.Count(character => character == '\t') >= 5 || value.Split("\\t").Length >= 6)
            return "Detalhe de cookies omitido.";
        value = UrlPattern().Replace(value, "[URL]");
        value = CredentialPattern().Replace(value, "[credencial omitida]");
        value = SecretPattern().Replace(value, "[credencial omitida]");
        value = EmailPattern().Replace(value, "[e-mail]");
        value = LocalPathPattern().Replace(value, "[arquivo local]");
        value = string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character)).Trim();
        return value.Length <= 480 ? value : value[..480] + "…";
    }

    [GeneratedRegex(@"(?i)(?:https?|wss?|file|content)://[^\s""<>]+")]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?im)\b(?:authorization|cookie|set-cookie|access_token|refresh_token|id_token|token|client_secret|password|passwd|sapisid|apisid|hsid|ssid|sid|__secure-[\w-]+)[""']?\s*[:=][^\r\n]*")]
    private static partial Regex CredentialPattern();

    [GeneratedRegex(@"(?i)\b(?:bearer|sapisidhash)\s+[^\s,;]+|\bya29\.[\w.-]+|\b[\w-]{20,}\.[\w-]{6,}\.[\w-]{20,}\b|\bmfa\.[\w-]{20,}")]
    private static partial Regex SecretPattern();

    [GeneratedRegex("""(?i)["'](?:[a-z]:[\\/]|/)[^"'\r\n]+["']|(?:[a-z]:\\|/(?:data|storage|home|Users|mnt|tmp|sdcard|var)/)[^\s<>"']+""")]
    private static partial Regex LocalPathPattern();

    [GeneratedRegex(@"(?i)\b[\w.%+-]+@[\w.-]+\.[a-z]{2,}\b")]
    private static partial Regex EmailPattern();
}
