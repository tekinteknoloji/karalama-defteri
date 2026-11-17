using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shell;
using ICSharpCode.AvalonEdit;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private readonly Dictionary<long, DesktopNoteWindow> desktopWindows = [];

    internal void RestoreDesktopPins()
    {
        foreach (var id in store.DesktopPins()) PinCard(id);
    }

    internal void PinCard(long id)
    {
        if (!Features.Enabled(Feature.DesktopPins)) return;
        if (desktopWindows.TryGetValue(id, out var existing))
        {
            existing.Activate();
            return;
        }
        if (!editors.TryGetValue(id, out var pane))
        {
            if (store.Get(id) is not { Deleted: null } note) return;
            pane = new EditorPane(this, note);
            editors.Add(id, pane);
        }
        var window = new DesktopNoteWindow(this, pane, store.DesktopPin(id));
        desktopWindows.Add(id, window);
        window.Closing += (_, e) =>
        {
            if (exiting || !desktopWindows.ContainsKey(id)) return;
            if (!Flush(false) || !WriteDesktopLayout(id, null)) { e.Cancel = true; return; }
            desktopWindows.Remove(id);
            if (!tabs.Items.Contains(pane.Tab)) RemoveTab(pane);
        };
        window.Show();
        window.Persist();
    }

    internal bool WriteDesktopLayout(long id, string? layout)
    {
        try { store.SetDesktopPin(id, layout); return true; }
        catch (SqliteException error) { Notify(error.Message); return false; }
    }

    internal sealed class DesktopNoteWindow : Window
    {
        private sealed record Layout(double X, double Y, double W, double H, double Opacity);
        private readonly Center center;
        private readonly EditorPane pane;
        private readonly TextEditor text;
        private readonly DockPanel root = new();
        private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };

        internal DesktopNoteWindow(Center center, EditorPane pane, string? saved)
        {
            this.center = center;
            this.pane = pane;
            Title = "İğneli not";
            Width = 340; Height = 240; MinWidth = 230; MinHeight = 130; Topmost = true;
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0)
            });
            if (saved != null)
            {
                try
                {
                    if (JsonSerializer.Deserialize<Layout>(saved) is { } layout)
                    {
                        var area = SystemParameters.WorkArea;
                        Width = Math.Clamp(layout.W, MinWidth, area.Width);
                        Height = Math.Clamp(layout.H, MinHeight, area.Height);
                        Opacity = Math.Clamp(layout.Opacity, .6, 1);
                        var visible = layout.X >= SystemParameters.VirtualScreenLeft && layout.Y >= SystemParameters.VirtualScreenTop &&
                            layout.X + 100 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                            layout.Y + 36 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Left = visible ? layout.X : area.Left + 24;
                        Top = visible ? layout.Y : area.Top + 24;
                    }
                }
                catch (JsonException) { }
            }
            var bar = new DockPanel { Margin = new Thickness(6) };
            var close = Button("×", Close);
            close.ToolTip = "Masaüstü iğnesini kaldır";
            Add(bar, close, Dock.Right);
            var opacity = new Slider { Minimum = .6, Maximum = 1, Value = Opacity, Width = 75, ToolTip = "Saydamlık" };
            opacity.ValueChanged += (_, _) => { Opacity = opacity.Value; Restart(timer); };
            Add(bar, opacity, Dock.Right);
            var heading = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            heading.SetBinding(TextBlock.TextProperty, new Binding(nameof(Note.Title)) { Source = pane.Note });
            heading.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            bar.Children.Add(heading);
            Add(root, bar, Dock.Top);
            text = new TextEditor { Document = pane.SharedDocument, WordWrap = center.EditorWrap, Margin = new Thickness(6) };
            root.Children.Add(text);
            Content = root;
            ApplyPalette();
            LocationChanged += (_, _) => Restart(timer);
            SizeChanged += (_, _) => Restart(timer);
            Deactivated += (_, _) => center.Flush();
            timer.Tick += (_, _) => { timer.Stop(); Persist(); };
            Closed += (_, _) => { timer.Stop(); text.Document = new ICSharpCode.AvalonEdit.Document.TextDocument(); };
            Theme.Configure(this);
        }

        internal void ApplyPalette()
        {
            root.Background = NoteBrush(pane.Note.Color);
            text.Background = root.Background;
            text.Foreground = Theme.Brush("Ink");
            text.FontFamily = new System.Windows.Media.FontFamily(center.EditorFont);
            text.FontSize = center.EditorSize;
        }

        internal void Persist() => center.WriteDesktopLayout(pane.Note.Id,
            JsonSerializer.Serialize(new Layout(Left, Top, ActualWidth, ActualHeight, Opacity)));
    }
}
