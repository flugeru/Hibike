using Android.Content;
using Android.Security.Keystore;
using Hibike.App;
using Hibike.Core.Services;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Text;

namespace Hibike.Android;

internal static class WebSessionStore
{
    private const string Alias = "Hibike.GoogleSession";
    private static Java.Security.IKey Key()
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!; store.Load(null);
        if (store.ContainsAlias(Alias)) return store.GetKey(Alias, null)!;
        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes!, "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm!).SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone!).Build();
        generator.Init(spec); return generator.GenerateKey()!;
    }
    internal static void Configure(Context context)
    {
        var path = Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "google-session.bin");
        AppServices.SaveYouTubeAccount = account =>
        {
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, Key());
            var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(account.ExportWebSession()))!;
            File.WriteAllBytes(path + ".tmp", cipher.GetIV()!.Concat(encrypted).ToArray()); File.Move(path + ".tmp", path, true);
        };
        AppServices.RestoreYouTubeAccount = () =>
        {
            if (!File.Exists(path)) return Task.FromResult<YouTubeAccountLibrary?>(null);
            var data = File.ReadAllBytes(path);
            if (data.Length < 28 || data.Length > 100000) throw new IOException("Sessão salva inválida. Entre novamente.");
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            using var spec = new GCMParameterSpec(128, data[..12]); cipher.Init(CipherMode.DecryptMode, Key(), spec);
            return Task.FromResult<YouTubeAccountLibrary?>(YouTubeAccountLibrary.FromWebSession(WebAccountLogin.Client, Encoding.UTF8.GetString(cipher.DoFinal(data[12..])!)));
        };
        AppServices.ClearYouTubeAccount = () => { File.Delete(path); File.Delete(path + ".tmp"); };
    }
}
