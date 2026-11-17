using Android.Content;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common.Apis;
using Android.Gms.Tasks;

namespace KaralamaDefteri.Mobile;

internal static class GoogleDriveAuthorization
{
    private const string ScopeName = "https://www.googleapis.com/auth/drive.appdata";

    internal static Task<string?> ChooseAccount() => Platform.CurrentActivity is MainActivity activity
        ? activity.ChooseDriveAccount() : System.Threading.Tasks.Task.FromResult<string?>(null);

    internal static async Task<string?> GetToken(bool interactive, string? accountName)
    {
        if (Platform.CurrentActivity is not MainActivity activity) return null;
        var client = Identity.GetAuthorizationClient(activity);
        using var scope = new Scope(ScopeName);
        using var account = accountName is null ? null : new Android.Accounts.Account(accountName, "com.google");
        using var builder = AuthorizationRequest.InvokeBuilder().SetRequestedScopes([scope]);
        if (account is not null) builder.SetAccount(account);
        using var request = builder.Build();
        var completion = new TaskCompletionSource<AuthorizationResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new AuthorizationListener(completion);
        client.Authorize(request).AddOnCompleteListener(listener);
        using var result = await completion.Task;
        if (result is null) return null;
        if (!result.HasResolution) return result.AccessToken;
        if (!interactive || result.PendingIntent is not { } pending) return null;
        var data = await activity.ResolveGoogleDrive(pending);
        return data is null ? null : client.GetAuthorizationResultFromIntent(data)?.AccessToken;
    }

    private sealed class AuthorizationListener(TaskCompletionSource<AuthorizationResult?> completion)
        : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful) completion.TrySetResult(task.Result as AuthorizationResult);
            else if (task.Exception is { } error) completion.TrySetException(error);
            else completion.TrySetResult(null);
        }
    }
}
