using Android.Hardware.Biometrics;
using Android.OS;
using Android.Views;

namespace KaralamaDefteri.Mobile;

internal static class AndroidUnlock
{
    private const int StrongBiometricOrCredential = 0x000F | 0x8000;

    internal static bool Available() => OperatingSystem.IsAndroidVersionAtLeast(30) &&
        Platform.CurrentActivity?.GetSystemService(Java.Lang.Class.FromType(typeof(BiometricManager))) is BiometricManager manager &&
        (int)manager.CanAuthenticate(StrongBiometricOrCredential) == 0;

    internal static async Task<bool> Verify()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || Platform.CurrentActivity is not { } activity || !Available())
            return false;
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationSignal();
        using var callback = new Callback(result);
        using var prompt = new BiometricPrompt.Builder(activity)
            .SetTitle("Karalama Defteri kilidini aç")
            .SetAllowedAuthenticators(StrongBiometricOrCredential)
            .Build();
        prompt.Authenticate(cancel, activity.MainExecutor!, callback);
        return await result.Task;
    }

    internal static void ProtectScreen()
    {
        if (Platform.CurrentActivity?.Window is { } window)
            window.AddFlags(WindowManagerFlags.Secure);
    }

    internal static void ReleaseScreen()
    {
        if (Platform.CurrentActivity?.Window is { } window)
            window.ClearFlags(WindowManagerFlags.Secure);
    }

    private sealed class Callback(TaskCompletionSource<bool> result) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? authenticationResult) => result.TrySetResult(true);
        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) => result.TrySetResult(false);
    }
}
