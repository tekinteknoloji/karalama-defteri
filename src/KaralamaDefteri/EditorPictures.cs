using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace KaralamaDefteri;

public sealed partial class EditorPane
{
    internal void RefreshPictures()
    {
        bitmaps.Clear();
        bitmapOrder.Clear();
        editor.TextArea.TextView.Redraw();
    }
    private readonly ComboBox groupBox = new() { IsEditable = true, IsTextSearchEnabled = true, Width = 175, Margin = new Thickness(4) };
    private bool updatingGroup;
    private readonly Dictionary<long, BitmapSource> bitmaps = [];
    private readonly Queue<long> bitmapOrder = [];
    private long? selectedPicture;
    internal bool GroupInputFocused => groupBox.IsKeyboardFocusWithin;

    private void ConfigureGroupBox()
    {
        RefreshGroupBox();
        groupBox.DropDownOpened += (_, _) => RefreshGroupBox();
        groupBox.SelectionChanged += (_, _) =>
        {
            if (!updatingGroup && groupBox.SelectedItem is string name) center.AssignGroup(Note, name);
        };
        groupBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            center.AssignGroup(Note, groupBox.Text);
            groupBox.IsDropDownOpen = false;
            RefreshGroupBox();
            editor.Focus();
        };
    }

    internal void RefreshGroupBox()
    {
        if (updatingGroup) return;
        var groups = center.GetGroups();
        var name = groups.FirstOrDefault(group => group.Id == Note.GroupId)?.Name ?? "Grupsuz";
        updatingGroup = true;
        groupBox.ItemsSource = new[] { "Grupsuz" }.Concat(groups.Select(group => group.Name)).ToArray();
        groupBox.SelectedItem = name;
        groupBox.Text = name;
        updatingGroup = false;
    }

    private void ConfigurePictures()
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 0, 3) };
        toolbar.Children.Add(Center.Button("Resim ekle", PickPictures));
        SetDock(toolbar, Dock.Top);
        Children.Insert(Children.Count - 1, toolbar);
        editor.TextArea.TextView.ElementGenerators.Add(new PictureGenerator(InlinePicture));
        editor.SizeChanged += (_, _) => editor.TextArea.TextView.Redraw();
        editor.AllowDrop = true;
        editor.PreviewDragOver += (_, e) =>
        {
            if (!IsPictureDrop(e.Data)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        editor.PreviewDrop += (_, e) =>
        {
            if (!IsPictureDrop(e.Data)) return;
            var offset = editor.GetPositionFromPoint(e.GetPosition(editor)) is { } position ? editor.Document.GetOffset(position.Location) : Offset;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) LoadFiles(files, offset);
            else if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource source) PastePicture(source, offset);
            e.Handled = true;
        };
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.V && Keyboard.Modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
            {
                var force = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                try
                {
                    if (force || !Clipboard.ContainsText())
                    {
                        if (ClipboardPictures.Read() is { } image)
                        {
                            PastePicture(image, Offset);
                            e.Handled = true;
                        }
                        else if (Clipboard.ContainsFileDropList())
                        {
                            var files = Clipboard.GetFileDropList().Cast<string>().Where(Pictures.Supports).ToArray();
                            if (files.Length > 0)
                            {
                                LoadFiles(files, Offset);
                                e.Handled = true;
                            }
                        }
                    }
                }
                catch (System.Runtime.InteropServices.ExternalException) { center.Notify("Pano şu anda kullanılıyor."); e.Handled = true; }
            }
            if (e.Key is Key.Delete or Key.Back && Keyboard.Modifiers == ModifierKeys.None)
            {
                var id = selectedPicture ?? MarkerAtCaret(e.Key == Key.Back);
                if (id.HasValue) { DeletePicture(id.Value); e.Handled = true; }
            }
            else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) selectedPicture = null;
        };
        editor.PreviewMouseLeftButtonDown += (_, _) => selectedPicture = null;
    }

    private static bool IsPictureDrop(IDataObject data) => data.GetData(DataFormats.FileDrop) is string[] files
        ? files.Any(Pictures.Supports) : data.GetDataPresent(DataFormats.Bitmap);

    internal void PickPictures()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Resim ekle", Multiselect = true,
            Filter = "Resimler|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp"
        };
        if (dialog.ShowDialog(center) == true) LoadFiles(dialog.FileNames, Offset);
    }

    private void LoadFiles(string[] files, int offset)
    {
        var threshold = center.CompressionThreshold;
        var pictures = new List<Picture>();
        foreach (var file in files.Where(Pictures.Supports))
        {
            try { pictures.Add(Pictures.FromFile(file, threshold)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            { center.Notify($"Resim açılamadı · {error.Message}"); }
        }
        if (pictures.Count > 0) center.AddPictures(this, pictures, offset);
    }

    private void PastePicture(BitmapSource source, int offset)
    {
        try { center.AddPictures(this, [Pictures.FromClipboard(source, center.CompressionThreshold)], offset); }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException)
        { center.Notify($"Resim eklenemedi · {error.Message}"); }
    }

    internal void InsertStoredMarkers(int offset, string markers)
    {
        editor.Document.Insert(offset, markers);
        editor.TextArea.TextView.Redraw();
        editor.CaretOffset = offset + markers.Length;
        editor.Focus();
    }

    private FrameworkElement? InlinePicture(long id)
    {
        if (!bitmaps.TryGetValue(id, out var bitmap))
        {
            var picture = center.GetPicture(Note.Id, id);
            if (picture == null) return null;
            try { bitmap = Pictures.Decode(picture.Bytes); }
            catch (Exception error) when (error is IOException or NotSupportedException) { return null; }
            if (bitmapOrder.Count >= 16) bitmaps.Remove(bitmapOrder.Dequeue());
            bitmaps[id] = bitmap;
            bitmapOrder.Enqueue(id);
        }
        var available = editor.TextArea.TextView.ActualWidth;
        var maximum = Math.Max(1, Math.Min(bitmap.PixelWidth, available > 1 ? available - 1 : Math.Max(1, editor.ActualWidth - 45)));
        var width = Math.Min(center.PictureWidth(id) ?? maximum, maximum);
        var image = new Image
        {
            Source = bitmap, Width = width, Height = width * bitmap.PixelHeight / bitmap.PixelWidth,
            Stretch = Stretch.Uniform, Cursor = Cursors.Hand, ToolTip = "Çift tıkla · Resmi aç"
        };
        image.MouseLeftButtonDown += (_, e) =>
        {
            editor.Focus();
            selectedPicture = id;
            if (e.ClickCount == 2) center.ShowPicture(Note.Id, id, PictureIds());
            e.Handled = true;
        };
        var menu = new ContextMenu();
        void Item(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Görüntüle", () => center.ShowPicture(Note.Id, id, PictureIds()));
        Item("Kopyala", () => { if (center.GetPicture(Note.Id, id) is { } picture) center.CopyPicture(picture); });
        Item("Farklı kaydet…", () => { if (center.GetPicture(Note.Id, id) is { } picture) center.SavePicture(picture, center); });
        Item("Sil", () => DeletePicture(id));
        image.ContextMenu = menu;
        var container = new Grid();
        container.Children.Add(image);
        var grip = new System.Windows.Controls.Primitives.Thumb
        {
            Width = 13, Height = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE, ToolTip = "Resmi boyutlandır", Background = Theme.Brush("Accent")
        };
        grip.DragDelta += (_, e) =>
        {
            image.Width = Math.Clamp(image.Width + e.HorizontalChange, Math.Min(24, maximum), maximum);
            image.Height = image.Width * bitmap.PixelHeight / bitmap.PixelWidth;
        };
        grip.DragCompleted += (_, _) => center.SetPictureWidth(id, image.Width);
        container.Children.Add(grip);
        return container;
    }

    private long[] PictureIds() => ImageMarkers.Pattern().Matches(editor.Text).Select(ImageMarkers.Id).Where(id => id > 0).Distinct().ToArray();

    private long? MarkerAtCaret(bool backspace)
    {
        var line = editor.Document.GetLineByOffset(Offset);
        var text = editor.Document.GetText(line.Offset, line.Length);
        var local = Offset - line.Offset;
        foreach (System.Text.RegularExpressions.Match match in ImageMarkers.Pattern().Matches(text))
        {
            if (backspace ? local > match.Index && local <= match.Index + match.Length
                : local >= match.Index && local < match.Index + match.Length)
                return ImageMarkers.Id(match);
        }
        return null;
    }

    private void DeletePicture(long id)
    {
        var marker = ImageMarkers.Marker(id);
        var text = editor.Text;
        var offset = text.IndexOf(marker, StringComparison.Ordinal);
        selectedPicture = null;
        if (offset < 0) return;
        editor.Document.Remove(offset, marker.Length);
        var anchor = editor.Document.CreateAnchor(offset);
        anchor.SurviveDeletion = true;
        center.Flush();
        center.Notify("Resim silindi.", () =>
        {
            if (closed)
            {
                center.RestorePicture(Note.Id, marker, offset);
                return;
            }
            editor.Document.Insert(anchor.Offset, marker);
            center.Flush();
        });
    }
}
