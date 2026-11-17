using Android.Hardware.Biometrics;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Security.Cryptography;
using System.Text;
using CipherMode = Javax.Crypto.CipherMode;

namespace KaralamaDefteri.Mobile;

internal static class BiometricPassword
{
    private const string Alias = "karalama.database.biometric.v1";
    private const string WrappedPreference = "mobileBiometricWrapped";
    private const int StrongBiometric = 0x000F;
    private const int AuthBiometricStrong = 0x00000002;
    private const int NonceLength = 12;

    internal static bool HasPassword => Preferences.Default.ContainsKey(WrappedPreference);

    internal static bool Available() => OperatingSystem.IsAndroidVersionAtLeast(30) &&
        Platform.CurrentActivity?.GetSystemService(Java.Lang.Class.FromType(typeof(BiometricManager))) is BiometricManager manager &&
        (int)manager.CanAuthenticate(StrongBiometric) == 0;

    internal static async Task<bool> Enroll(string password)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || !Available()) return false;
        Forget();
        var enrolled = false;
        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var generator = KeyGenerator.GetInstance("AES", "AndroidKeyStore")!;
            using var builder = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
            using var parameters = builder
                .SetBlockModes("GCM")
                .SetEncryptionPaddings("NoPadding")
                .SetKeySize(256)
                .SetUserAuthenticationRequired(true)
                .SetUserAuthenticationParameters(0, AuthBiometricStrong)
                .SetInvalidatedByBiometricEnrollment(true)
                .Build();
            generator.Init(parameters);
            using var key = generator.GenerateKey();
            using var factory = SecretKeyFactory.GetInstance("AES", "AndroidKeyStore")!;
            using var info = factory.GetKeySpec(key, Java.Lang.Class.FromType(typeof(KeyInfo))) as KeyInfo;
#pragma warning disable CA1422 // Required for Android 11, where SecurityLevel is unavailable.
            if (info?.IsInsideSecureHardware != true) return false;
#pragma warning restore CA1422
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, key);
            if (!await Authenticate(cipher)) return false;
            var nonce = cipher.GetIV()!;
            if (nonce.Length != NonceLength) return false;
            var payload = cipher.DoFinal(bytes)!;
            Preferences.Default.Set(WrappedPreference, Convert.ToBase64String([.. nonce, .. payload]));
            enrolled = true;
            return true;
        }
        catch (Java.Lang.Exception) { return false; }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (!enrolled) Forget();
        }
    }

    internal static async Task<string?> Unlock()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || !Available() || !HasPassword) return null;
        try
        {
            var wrapped = Convert.FromBase64String(Preferences.Default.Get(WrappedPreference, ""));
            if (wrapped.Length <= NonceLength + 16) throw new FormatException();
            using var store = OpenKeyStore();
            using var key = store.GetKey(Alias, null);
            if (key is null) { Forget(); return null; }
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            using var parameters = new GCMParameterSpec(128, wrapped[..NonceLength]);
            cipher.Init(CipherMode.DecryptMode, key, parameters);
            if (!await Authenticate(cipher)) return null;
            var secret = cipher.DoFinal(wrapped[NonceLength..])!;
            try { return Encoding.UTF8.GetString(secret); }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }
        catch (Exception error) when (error is KeyPermanentlyInvalidatedException or AEADBadTagException or FormatException)
        {
            Forget();
            return null;
        }
        catch (Java.Lang.Exception) { return null; }
    }

    internal static void Forget()
    {
        Preferences.Default.Remove(WrappedPreference);
        try
        {
            using var store = OpenKeyStore();
            if (store.ContainsAlias(Alias)) store.DeleteEntry(Alias);
        }
        catch (Java.Lang.Exception) { }
    }

    private static KeyStore OpenKeyStore()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        return store;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
    private static async Task<bool> Authenticate(Cipher cipher)
    {
        if (Platform.CurrentActivity is not { } activity) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationSignal();
        using var callback = new Callback(completion);
        using var negative = new CancelButton(completion);
        using var crypto = new BiometricPrompt.CryptoObject(cipher);
        using var prompt = new BiometricPrompt.Builder(activity)
            .SetTitle("Karalama Defteri kilidini aç")
            .SetAllowedAuthenticators(StrongBiometric)
            .SetNegativeButton("Parolayla aç", activity.MainExecutor!, negative)
            .Build();
        prompt.Authenticate(crypto, cancel, activity.MainExecutor!, callback);
        return await completion.Task;
    }

    private sealed class Callback(TaskCompletionSource<bool> completion) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) => completion.TrySetResult(true);
        public override void OnAuthenticationError(BiometricErrorCode code, Java.Lang.ICharSequence? message) => completion.TrySetResult(false);
    }

    private sealed class CancelButton(TaskCompletionSource<bool> completion) : Java.Lang.Object, Android.Content.IDialogInterfaceOnClickListener
    {
        public void OnClick(Android.Content.IDialogInterface? dialog, int which) => completion.TrySetResult(false);
    }
}
