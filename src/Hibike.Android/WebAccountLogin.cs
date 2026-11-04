using Android.App;
using Android.Webkit;
using Hibike.Core.Services;

namespace Hibike.Android;

internal sealed class WebAccountLogin(Activity activity)
{
    internal static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 8_000_000 };
    private Dialog? _dialog;
    private WebView? _view;
    private TaskCompletionSource<YouTubeAccountLibrary>? _pending;
    public async Task<YouTubeAccountLibrary> ConnectAsync()
    {
        if (_pending is not null) throw new InvalidOperationException("Login já está aberto.");
        var pending = _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var cookies = CookieManager.Instance!;
            await ClearCookiesAsync(); cookies.SetAcceptCookie(true);
            _view = new WebView(activity);
            _view.Settings.JavaScriptEnabled = true;
            _view.Settings.DomStorageEnabled = true;
            _view.Settings.AllowFileAccess = false;
            _view.Settings.AllowContentAccess = false;
            _view.Settings.CacheMode = CacheModes.NoCache;
            _view.SetWebChromeClient(new WebChromeClient());
            _view.SetWebViewClient(new LoginClient(this));
            _dialog = new Dialog(activity, global::Android.Resource.Style.ThemeMaterialLightNoActionBar);
            _dialog.SetContentView(_view);
            _dialog.CancelEvent += (_, _) => pending.TrySetCanceled();
            _dialog.Show();
            _view.LoadUrl("https://accounts.google.com/ServiceLogin?ltmpl=music&service=youtube&passive=true&continue=https%3A%2F%2Fwww.youtube.com%2Fsignin%3Faction_handle_signin%3Dtrue%26next%3Dhttps%253A%252F%252Fmusic.youtube.com%252F");
            return await pending.Task.WaitAsync(TimeSpan.FromMinutes(5));
        }
        finally
        {
            _dialog?.Dismiss(); _dialog?.Dispose(); _dialog = null;
            _view?.StopLoading(); _view?.ClearCache(true); _view?.ClearHistory(); _view?.Destroy(); _view?.Dispose(); _view = null;
            await ClearCookiesAsync();
            WebStorage.Instance!.DeleteAllData();
            _pending = null;
        }
    }
    public void Cancel() { _pending?.TrySetCanceled(); _dialog?.Cancel(); }
    private static async Task ClearCookiesAsync()
    {
        var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CookieManager.Instance!.RemoveAllCookies(new CookieCallback(cleared));
        await cleared.Task;
        CookieManager.Instance.Flush();
    }
    private sealed class CookieCallback(TaskCompletionSource completion) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) => completion.TrySetResult();
    }
    private sealed class LoginClient(WebAccountLogin owner) : WebViewClient
    {
        private bool _checking;
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            // Embedded account frames are not top-level navigation; let WebView enforce their origin rules.
            if (request?.IsForMainFrame != true) return false;
            var url = request?.Url?.ToString();
            var blocked = !WebLoginNavigation.Allowed(url);
            Uri.TryCreate(url, UriKind.Absolute, out var uri);
            PlaybackDiagnostics.Write("Login web Android", (blocked ? "Navegação bloqueada; host=" : "Navegando; host=") + (uri?.Host ?? "inválido"));
            if (blocked) owner._pending?.TrySetException(new InvalidOperationException(
                "Login solicitou redirecionamento não permitido. Destino: " + (uri is null ? "inválido" : uri.Scheme + ": " + (string.IsNullOrEmpty(uri.Host) ? "sem domínio" : uri.IdnHost)) +
                ". Informe somente esta mensagem; não envie URL completa, códigos ou cookies."));
            return blocked;
        }
        public override void DoUpdateVisitedHistory(WebView? view, string? url, bool isReload)
        {
            base.DoUpdateVisitedHistory(view, url, isReload);
            PlaybackDiagnostics.Write("Login web Android", "Página atual; host=" + (Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "inválido"));
        }
        public override async void OnPageFinished(WebView? view, string? url)
        {
            if (_checking || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "music.youtube.com") return;
            var cookies = CookieManager.Instance!.GetCookie("https://music.youtube.com");
            if (cookies is null || !cookies.Split(';').Any(c => c.Trim().StartsWith("SAPISID=", StringComparison.Ordinal))) return;
            _checking = true;
            var pending = owner._pending;
            YouTubeAccountLibrary? account = null;
            try
            {
                account = YouTubeAccountLibrary.FromWebSession(Client, cookies);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await account.ValidateSessionAsync(timeout.Token);
                if (pending?.TrySetResult(account) != true) account.Dispose();
            }
            catch { account?.Dispose(); pending?.TrySetException(new InvalidOperationException("Não foi possível validar sessão do YouTube Music. Tente novamente.")); }
            finally { _checking = false; }
        }
    }
}
