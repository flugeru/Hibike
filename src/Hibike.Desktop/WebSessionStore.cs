#if WINDOWS
using System.Security.Cryptography;
using System.Text;
using Hibike.App;
using Hibike.Core.Services;

namespace Hibike.Desktop;

internal static class WebSessionStore
{
    internal static void Configure(string? storageDirectory = null)
    {
        var directory = storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hibike-Account");
        var path = Path.Combine(directory, "google-session.bin");
        AppServices.SaveYouTubeAccount = account =>
        {
            Directory.CreateDirectory(directory);
            var bytes = Encoding.UTF8.GetBytes(account.ExportWebSession());
            try
            {
                var data = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path + ".tmp", data); File.Move(path + ".tmp", path, true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                try { File.Delete(path + ".tmp"); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        };
        AppServices.RestoreYouTubeAccount = () =>
        {
            if (!File.Exists(path)) return Task.FromResult<YouTubeAccountLibrary?>(null);
            if (new FileInfo(path).Length > 100000) throw new IOException("Sessão salva inválida. Entre novamente.");
            return Task.FromResult<YouTubeAccountLibrary?>(YouTubeAccountLibrary.FromWebSession(WebAccountLogin.Client, Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser))));
        };
        AppServices.ClearYouTubeAccount = () => { File.Delete(path); File.Delete(path + ".tmp"); };
    }
}
#endif
