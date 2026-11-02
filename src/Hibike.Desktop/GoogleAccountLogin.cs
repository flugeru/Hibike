using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hibike.Core.Services;

namespace Hibike.Desktop;

internal static class GoogleAccountLogin
{
    private const string ClientId = "608800044236-2tpfrd9da8812kldg6j5jp6bk6qmemon.apps.googleusercontent.com";
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<YouTubeAccountLibrary> ConnectAsync(string directory)
    {
        var path = Path.Combine(directory, "google-oauth-desktop.json");
        var clientId = ClientId;
        string? secret = null;
        using var bundled = typeof(GoogleAccountLogin).Assembly.GetManifestResourceStream("Hibike.GoogleOAuth.Desktop.json");
        if (bundled is not null)
        {
            using var config = await JsonDocument.ParseAsync(bundled);
            if (!config.RootElement.TryGetProperty("installed", out var installed)
                || installed.GetProperty("client_id").GetString() != ClientId)
                throw new InvalidOperationException("Configuração Google do build não corresponde ao cliente Desktop.");
            secret = installed.GetProperty("client_secret").GetString();
            if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("Configuração Google do build incompleta.");
        }
        // Keep existing installations working without requiring an imported file.
        if (secret is null && File.Exists(path))
        {
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            if (config.RootElement.TryGetProperty("installed", out var installed)
                && installed.TryGetProperty("client_id", out var id) && id.GetString() == ClientId)
                secret = installed.TryGetProperty("client_secret", out var value) ? value.GetString() : null;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        static string RandomValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var state = RandomValue(); var verifier = RandomValue();
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener(); listener.Prefixes.Add(redirect); listener.Start();
        var url = "https://accounts.google.com/o/oauth2/v2/auth?" + Encode(new()
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["scope"] = "https://www.googleapis.com/auth/youtube.readonly", ["state"] = state,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["access_type"] = "online", ["prompt"] = "select_account"
        });
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        string? code = null;
        while (code is null)
        {
            var context = await listener.GetContextAsync().WaitAsync(token);
            var valid = context.Request.Url?.AbsolutePath == "/" && context.Request.QueryString["state"] == state;
            var bytes = Encoding.UTF8.GetBytes(valid ? "Volte ao Hibike. Esta janela pode ser fechada." : "Solicitacao invalida.");
            context.Response.StatusCode = valid ? 200 : 400;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.OutputStream.WriteAsync(bytes, token); context.Response.Close();
            if (!valid) continue;
            code = context.Request.QueryString["code"];
            if (string.IsNullOrEmpty(code)) throw new InvalidOperationException("Autorização Google recusada.");
        }
        var fields = new Dictionary<string, string> { ["client_id"] = clientId, ["code"] = code,
            ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code" };
        if (!string.IsNullOrEmpty(secret)) fields["client_secret"] = secret;
        using var response = await Client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(fields), token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            var requiresSecret = false;
            try
            {
                using var failure = JsonDocument.Parse(body);
                requiresSecret = failure.RootElement.TryGetProperty("error_description", out var description)
                    && description.GetString()?.Contains("client_secret", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch (JsonException) { }
            throw new InvalidOperationException(requiresSecret
                ? "Google exige client_secret deste cliente Desktop. Configuração do build precisa incluir esse valor; nenhuma senha da conta é necessária."
                : $"Google recusou troca OAuth (HTTP {(int)response.StatusCode}). Confira cliente Desktop, YouTube Data API e permissão da conta de teste.");
        }
        using var result = JsonDocument.Parse(body);
        if (!result.RootElement.TryGetProperty("scope", out var scopes)
            || !scopes.GetString()!.Split(' ').Contains("https://www.googleapis.com/auth/youtube.readonly"))
            throw new InvalidOperationException("Permissão de leitura do YouTube não concedida.");
        // ponytail: access token only in memory; add OS credential storage and refresh for persistent login.
        return new YouTubeAccountLibrary(Client, result.RootElement.GetProperty("access_token").GetString()!);
    }

    private static string Encode(Dictionary<string, string> values) => string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
}
