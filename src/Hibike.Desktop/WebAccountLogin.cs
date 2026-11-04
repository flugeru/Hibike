using Hibike.Core.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hibike.Desktop;

internal static class WebAccountLogin
{
    internal static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 8_000_000 };
    private static int _open;
    public static Task<YouTubeAccountLibrary> ConnectAsync()
    {
        if (Interlocked.Exchange(ref _open, 1) != 0) throw new InvalidOperationException("Login já está aberto.");
        var result = new TaskCompletionSource<YouTubeAccountLibrary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var web = new WebView2();
                var window = new System.Windows.Window { Title = "Hibike · Entrar no YouTube Music", Width = 520, Height = 740, Content = web, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen };
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
                timer.Tick += (_, _) => { result.TrySetException(new InvalidOperationException("Login excedeu 5 minutos. Consulte Diagnóstico de reprodução para ver etapa, sem enviar códigos ou cookies.")); window.Close(); };
                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hibike-WebLogin"));
                        var options = environment.CreateCoreWebView2ControllerOptions();
                        options.IsInPrivateModeEnabled = true;
                        await web.EnsureCoreWebView2Async(environment, options);
                        web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                        web.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                        web.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                        web.CoreWebView2.NewWindowRequested += (_, e) =>
                        {
                            e.Handled = true;
                            if (Allowed(e.Uri)) web.CoreWebView2.Navigate(e.Uri);
                            else PlaybackDiagnostics.Write("Login web", "Janela externa bloqueada; host=" + Host(e.Uri));
                        };
                        web.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
                        web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                        web.CoreWebView2.NavigationStarting += (_, e) =>
                        {
                            e.Cancel = !Allowed(e.Uri);
                            PlaybackDiagnostics.Write("Login web", (e.Cancel ? "Navegação bloqueada; host=" : "Navegando; host=") + Host(e.Uri));
                            if (e.Cancel)
                            {
                                result.TrySetException(new InvalidOperationException("Login solicitou redirecionamento não permitido. Consulte Diagnóstico de reprodução; não envie códigos ou cookies."));
                                window.Dispatcher.BeginInvoke(new Action(window.Close));
                            }
                        };
                        var checking = false;
                        web.CoreWebView2.NavigationCompleted += async (_, navigation) =>
                        {
                            PlaybackDiagnostics.Write("Login web", $"Navegação concluída; host={Host(web.Source?.AbsoluteUri)}; sucesso={navigation.IsSuccess}; estado={navigation.WebErrorStatus}; HTTP={navigation.HttpStatusCode}");
                            if (checking || result.Task.IsCompleted || !Uri.TryCreate(web.Source?.AbsoluteUri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "music.youtube.com") return;
                            checking = true;
                            YouTubeAccountLibrary? account = null;
                            try
                            {
                                var cookies = await web.CoreWebView2.CookieManager.GetCookiesAsync("https://music.youtube.com");
                                if (!cookies.Any(c => c.Name == "SAPISID")) { PlaybackDiagnostics.Write("Login web", "Página musical sem sessão autenticada."); return; }
                                PlaybackDiagnostics.Write("Login web", "Validando sessão musical.");
                                account = YouTubeAccountLibrary.FromWebSession(Client, string.Join("; ", cookies.Select(c => c.Name + "=" + c.Value)));
                                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                                await account.ValidateSessionAsync(timeout.Token);
                                if (!result.TrySetResult(account)) account.Dispose();
                                window.Close();
                            }
                            catch { account?.Dispose(); result.TrySetException(new InvalidOperationException("Não foi possível validar sessão do YouTube Music. Tente novamente.")); window.Close(); }
                            finally { checking = false; }
                        };
                        web.CoreWebView2.Navigate("https://accounts.google.com/ServiceLogin?ltmpl=music&service=youtube&passive=true&continue=https%3A%2F%2Fwww.youtube.com%2Fsignin%3Faction_handle_signin%3Dtrue%26next%3Dhttps%253A%252F%252Fmusic.youtube.com%252F");
                    }
                    catch { result.TrySetException(new InvalidOperationException("Login web indisponível. Confira instalação do Microsoft Edge WebView2 Runtime e tente novamente.")); window.Close(); }
                };
                timer.Start(); window.ShowDialog(); timer.Stop();
            }
            catch { result.TrySetException(new InvalidOperationException("Não foi possível abrir janela de login.")); }
            finally { Interlocked.Exchange(ref _open, 0); result.TrySetCanceled(); }
        }) { IsBackground = true, Name = "Hibike web login" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return result.Task;
    }
    private static bool Allowed(string? url) => WebLoginNavigation.Allowed(url);
    private static string Host(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "inválido";
}
