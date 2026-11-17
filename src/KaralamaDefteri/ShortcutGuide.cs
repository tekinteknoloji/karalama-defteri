using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

internal static class ShortcutGuide
{
    private static StackPanel BuildGlobalSettings(App app, FeatureFlags features)
    {
        var panel = new StackPanel();
        var labels = new[] { "Yeni not aç", "Panodan yeni not oluştur", "Hızlı aramayı aç", "Kontrol merkezini göster veya gizle", "Doğrulayıcıyı aç" };
        var rows = new List<(CheckBox Enabled, Button Keys, TextBlock Status)>();
        int? capturing = null;
        panel.Children.Add(new TextBlock { Text = "Windows genelinde", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) });
        for (var index = 0; index < labels.Length; index++)
        {
            var id = index;
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var keys = Center.Button("");
            keys.MinWidth = 112;
            var enabled = new CheckBox { Content = labels[id], VerticalAlignment = VerticalAlignment.Center };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            DockPanel.SetDock(keys, Dock.Right);
            row.Children.Add(keys);
            row.Children.Add(enabled);
            if (id != App.AuthenticatorHotkeyIndex || features.Enabled(Feature.Authenticator))
            {
                panel.Children.Add(row);
                panel.Children.Add(status);
            }
            rows.Add((enabled, keys, status));
            enabled.Click += (_, _) => Update(id, () => app.SetHotkey(id, app.HotkeyBindings[id] with { Enabled = enabled.IsChecked == true }));
            keys.Click += (_, _) =>
            {
                app.SuspendHotkeys();
                capturing = id;
                keys.Content = "Tuşlara basın…";
                status.Text = "İptal etmek için Esc.";
                status.Visibility = Visibility.Visible;
            };
            keys.LostKeyboardFocus += (_, _) =>
            {
                if (capturing != id) return;
                CancelCapture();
            };
            keys.PreviewKeyDown += (_, e) =>
            {
                if (capturing != id) return;
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                var modifiers = Keyboard.Modifiers;
                if (key == Key.Escape && modifiers == ModifierKeys.None) { CancelCapture(); return; }
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                if (!HotkeyBinding.CanRegister(modifiers, key))
                {
                    status.Text = "Ctrl, Alt veya Win ile bir tuşa ya da bir işlev tuşuna basın.";
                    return;
                }
                capturing = null;
                Update(id, () => app.SetHotkey(id, app.HotkeyBindings[id] with { Modifiers = modifiers, Key = key }));
            };
        }
        var reset = Center.Button("Varsayılana dön", () => Update(0, app.ResetHotkeys));
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 8, 0, 6);
        panel.Children.Add(reset);
        Refresh();
        return panel;

        void CancelCapture()
        {
            capturing = null;
            app.RegisterConfiguredHotkeys();
            Refresh();
        }

        void Update(int id, Action save)
        {
            try { save(); Refresh(); }
            catch (SqliteException)
            {
                app.RegisterConfiguredHotkeys();
                Refresh();
                rows[id].Status.Text = "Kısayol ayarları kaydedilemedi.";
                rows[id].Status.Visibility = Visibility.Visible;
            }
        }

        void Refresh()
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var binding = app.HotkeyBindings[i];
                var row = rows[i];
                row.Enabled.IsChecked = binding.Enabled;
                row.Keys.Content = binding.Display;
                row.Status.Text = binding.Enabled && !app.IsHotkeyRegistered(i)
                    ? "Kısayol kaydedilemedi. Başka bir işlem veya Windows bu tuşları kullanıyor olabilir." : "";
                row.Status.Visibility = row.Status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    public static ScrollViewer Create(FeatureFlags features)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };
        panel.Children.Add(BuildGlobalSettings((App)Application.Current, features));
        Add("Uygulama içinde",
            ("Ctrl+N", "Yeni not aç."),
            ("Ctrl+K", "Hızlı aramayı aç."),
            ("Ctrl+L", "Not arama kutusuna odaklan."),
            ("Enter", "Arama sonucunu aç veya grup adını onayla."),
            ("↑ / ↓", "Hızlı aramada sonuçlar veya grup önerileri arasında gez."),
            ("Tab / Enter", "Arama kutusunda açık olan grup önerisini seç."),
            ("Esc", "Arama paletini, grup önerilerini veya grup düzenlemeyi kapat."),
            ("Ctrl / Shift + tık", "Kart seçimini değiştir veya bir kart aralığını seç."));
        if (features.Enabled(Feature.Board))
            Add("Pano", ("Ctrl+Shift+← / →", "Odaklı kartı komşu sütuna taşı."),
                ("Ctrl+Shift+↑ / ↓", "Odaklı kartın sütun içindeki sırasını değiştir."));
        Add("Editör",
            ("Ctrl+S", "Bekleyen değişiklikleri hemen kaydet."),
            ("Ctrl+W", "Etkin sekmeyi kapat."),
            ("Ctrl+F / Ctrl+H", "Bul veya değiştir alanını aç."),
            ("Enter / Esc", "Bul alanında sonraki eşleşmeye geç veya paneli kapat."),
            ("Ctrl+Shift+F", "JSON veya XML’i biçimlendir."),
            ("Ctrl+V", "Metin, resim veya kopyalanmış resim dosyasını yapıştır."),
            ("Ctrl+Shift+V", "Panoda metin de varsa resmi yapıştır."),
            ("Ctrl+C / Ctrl+X", "Seçili metni kopyala veya kes."),
            ("Ctrl+A", "Not metninin tamamını seç."),
            ("Ctrl+Z / Ctrl+Y", "Düzenlemeyi geri al veya yinele."),
            ("Alt+Z", "Kelime kaydırmayı aç veya kapat."),
            ("Alt+sürükle", "Dikdörtgen metin seçimi yap."),
            ("Ctrl+fare tekerleği", "Editör yazısını büyüt veya küçült."),
            ("Delete / Backspace", "Seçili resmi veya imlecin yanındaki resim işaretini sil."));
        Add("Resim görüntüleyici",
            ("← / →", "Önceki veya sonraki resmi göster."),
            ("Fare tekerleği", "Resmi yakınlaştır veya uzaklaştır."),
            ("Esc", "Görüntüleyiciyi kapat."));
        if (features.Enabled(Feature.Authenticator))
            Add("Doğrulayıcı",
                ("Ctrl+N", "Yeni hesap ekle."),
                ("Ctrl+F", "Hesap aramasına odaklan."),
                ("Ctrl+T", "Seçili hesabın kodunu kopyala."),
                ("Ctrl+Shift+T", "Seçili hesabın kodunu göster."),
                ("Enter / Del", "Listede seçili hesabı düzenle veya kaldır."));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        void Add(string heading, params (string Keys, string Description)[] shortcuts)
        {
            panel.Children.Add(new TextBlock
            {
                Text = heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6)
            });
            foreach (var (keys, description) in shortcuts)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = keys, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
                var detail = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(detail, 1);
                row.Children.Add(detail);
                panel.Children.Add(row);
            }
        }
    }
}
