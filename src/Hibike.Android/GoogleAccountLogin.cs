using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using Hibike.Core.Services;

namespace Hibike.Android;

internal sealed class GoogleAccountLogin(Activity activity)
{
    private const int RequestCode = 7401;
    private const string Scope = "https://www.googleapis.com/auth/youtube.readonly";
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    private TaskCompletionSource<AuthorizationResult>? _pending;
    private readonly CancellationTokenSource _lifetime = new();

    public async Task<YouTubeAccountLibrary> ConnectAsync()
    {
        if (_pending is not null) throw new InvalidOperationException("Login Google já está em andamento.");
        var pending = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var request = AuthorizationRequest.InvokeBuilder().SetRequestedScopes(new[] { new Scope(Scope) })!.Build();
            var result = await Identity.GetAuthorizationClient(activity).Authorize(request).AsAsync<AuthorizationResult>().WaitAsync(timeout.Token);
            if (result.HasResolution)
            {
                activity.StartIntentSenderForResult(result.PendingIntent!.IntentSender, RequestCode, null, 0, 0, 0);
                result = await pending.Task.WaitAsync(timeout.Token);
            }
            if (string.IsNullOrWhiteSpace(result.AccessToken) || !result.GrantedScopes.Contains(Scope))
                throw new InvalidOperationException("Permissão de leitura do YouTube não concedida.");
            // ponytail: token only in memory; reauthorize when expired instead of persisting credentials.
            return new YouTubeAccountLibrary(Client, result.AccessToken);
        }
        catch (ApiException error)
        {
            throw new InvalidOperationException($"Autorização Google falhou (código {error.StatusCode}). Confira YouTube Data API, usuário de teste e cadastro Android com pacote e SHA-1 deste APK.");
        }
        finally { if (ReferenceEquals(_pending, pending)) _pending = null; }
    }

    public bool HandleResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode) return false;
        if (_pending is not { } pending) return true;
        if (resultCode != Result.Ok || data is null)
            pending.TrySetException(new InvalidOperationException("Autorização Google cancelada."));
        else
        {
            try { pending.TrySetResult(Identity.GetAuthorizationClient(activity).GetAuthorizationResultFromIntent(data)); }
            catch (ApiException error) { pending.TrySetException(error); }
        }
        return true;
    }

    public void Cancel() { _lifetime.Cancel(); _pending?.TrySetCanceled(); }
}
