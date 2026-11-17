using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private const string ProductWebsite = "https://tekinteknoloji.com/karalama-defteri/";
    private const string PrivacyWebsite = ProductWebsite + "gizlilik/";

    private static TextBlock Brand(string label, string url = "https://tekinteknoloji.com")
    {
        var link = new Hyperlink(new Run(label));
        link.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        var text = new TextBlock { Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
        text.Inlines.Add(link);
        return text;
    }

    public void OpenAbout() => ShowAbout(false);

    private StackPanel AboutSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Karalama Defteri", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = BuildInfo.Summary, Margin = new Thickness(0, 4, 0, 10) });
        panel.Children.Add(Brand("Karalama Defteri web sitesi", ProductWebsite));
        panel.Children.Add(Brand("Gizlilik politikası", PrivacyWebsite));
        panel.Children.Add(Button("Hakkında ve destek", OpenAbout));
        return panel;
    }

    private void ShowAbout(bool focusSupport)
    {
        Reveal();
        var about = Owned("Hakkında", 485, 490);
        var layout = new StackPanel { Margin = new Thickness(24) };
        layout.Children.Add(new Image { Source = Theme.AboutIcon, Width = 42, Height = 42, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });
        layout.Children.Add(new TextBlock { Text = "Karalama Defteri", FontSize = 24, FontWeight = FontWeights.SemiBold });
        layout.Children.Add(new TextBlock { Text = BuildInfo.Summary });
        layout.Children.Add(new TextBlock { Text = "Geliştiren: Tekin Teknoloji", Margin = new Thickness(0, 12, 0, 0) });
        layout.Children.Add(Brand("Karalama Defteri web sitesi", ProductWebsite));
        layout.Children.Add(Brand("Gizlilik politikası", PrivacyWebsite));
        layout.Children.Add(new TextBlock { Text = "© 2026 Tekin Teknoloji" });
        var copy = Button("Sürümü kopyala");
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.Margin = new Thickness(0, 12, 0, 0);
        var feedback = new TextBlock { Margin = new Thickness(0, 5, 0, 0) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(BuildInfo.ClipboardText); feedback.Text = "Sürüm bilgisi kopyalandı."; }
            catch (System.Runtime.InteropServices.ExternalException) { feedback.Text = "Pano şu anda kullanılıyor."; }
        };
        layout.Children.Add(copy);
        layout.Children.Add(feedback);
        var support = SupportSection();
        layout.Children.Add(support);
        layout.Children.Add(Brand("Kurumsal BT desteği mi lazım? → tekinteknoloji.com"));
        about.Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (focusSupport) about.Loaded += (_, _) => support.BringIntoView();
        about.Show();
    }

    internal int CompressionThreshold => (int.TryParse(store.Setting("pictureThresholdMb"), out var mb) ? Math.Clamp(mb, 1, 100) : 3) * 1024 * 1024;

    private void AddPictureSettings(Panel layout)
    {
        var thresholds = new ComboBox { ItemsSource = Enumerable.Range(1, 100), SelectedItem = CompressionThreshold / (1024 * 1024) };
        SettingsField(layout, "Resim sıkıştırma eşiği (MB)", thresholds, 80);
        thresholds.SelectionChanged += (_, _) => store.Set("pictureThresholdMb", thresholds.SelectedItem.ToString()!);
        SettingsAction(layout, Button("Veritabanını sıkıştır", () =>
        {
            if (!Flush(false)) return;
            try { store.Vacuum(); Notify("Veritabanı sıkıştırıldı."); }
            catch (SqliteException error) { Notify($"Veritabanı sıkıştırılamadı · {error.Message}"); }
        }));
    }

    internal StoredPicture? GetPicture(long note, long id) => store.GetPicture(note, id);

    internal double? PictureWidth(long id) =>
        double.TryParse(store.Setting($"pictureWidth:{id}"), System.Globalization.CultureInfo.InvariantCulture, out var width) && width >= 24 && double.IsFinite(width)
            ? width : null;

    internal void SetPictureWidth(long id, double width) => Transfer(() =>
        store.Set($"pictureWidth:{id}", width.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    internal void RestorePicture(long id, string marker, int offset)
    {
        if (editors.TryGetValue(id, out var editor))
        {
            editor.InsertStoredMarkers(Math.Min(offset, editor.Note.Content.Length), marker);
            Flush();
            return;
        }
        if (store.Get(id) is not { } note) return;
        note.Content = note.Content.Insert(Math.Min(offset, note.Content.Length), marker);
        store.Atomic(() => { store.Save(note); store.SyncPictures(note); });
        RefreshCards();
    }

    internal void AddPictures(EditorPane pane, IReadOnlyList<Picture> pictures, int offset)
    {
        if (!Flush(false)) return;
        var original = pane.Note.Content;
        var previousTitle = pane.Note.Title;
        var inserted = "";
        try
        {
            store.Atomic(() =>
            {
                var markers = pictures.Select(image => ImageMarkers.Marker(store.InsertPicture(pane.Note.Id, image)));
                inserted = string.Join(Environment.NewLine, markers);
                if (offset > 0 && original[offset - 1] is not ('\r' or '\n')) inserted = Environment.NewLine + inserted;
                if (offset == original.Length || original[offset] is not ('\r' or '\n')) inserted += Environment.NewLine;
                pane.Note.Content = original.Insert(offset, inserted);
                if (!pane.Note.Manual) pane.Note.Title = NoteStore.AutoTitle(pane.Note.Content, pane.Note.Created);
                store.Save(pane.Note);
                store.SyncPictures(pane.Note);
            });
            pane.InsertStoredMarkers(offset, inserted);
            Flush(false);
            RefreshCards();
        }
        catch (SqliteException error)
        {
            pane.Note.Content = original;
            pane.Note.Title = previousTitle;
            Notify($"Resim kaydedilemedi · {error.Message}");
        }
    }

    private void CreatePictureNote(Picture picture)
    {
        Note? note = null;
        store.Atomic(() =>
        {
            note = store.Create("", NewNoteGroup, $"Ekran görüntüsü – {DateTime.Now:dd.MM.yyyy HH:mm}");
            note.Content = ImageMarkers.Marker(store.InsertPicture(note.Id, picture));
            store.Save(note);
            store.SyncPictures(note);
        });
        Reveal();
        Open(note!);
        RefreshCards();
    }

    internal void ShowPicture(long note, long first, long[] ids)
    {
        var index = Array.IndexOf(ids, first);
        if (index < 0) return;
        var window = Owned("Resim görüntüleyici", 950, 700);
        var root = new DockPanel { Margin = new Thickness(10) };
        var toolbar = new WrapPanel();
        Add(root, toolbar, Dock.Top);
        var image = new Image { Stretch = Stretch.Uniform };
        var scroll = new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll);
        window.Content = root;
        StoredPicture? picture = null;
        Button? restore = null;
        double zoom = 0;
        void Display()
        {
            picture = store.GetPicture(note, ids[index]);
            if (restore != null) restore.IsEnabled = store.HasOriginal(note, ids[index]);
            if (picture == null) return;
            image.Source = Pictures.Decode(picture.Bytes);
            window.Title = $"Resim görüntüleyici · {index + 1}/{ids.Length}";
            Resize();
        }
        void Resize()
        {
            if (image.Source is not System.Windows.Media.Imaging.BitmapSource bitmap) return;
            var fit = zoom == 0;
            image.Width = fit ? Math.Min(bitmap.PixelWidth, Math.Max(1, scroll.ActualWidth - 22)) : bitmap.PixelWidth * zoom;
            image.Height = fit ? Math.Min(bitmap.PixelHeight, Math.Max(1, scroll.ActualHeight - 22)) : bitmap.PixelHeight * zoom;
        }
        void Step(int direction)
        {
            index = (index + ids.Length + direction) % ids.Length;
            Display();
        }
        var more = Button("…");
        System.Windows.Automation.AutomationProperties.SetName(more, "Resim işlemleri");
        var operations = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        more.Click += (_, _) => operations.IsOpen = true;
        toolbar.Children.Add(more);
        if (ids.Length > 1)
        {
            toolbar.Children.Add(Button("‹", () => Step(-1)));
            toolbar.Children.Add(Button("›", () => Step(1)));
        }
        Menu(operations, "Bu resimdeki QR’ı doğrulayıcıya ekle", () =>
        {
            if (image.Source is System.Windows.Media.Imaging.BitmapSource bitmap)
                OpenAuthenticator(bitmap, Guid.TryParse(store.Get(note)?.Uuid, out var id) ? id : null);
        });
        var qrAction = (MenuItem)operations.Items[0];
        operations.Opened += (_, _) => qrAction.Visibility =
            Features.Enabled(Feature.Authenticator) ? Visibility.Visible : Visibility.Collapsed;
        Menu(operations, "Sığdır", () => { zoom = 0; Resize(); });
        Menu(operations, "Gerçek boyut", () => { zoom = 1; Resize(); });
        Menu(operations, "Panoya kopyala", () =>
        {
            if (picture != null) CopyPicture(picture);
        });
        Menu(operations, "Farklı kaydet…", () =>
        {
            if (picture != null) SavePicture(picture, window);
        });
        var edit = Button("Düzenle…", () =>
        {
            if (picture is not { } current) return;
            OpenPictureEditor(window, current, bytes => Transfer(() =>
            {
                store.ReplacePicture(note, current.Id, Pictures.FromClipboard(bytes, CompressionThreshold));
                PictureChanged();
            }));
        });
        edit.SetResourceReference(StyleProperty, "PrimaryButton");
        toolbar.Children.Insert(0, edit);
        restore = Button("Orijinali geri yükle", () => Transfer(() =>
        {
            store.RestoreOriginal(note, ids[index]);
            PictureChanged();
        }));
        toolbar.Children.Add(restore);
        void PictureChanged()
        {
            Display();
            if (editors.TryGetValue(note, out var editor)) editor.RefreshPictures();
            RefreshCards();
        }
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Left) Step(-1);
            else if (e.Key == Key.Right) Step(1);
            else if (e.Key == Key.Escape) window.Close();
            else return;
            e.Handled = true;
        };
        scroll.PreviewMouseWheel += (_, e) =>
        {
            zoom = Math.Clamp((zoom == 0 && image.Source is System.Windows.Media.Imaging.BitmapSource bitmap
                ? image.ActualWidth / bitmap.PixelWidth : zoom) * (e.Delta > 0 ? 1.2 : 1 / 1.2), .05, 8);
            Resize();
            e.Handled = true;
        };
        scroll.SizeChanged += (_, _) => { if (zoom == 0) Resize(); };
        Display();
        restore.IsEnabled = store.HasOriginal(note, ids[index]);
        window.Show();
    }

    internal void CopyPicture(StoredPicture picture)
    {
        try { Clipboard.SetImage(Pictures.Decode(picture.Bytes)); }
        catch (System.Runtime.InteropServices.ExternalException) { Notify("Pano şu anda kullanılıyor."); }
    }

    internal void SavePicture(StoredPicture picture, Window owner)
    {
        try { Pictures.Save(picture, owner); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Notify($"Resim yazılamadı · {error.Message}"); }
    }
}
