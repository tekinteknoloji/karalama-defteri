using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KaralamaDefteri;

internal static class Theme
{
    private static ResourceDictionary? palette;
    private static readonly Uri IconUri = new("pack://application:,,,/KaralamaDefteri;component/Resources/app.ico");
    public static ImageSource Icon { get; } = BitmapFrame.Create(IconUri);
    public static ImageSource AboutIcon { get; } = BitmapDecoder.Create(IconUri, BitmapCreateOptions.None,
        BitmapCacheOption.OnLoad).Frames.First(frame => frame.PixelWidth >= 48);
    public static System.Drawing.Icon TrayIcon()
    {
        using var stream = Application.GetResourceStream(IconUri).Stream;
        using var icon = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)icon.Clone();
    }

    public static bool IsDark { get; private set; }

    public static bool Resolve(string? choice) => choice == "Koyu" || choice is not ("Açık" or "Koyu") &&
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is 0;

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary
        {
            Source = new Uri($"/KaralamaDefteri;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };
        if (palette != null) dictionaries.Remove(palette);
        dictionaries.Insert(0, next);
        palette = next;
        foreach (Window window in Application.Current.Windows) SetCaption(window);
    }

    public static Brush Brush(string role) => (Brush)Application.Current.FindResource($"{role}Brush");

    public static void Configure(Window window)
    {
        window.SetResourceReference(ControlBackground, "WindowBrush");
        window.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "InkBrush");
        window.Icon = Icon;
        window.SourceInitialized += (_, _) => SetCaption(window);
    }

    private static DependencyProperty ControlBackground => System.Windows.Controls.Control.BackgroundProperty;

    private static void SetCaption(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        var caption = IsDark ? 0x001D1E1F : 0x00E6EEEF;
        var ink = IsDark ? 0x00EDF4F5 : 0x00131414;
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref ink, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
