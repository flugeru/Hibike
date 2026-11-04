using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly DispatcherTimer _accountRefreshTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private bool _accountRestoreAttempted, _refreshingHome, _accountExpired;
    private string? _accountError;
    private RefreshContainer? _homeRefreshContainer;
    private bool _accountRestoreFailed;
    private async Task RestoreAccountAsync()
    {
        if (_accountRestoreAttempted || _account is not null) return;
        if (AppServices.RestoreYouTubeAccount is not { } restore)
        {
            _accountRestoreFailed = true;
            return;
        }
        _accountRestoreAttempted = true;
        try
        {
            _account = await restore();
            _accountRestoreFailed = false;
            if (_account is not null) await LoadAccountHomeAsync();
        }
        catch { _accountRestoreFailed = true; _accountError = "Não foi possível abrir sessão salva. Tente conexão novamente ou entre novamente."; _music.SetStatus(_accountError); }
    }
    private async Task RefreshHomeAsync()
    {
        if (_refreshingHome) return;
        _refreshingHome = true; _music.SetStatus("Atualizando início…");
        var loadingKey = new object(); _loadingOperations.Add(loadingKey, "Atualizando dados…"); RefreshLoading();
        try
        {
            AppServices.InvalidateCatalog();
            if (_accountRestoreFailed) { _accountRestoreAttempted = false; await RestoreAccountAsync(); }
            await Task.WhenAll(LoadAccountHomeAsync(), _music.RefreshRecommendationsAsync(true));
            _music.SetStatus(_accountError ?? "Início atualizado.");
        }
        catch { _music.SetStatus("Não foi possível atualizar início. Tente conexão novamente."); }
        finally { _refreshingHome = false; _loadingOperations.Remove(loadingKey); RefreshLoading(); }
    }
    private void InitializeAccountRefresh()
    {
        _accountRefreshTimer.Tick += async (_, _) =>
        {
            if (_account is not null && !_accountExpired && (OperatingSystem.IsAndroid() || TopLevel.GetTopLevel(this) is Window { IsActive: true })) await RefreshHomeAsync();
        };
        _homeRefreshContainer = new RefreshContainer { Content = _homeScroll, Visualizer = new RefreshVisualizer { Opacity = 0, IsHitTestVisible = false } };
        _homeRefreshContainer.RefreshRequested += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try { if (_page == "home" && !_showingPlayer) await RefreshHomeAsync(); }
            finally { deferral.Complete(); }
        };
    }
}
