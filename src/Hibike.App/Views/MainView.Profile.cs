using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hibike.Core.Models;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private Button? _profileButton;
    private YouTubeAccountLibrary? _profileAccount;
    private bool _spaceHeld;
    private void ResetPlaybackKey(object? sender, EventArgs e) => _spaceHeld = false;
    private void RefreshProfile()
    {
        if (_profileButton is null || ReferenceEquals(_profileAccount, _account) && _profileButton.Tag is not null) return;
        _profileAccount = _account; _profileButton.Tag = true;
        var size = _androidLayout ? UiTheme.TouchSize : 32;
        _profileButton.Content = _account?.AvatarUrl is { } url
            ? new Artwork(new Track("account-avatar", "Conta", "", "", Thumbnail: url), size - 4, highQuality: true)
                { CornerRadius = new((size - 4) / 2), BorderThickness = UiTheme.Zero }
            : Icons.Make(_account is null ? "account-off" : "account", Muted, 24);
        Avalonia.Automation.AutomationProperties.SetName(_profileButton, _account?.DisplayName is { Length: > 0 } name ? "Perfil de " + name : _account is null ? "Perfil · sem conta" : "Perfil · conta conectada");
    }
    private bool CanUsePlaybackKey(KeyEventArgs e) => !OperatingSystem.IsAndroid()
        && TopLevel.GetTopLevel(this) is Window { IsActive: true }
        && _googleWelcome is null && _onboarding is null
        && e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None
        && !(e.Source is Avalonia.Visual visual && visual.GetSelfAndVisualAncestors().OfType<TextBox>().Any());
    private async void OnPlaybackKeyDown(object? sender, KeyEventArgs e)
    {
        if (!CanUsePlaybackKey(e)) return;
        e.Handled = true;
        if (_spaceHeld) return;
        _spaceHeld = true;
        try { await _music.ToggleAsync(); }
        catch (Exception error) { _music.SetStatus("Não foi possível alterar reprodução: " + error.Message); }
    }
    private void OnPlaybackKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        if (_spaceHeld) e.Handled = true;
        _spaceHeld = false;
    }
}
