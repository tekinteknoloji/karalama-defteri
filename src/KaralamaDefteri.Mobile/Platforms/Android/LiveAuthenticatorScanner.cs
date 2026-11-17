using Android.App;
using Android.Hardware;
using Android.Views;
using Android.Widget;
using ZXing;
using Camera = Android.Hardware.Camera;

namespace KaralamaDefteri.Mobile;

// Use the platform preview API directly so frames can be decoded locally without taking a photograph.
#pragma warning disable CS0618
internal sealed class LiveAuthenticatorScanner(Activity activity, Func<string, bool> accept) :
    Java.Lang.Object, ISurfaceHolderCallback, Camera.IPreviewCallback
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BarcodeReaderGeneric reader = new() { AutoRotate = true };
    private Dialog? dialog;
    private TextView? hint;
    private Camera? camera;
    private int width, height;
    private bool decoding;
    private long nextFrame;
    private string? previous;

    internal async Task Run()
    {
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        reader.Options.TryHarder = true;
        using var ui = new Dialog(activity);
        dialog = ui;
        var layout = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        hint = new TextView(activity) { Text = "QR kodunu kameraya göster.", TextSize = 16 };
        hint.SetPadding(20, 20, 20, 20);
        layout.AddView(hint);
        var surface = new SurfaceView(activity);
        surface.Holder?.AddCallback(this);
        layout.AddView(surface, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        var cancel = new Android.Widget.Button(activity) { Text = "Vazgeç" };
        cancel.Click += (_, _) => ui.Dismiss();
        layout.AddView(cancel);
        ui.SetContentView(layout);
        ui.DismissEvent += (_, _) =>
        {
            dialog = null;
            ReleaseCamera();
            completion.TrySetResult();
        };
        ui.Window?.AddFlags(WindowManagerFlags.Secure);
        ui.Show();
        ui.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        await completion.Task;
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        try
        {
            using var info = new Camera.CameraInfo();
            var selected = 0;
            for (var index = 0; index < Camera.NumberOfCameras; index++)
            {
                Camera.GetCameraInfo(index, info);
                if (info.Facing != CameraFacing.Back) continue;
                selected = index;
                break;
            }
            Camera.GetCameraInfo(selected, info);
            var opened = camera = Camera.Open(selected) ?? throw new InvalidOperationException();
            using var parameters = opened.GetParameters() ?? throw new InvalidOperationException();
            var size = parameters.SupportedPreviewSizes?.OrderBy(s => Math.Abs(s.Width - 1280)).FirstOrDefault();
            if (size != null) parameters.SetPreviewSize(size.Width, size.Height);
            parameters.PreviewFormat = Android.Graphics.ImageFormatType.Nv21;
            if (parameters.SupportedFocusModes?.Contains("continuous-picture") == true)
                parameters.FocusMode = "continuous-picture";
            opened.SetParameters(parameters);
            width = parameters.PreviewSize!.Width;
            height = parameters.PreviewSize.Height;
            var rotation = (int)(activity.WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation0) * 90;
            opened.SetDisplayOrientation((info.Orientation - rotation + 360) % 360);
            opened.SetPreviewDisplay(holder);
            opened.SetPreviewCallback(this);
            opened.StartPreview();
        }
        catch (Exception error) when (error is Java.Lang.Exception or IOException or InvalidOperationException)
        {
            ReleaseCamera();
            if (hint != null) hint.Text = "Kamera açılamadı. Kapatıp tekrar deneyin.";
        }
    }

    public void SurfaceChanged(ISurfaceHolder holder, Android.Graphics.Format format, int width, int height) { }
    public void SurfaceDestroyed(ISurfaceHolder holder) => ReleaseCamera();

    public async void OnPreviewFrame(byte[]? data, Camera? source)
    {
        var now = Environment.TickCount64;
        if (data == null || decoding || now < nextFrame || dialog == null) return;
        decoding = true;
        nextFrame = now + 250;
        try
        {
            var frame = new PlanarYUVLuminanceSource(data, width, height, 0, 0, width, height, false);
            var value = await Task.Run(() => reader.Decode(frame)?.Text);
            if (dialog == null || value == null || value == previous ||
                !value.StartsWith("otpauth", StringComparison.OrdinalIgnoreCase)) return;
            previous = value;
            if (accept(value)) dialog.Dismiss();
            else if (hint != null) hint.Text = "Aktarım tamamlanmadı. Sonraki QR bölümünü göster.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (hint != null) hint.Text = "QR işlenemedi. Tekrar deneyin.";
        }
        finally { decoding = false; }
    }

    private void ReleaseCamera()
    {
        if (camera is not { } opened) return;
        camera = null;
        try { opened.SetPreviewCallback(null); opened.StopPreview(); }
        catch (Java.Lang.RuntimeException) { }
        finally { opened.Release(); opened.Dispose(); }
    }
}
#pragma warning restore CS0618
