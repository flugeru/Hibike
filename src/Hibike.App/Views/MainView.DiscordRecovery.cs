using Avalonia.Threading;
using Hibike.Core.Services;

namespace Hibike.App.Views;

public sealed partial class MainView
{
    private readonly DispatcherTimer _discordRetryTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private DiscordAccountPresence? _discordAccount;
    private bool _discordRecoveryActive;
    private bool _discordConnecting;
    private bool _discordRetryAllowed;
    private string? _discordRecoveryStatus;
    private event Action? DiscordConnectionChanged;

    private void InitializeDiscordRecovery()
    {
        _discordRetryTimer.Tick += async (_, _) =>
        {
            if (_discordAccount is { IsClosed: false } && !_discordConnecting)
            {
                if (AppServices.HasNetwork) return;
                ReplaceDiscordConnection(null);
                ScheduleDiscordRetry();
                return;
            }
            _discordRetryTimer.Stop();
            await RestoreDiscordAsync();
        };
    }

    private void StartDiscordRecovery()
    {
        _discordRecoveryActive = true;
        _discordRetryAllowed = true;
        _ = RestoreDiscordAsync();
    }

    private void StopDiscordRecovery()
    {
        _discordRecoveryActive = false;
        _discordRetryTimer.Stop();
        ReplaceDiscordConnection(null);
    }

    private void ReplaceDiscordConnection(DiscordAccountPresence? connection)
    {
        if (_discordAccount is not null)
        {
            _discordAccount.Changed -= OnDiscordConnectionChanged;
            _discordAccount.Dispose();
        }
        _discordAccount = connection;
        if (connection is not null) connection.Changed += OnDiscordConnectionChanged;
        DiscordConnectionChanged?.Invoke();
    }

    private void OnDiscordConnectionChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_discordAccount is { IsClosed: true } closed && !_discordConnecting)
                ScheduleDiscordRetry(closed.CredentialsRejected);
            DiscordConnectionChanged?.Invoke();
        });
    }

    private void ScheduleDiscordRetry(bool credentialsRejected = false)
    {
        if (credentialsRejected)
        {
            _discordRetryTimer.Stop();
            _discordRetryAllowed = false;
            _discordRecoveryStatus = "Token inválido ou expirado. Informe um novo token.";
        }
        else if (_discordRecoveryActive && _discordRetryAllowed && AppServices.DiscordTokenStorage?.Exists() == true)
        {
            _discordRecoveryStatus = AppServices.HasNetwork
                ? "Conexão interrompida. Nova tentativa em 30 segundos."
                : "Sem internet. O Discord será reconectado quando a rede voltar.";
            _discordRetryTimer.Stop();
            _discordRetryTimer.Start();
        }
        DiscordConnectionChanged?.Invoke();
    }

    private async Task RestoreDiscordAsync()
    {
        if (!_discordRecoveryActive || !_discordRetryAllowed || _discordConnecting
            || _discordAccount is { IsClosed: false } || AppServices.DiscordTokenStorage is not { } storage
            || !storage.Exists()) return;

        if (!AppServices.HasNetwork)
        {
            ScheduleDiscordRetry();
            return;
        }

        string? token;
        try { token = storage.Read(); }
        catch (Exception error)
        {
            PlaybackDiagnostics.WriteFailure(DiagnosticSector.Discord, "Restaurar token local", error);
            _discordRetryAllowed = false;
            _discordRecoveryStatus = "Não foi possível ler o token salvo. Informe um novo token.";
            DiscordConnectionChanged?.Invoke();
            return;
        }
        if (!string.IsNullOrWhiteSpace(token)) await ConnectDiscordAsync(token, saved: true, remember: false);
    }

    private async Task ConnectDiscordAsync(string token, bool saved, bool remember)
    {
        if (_discordConnecting) return;
        if (saved && !AppServices.HasNetwork)
        {
            _discordRetryAllowed = true;
            ScheduleDiscordRetry();
            return;
        }
        _discordRetryTimer.Stop();
        _discordConnecting = true;
        _discordRetryAllowed = saved;
        _discordRecoveryStatus = "Conectando…";
        var connection = new DiscordAccountPresence(_music);
        ReplaceDiscordConnection(connection);
        try
        {
            await connection.ConnectAsync(token);
            if (!ReferenceEquals(_discordAccount, connection)) return;
            if (!saved && AppServices.DiscordTokenStorage is { } storage)
            {
                if (remember) storage.Save(token);
                else storage.Clear();
            }
            _discordRetryAllowed = saved || remember;
            _discordRecoveryStatus = null;
            if (_discordRecoveryActive && _discordRetryAllowed && AppServices.DiscordTokenStorage?.Exists() == true)
                _discordRetryTimer.Start();
        }
        catch (Exception error)
        {
            if (!ReferenceEquals(_discordAccount, connection)) return;
            if (!connection.IsClosed)
            {
                PlaybackDiagnostics.WriteFailure(DiagnosticSector.Discord, "Atualizar token local", error);
                _discordRecoveryStatus = "Discord conectado. Não foi possível atualizar o token salvo.";
            }
            else
            {
                _discordRecoveryStatus = connection.Status;
                ScheduleDiscordRetry(connection.CredentialsRejected);
            }
        }
        finally
        {
            _discordConnecting = false;
            DiscordConnectionChanged?.Invoke();
            if (!ReferenceEquals(_discordAccount, connection) && _discordRecoveryActive)
                _ = RestoreDiscordAsync();
        }
    }
}
