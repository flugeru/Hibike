namespace Hibike.Core.Services;

public sealed class AccountSessionExpiredException() : InvalidOperationException("Sessão Google expirada ou revogada. Entre novamente.");
