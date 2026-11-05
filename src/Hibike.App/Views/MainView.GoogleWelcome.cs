using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private Border? _googleWelcome;
    private bool _googleWelcomeBusy;
    private string GoogleWelcomePath => System.IO.Path.Combine(_music.DataDirectory, "google-welcome-seen");

    private void ShowGoogleWelcome(bool firstRun = false)
    {
        if (_googleWelcome is not null) return;
        if (firstRun && System.IO.File.Exists(GoogleWelcomePath)) { ShowMusicOnboarding(); return; }
        ClosePlayer(); CloseSheet(); CloseTopMenu();
        var items = new StackPanel { Spacing = 16, MaxWidth = 420, Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        items.Children.Add(new Image
        {
            Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Hibike.App/Assets/hibike-desktop.png"))),
            Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center
        });
        var title = Text("Sua música no Hibike", UiTheme.DialogTitle); title.TextAlignment = TextAlignment.Center; title.TextWrapping = TextWrapping.Wrap; items.Children.Add(title);
        var note = Text("Conecte suas playlists e curtidas do YouTube Music. A sessão é protegida e fica neste aparelho.", UiTheme.Body, Muted);
        note.TextWrapping = TextWrapping.Wrap; note.TextAlignment = TextAlignment.Center; items.Children.Add(note);
        var error = Text("", UiTheme.Caption, UiTheme.Error); error.TextWrapping = TextWrapping.Wrap; error.IsVisible = false;
        Button? signIn = null, skip = null;
        signIn = ActionButton("Entrar com Google", async () =>
        {
            if (_googleWelcomeBusy) return;
            _googleWelcomeBusy = true; signIn!.IsEnabled = skip!.IsEnabled = false; error.IsVisible = false;
            signIn.Content = "Aguardando Google…";
            try
            {
                if (AppServices.ConnectYouTubeAccount is null) throw new InvalidOperationException("Login Google indisponível neste build.");
                var connected = await AppServices.ConnectYouTubeAccount();
                try
                {
                    var save = AppServices.SaveYouTubeAccount ?? throw new InvalidOperationException("Armazenamento da sessão indisponível.");
                    save(connected);
                }
                catch { connected.Dispose(); throw new InvalidOperationException("Não foi possível salvar sessão protegida. Tente novamente."); }
                _account?.Dispose(); _account = connected; _accountExpired = false; _accountRestoreFailed = false; _accountError = null;
                if (!DismissGoogleWelcome()) return;
                if (_page == "account") await ShowAccountAsync();
                else { _page = "home"; Refresh(); }
                await LoadAccountHomeAsync();
                if (_page == "account") await ShowAccountAsync();
            }
            catch (OperationCanceledException)
            {
                error.Text = "Login cancelado ou tempo de autorização esgotado. Tente novamente.";
                error.IsVisible = true;
            }
            catch (InvalidOperationException failure)
            {
                error.Text = failure.Message;
                error.IsVisible = true;
            }
            catch (Exception)
            {
                error.Text = "Não foi possível conectar. Confira conexão e configuração Google do aplicativo e tente novamente. Você pode continuar sem conta.";
                error.IsVisible = true;
            }
            finally { _googleWelcomeBusy = false; signIn.Content = "Entrar com Google"; signIn.IsEnabled = skip.IsEnabled = true; }
        });
        signIn.Classes.Add("primary"); items.Children.Add(signIn);
        skip = ActionButton("Continuar sem conta", () => { DismissGoogleWelcome(); return Task.CompletedTask; }); items.Children.Add(skip); items.Children.Add(error);
        var legal = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
        legal.Children.Add(ActionButton("Privacidade", () => OpenLegalAsync("privacy.html")));
        legal.Children.Add(ActionButton("Termos", () => OpenLegalAsync("terms.html"))); items.Children.Add(legal);
        var scroll = new ScrollViewer { Content = items, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _googleWelcome = new Border { Background = Brushes.Black, Child = scroll };
        Grid.SetColumnSpan(_googleWelcome, _root.ColumnDefinitions.Count); Grid.SetRowSpan(_googleWelcome, 3);
        _root.Children.Add(_googleWelcome); Animate(items); signIn.Focus();
    }

    private async Task OpenLegalAsync(string page)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(new Uri("https://flugeru.github.io/Hibike/" + page));
    }

    private bool DismissGoogleWelcome()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_music.DataDirectory);
            System.IO.File.WriteAllText(GoogleWelcomePath, "1");
        }
        catch (Exception)
        {
            _music.SetStatus("Não foi possível salvar preferência de boas-vindas. Confira armazenamento disponível.");
            return false;
        }
        if (_googleWelcome is not null) _root.Children.Remove(_googleWelcome);
        _googleWelcome = null; RefreshProfile();
        if (!_music.OnboardingCompleted) ShowMusicOnboarding();
        return true;
    }
}
