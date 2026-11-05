using Avalonia.Controls;
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
    private CancellationTokenSource? _accountNetworkRequest;
    private bool _pullDispatchPending;
    private async Task RestoreAccountAsync()
    {
        if (!_music.CanUseOnline || _accountRestoreAttempted || _account is not null) return;
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
            if (_account is not null)
            {
                PlaybackDiagnostics.Write(DiagnosticSector.Google, "Restaurar sessão", "Sessão local aberta.");
                await LoadAccountHomeAsync();
            }
        }
        catch (Exception error)
        {
            PlaybackDiagnostics.WriteFailure(DiagnosticSector.Google, "Restaurar sessão", error);
            _accountRestoreFailed = true;
            _accountError = "Não foi possível abrir sessão salva. Tente conexão novamente ou entre novamente.";
            _music.SetStatus(_accountError);
        }
    }
    private Task RefreshHomeAsync() => RefreshHomeAsync(fromGesture: false);

    private async Task RefreshHomeAsync(bool fromGesture)
    {
        if (_refreshingHome || !_music.CanUseOnline) return;
        _refreshingHome = true;
        try
        {
            await WithHeaderLoadingAsync("Atualizando dados…", async () =>
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (!_music.CanUseOnline) return;
                AppServices.InvalidateCatalog();
                var recommendations = _music.RefreshRecommendationsAsync(true);
                await Task.WhenAll(RefreshAccountHomeAsync(), recommendations);
            }, keepFeedbackVisible: fromGesture);
            if (_music.CanUseOnline) _music.SetStatus(_accountError ?? "Início atualizado.");
        }
        catch (Exception error)
        {
            PlaybackDiagnostics.WriteFailure(DiagnosticSector.App, "Atualizar início", error);
            _music.SetStatus("Não foi possível atualizar início. Tente conexão novamente.");
        }
        finally { _refreshingHome = false; }
    }

    private async Task RefreshAccountHomeAsync()
    {
        if (_accountRestoreFailed || !_accountRestoreAttempted && _account is null)
        {
            _accountRestoreAttempted = false;
            await RestoreAccountAsync();
        }
        else
            await LoadAccountHomeAsync();
    }

    private void InitializeAccountRefresh()
    {
        _accountRefreshTimer.Tick += async (_, _) =>
        {
            if (_account is not null && !_accountExpired && (OperatingSystem.IsAndroid() || TopLevel.GetTopLevel(this) is Window { IsActive: true })) await RefreshHomeAsync();
        };
        var visualizer = new RefreshVisualizer { Opacity = 0, IsHitTestVisible = false };
        _homeRefreshContainer = new RefreshContainer { Content = _homeScroll, Visualizer = visualizer };
        visualizer.PropertyChanged += (_, change) =>
        {
            if (change.Property != RefreshVisualizer.RefreshVisualizerStateProperty) return;
            if (_pullDispatchPending) return;
            _pullLoadingHint = visualizer.GetValue(RefreshVisualizer.RefreshVisualizerStateProperty) switch
            {
                RefreshVisualizerState.Interacting => "Puxe para atualizar",
                RefreshVisualizerState.Pending => "Solte para atualizar",
                _ => null
            };
            RefreshHeaderLoading();
        };
        _homeRefreshContainer.RefreshRequested += (_, _) =>
        {
            // Let Avalonia settle the pull immediately. The header owns the network feedback.
            if (_pullDispatchPending) return;
            _pullDispatchPending = true;
            Dispatcher.UIThread.Post(async () =>
            {
                _pullDispatchPending = false;
                _pullLoadingHint = null;
                if (_homeRefreshContainer.IsEffectivelyVisible && _page == "home" && !_showingPlayer)
                    await RefreshHomeAsync(fromGesture: true);
                RefreshHeaderLoading();
            }, DispatcherPriority.Background);
        };
    }
}
