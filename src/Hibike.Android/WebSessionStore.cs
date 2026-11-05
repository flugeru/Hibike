using Android.Content;
using Android.Security.Keystore;
using Hibike.App;
using Hibike.Core.Services;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using CryptographicOperations = System.Security.Cryptography.CryptographicOperations;
using System.Text;

namespace Hibike.Android;

internal static class WebSessionStore
{
    private static Java.Security.IKey Key(string alias, bool create)
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (store.ContainsAlias(alias)) return store.GetKey(alias, null)!;
        if (!create) throw new IOException("Chave de armazenamento indisponível.");
        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes!, "AndroidKeyStore")!;
        using var spec = new KeyGenParameterSpec.Builder(alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm!).SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone!).Build();
        generator.Init(spec);
        return generator.GenerateKey()!;
    }

    private static void Save(string path, string alias, string value)
    {
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var key = Key(alias, create: true);
        cipher.Init(CipherMode.EncryptMode, key);
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var encrypted = cipher.DoFinal(bytes)!;
            File.WriteAllBytes(path + ".tmp", cipher.GetIV()!.Concat(encrypted).ToArray());
            File.Move(path + ".tmp", path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            File.Delete(path + ".tmp");
        }
    }

    private static string? Read(string path, string alias, int maxLength)
    {
        if (!File.Exists(path)) return null;
        var data = File.ReadAllBytes(path);
        if (data.Length < 28 || data.Length > maxLength) throw new IOException("Credencial salva inválida.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var spec = new GCMParameterSpec(128, data[..12]);
        using var key = Key(alias, create: false);
        cipher.Init(CipherMode.DecryptMode, key, spec);
        var bytes = cipher.DoFinal(data[12..])!;
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void Clear(string path)
    {
        File.Delete(path);
        File.Delete(path + ".tmp");
    }

    internal static void Configure(Context context)
    {
        var directory = context.NoBackupFilesDir!.AbsolutePath;
        var google = Path.Combine(directory, "google-session.bin");
        const string googleAlias = "Hibike.GoogleSession";
        AppServices.SaveYouTubeAccount = account => Save(google, googleAlias, account.ExportWebSession());
        AppServices.RestoreYouTubeAccount = () =>
        {
            var session = Read(google, googleAlias, 100000);
            return Task.FromResult<YouTubeAccountLibrary?>(session is null ? null : YouTubeAccountLibrary.FromWebSession(WebAccountLogin.Client, session));
        };
        AppServices.ClearYouTubeAccount = () => Clear(google);

        var discord = Path.Combine(directory, "discord-token.bin");
        const string discordAlias = "Hibike.DiscordToken";
        AppServices.DiscordTokenStorage = (() => Read(discord, discordAlias, 2080),
            token => Save(discord, discordAlias, token), () => Clear(discord), () => File.Exists(discord));
    }
}
