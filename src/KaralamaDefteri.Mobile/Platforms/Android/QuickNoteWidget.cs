using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace KaralamaDefteri.Mobile;

[BroadcastReceiver(Exported = false, Label = "Hızlı not")]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/karalama_widget")]
public sealed class QuickNoteWidget : AppWidgetProvider
{
    internal static bool RequestPin()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) || Platform.CurrentActivity is not { } activity) return false;
        var manager = AppWidgetManager.GetInstance(activity);
        return manager?.IsRequestPinAppWidgetSupported == true &&
            manager.RequestPinAppWidget(new ComponentName(activity, Java.Lang.Class.FromType(typeof(QuickNoteWidget))), null, null);
    }

    public override void OnUpdate(Context? context, AppWidgetManager? manager, int[]? ids)
    {
        if (context is null || manager is null || ids is null) return;
        using var intent = new Intent(context, typeof(MainActivity));
        intent.SetAction(MainActivity.NewNoteAction);
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        using var shortcut = PendingIntent.GetActivity(context, 55, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        using var widget = new RemoteViews(context.PackageName, Resource.Layout.karalama_widget);
        widget.SetOnClickPendingIntent(Resource.Id.quick_note, shortcut);
        foreach (var id in ids) manager.UpdateAppWidget(id, widget);
    }
}
