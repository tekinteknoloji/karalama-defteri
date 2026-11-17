using Android.App;
using Android.Content.PM;
using Android.Content;
using Android.OS;
using Android.Views;

namespace KaralamaDefteri.Mobile;

[Activity(Name = "com.tekinteknoloji.karalamadefteri.MainActivity", Theme = "@style/Maui.MainTheme.NoActionBar",
    MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges =
    ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
    ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/*",
    Label = "Karalama Defteri'ne kaydet")]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "image/*",
    Label = "Karalama Defteri'ne kaydet")]
[MetaData("android.app.shortcuts", Resource = "@xml/karalama_shortcuts")]
public sealed class MainActivity : MauiAppCompatActivity
{
    internal const string NewNoteAction = "com.tekinteknoloji.karalamadefteri.NEW_NOTE";
    private const int DriveRequest = 45054;
    private const int DriveAccountRequest = 45055;
    private TaskCompletionSource<Intent?>? driveResult;
    private TaskCompletionSource<string?>? driveAccountResult;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new AppBackCallback(this));
        if (savedInstanceState is null) Receive(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Receive(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplySystemBarTheme();
    }

    protected override async void OnPostResume()
    {
        base.OnPostResume();
        if (Microsoft.Maui.Controls.Application.Current is MobileApp app)
            await app.OnAndroidPostResume();
    }

    protected override void OnStop()
    {
        if (Microsoft.Maui.Controls.Application.Current is MobileApp app)
            app.OnAndroidStopped();
        base.OnStop();
    }

    private sealed class AppBackCallback(MainActivity activity) : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            if (Microsoft.Maui.Controls.Application.Current is MobileApp app && app.HandleBack()) return;
            activity.MoveTaskToBack(true);
        }
    }

    internal void ApplySystemBarTheme()
    {
        if (Window is not { } window || Microsoft.Maui.Controls.Application.Current is not { } app) return;
        var theme = app.UserAppTheme == AppTheme.Unspecified ? app.RequestedTheme : app.UserAppTheme;
        var light = theme != AppTheme.Dark;
        var background = Android.Graphics.Color.ParseColor(light ? MobileStyle.LightBackground : MobileStyle.DarkBackground);
        if (OperatingSystem.IsAndroidVersionAtLeast(28) && window.Attributes is { } attributes &&
            attributes.LayoutInDisplayCutoutMode != LayoutInDisplayCutoutMode.ShortEdges)
        {
            attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            window.Attributes = attributes;
        }
        window.SetStatusBarColor(background);
        window.SetNavigationBarColor(background);
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            window.StatusBarContrastEnforced = false;
            window.NavigationBarContrastEnforced = false;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            const int lightSystemBars = 8 | 16; // APPEARANCE_LIGHT_STATUS_BARS | APPEARANCE_LIGHT_NAVIGATION_BARS.
            window.InsetsController?.SetSystemBarsAppearance(light ? lightSystemBars : 0, lightSystemBars);
            return;
        }
#pragma warning disable CA1422, CS0618 // Legacy flags are used on Android before API 30.
        if (window.DecorView is { } decor)
        {
            var mask = SystemUiFlags.LightStatusBar;
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) mask |= SystemUiFlags.LightNavigationBar;
            var flags = (SystemUiFlags)decor.SystemUiVisibility;
            decor.SystemUiVisibility = (StatusBarVisibility)((flags & ~mask) | (light ? mask : 0));
        }
#pragma warning restore CA1422, CS0618
    }

    private async void Receive(Intent? intent)
    {
        if (intent is null || Microsoft.Maui.Controls.Application.Current is not MobileApp app) return;
        if (intent.Action == NewNoteAction) { app.QueueEntry(new()); return; }
        if (intent.Action != Intent.ActionSend) return;
        try
        {
            if (intent.Type?.StartsWith("image/", StringComparison.Ordinal) == true)
            {
                var uri = OperatingSystem.IsAndroidVersionAtLeast(33)
                    ? intent.GetParcelableExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(Android.Net.Uri))) as Android.Net.Uri
                    : intent.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
                uri ??= intent.ClipData?.GetItemAt(0)?.Uri;
                if (uri?.Scheme != "content") throw new InvalidDataException();
                var mime = ContentResolver!.GetType(uri) ?? intent.Type;
                var extension = mime switch
                {
                    "image/png" => ".png", "image/jpeg" => ".jpg", "image/gif" => ".gif",
                    "image/webp" => ".webp", "image/bmp" => ".bmp", _ => ".png"
                };
                var picture = await Task.Run(async () =>
                {
                    await using var input = ContentResolver.OpenInputStream(uri) ?? throw new IOException();
                    return await MobileEntry.ReadPicture(input, "Paylaşılan resim" + extension);
                });
                app.QueueEntry(new(Picture: picture));
            }
            else if (intent.GetCharSequenceExtra(Intent.ExtraText)?.ToString() is { Length: > 0 } text)
                app.QueueEntry(new(Text: text));
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(error.GetType().Name);
            Android.Widget.Toast.MakeText(this, "Paylaşım okunamadı.", Android.Widget.ToastLength.Short)?.Show();
        }
    }

    internal async Task<Intent?> ResolveGoogleDrive(PendingIntent pending)
    {
        if (driveResult is not null) return null;
        var completion = new TaskCompletionSource<Intent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        driveResult = completion;
        try
        {
#pragma warning disable CS0618 // Google Play services supplies an IntentSender for consent.
            StartIntentSenderForResult(pending.IntentSender, DriveRequest, null, 0, 0, 0);
#pragma warning restore CS0618
            return await completion.Task;
        }
        finally { driveResult = null; }
    }

    internal async Task<string?> ChooseDriveAccount()
    {
        if (driveAccountResult is not null) return null;
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        driveAccountResult = completion;
        try
        {
            using var chooser = Android.Accounts.AccountManager.NewChooseAccountIntent(null, null, ["com.google"], null, null, null, null);
#pragma warning disable CS0618 // Receive the account selection in OnActivityResult.
            StartActivityForResult(chooser, DriveAccountRequest);
#pragma warning restore CS0618
            return await completion.Task;
        }
        finally { driveAccountResult = null; }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == DriveRequest) driveResult?.TrySetResult(resultCode == Result.Ok ? data : null);
        if (requestCode == DriveAccountRequest)
            driveAccountResult?.TrySetResult(resultCode == Result.Ok ? data?.GetStringExtra(Android.Accounts.AccountManager.KeyAccountName) : null);
    }

    protected override void OnDestroy()
    {
        driveResult?.TrySetResult(null);
        driveAccountResult?.TrySetResult(null);
        base.OnDestroy();
    }
}
