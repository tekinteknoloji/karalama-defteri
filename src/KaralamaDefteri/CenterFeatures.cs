using System.Windows;
using System.Windows.Controls;

namespace KaralamaDefteri;

public sealed partial class Center
{
    internal FeatureFlags Features { get; }

    private StackPanel FeatureSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var toggles = new Dictionary<Feature, CheckBox>();
        var archive = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        AddFeature(Feature.Authenticator, "Doğrulayıcı", "Tek kullanımlık giriş kodlarını yönetir. Tam yerel şifreleme gerekir.");
        AddFeature(Feature.DesktopPins, "Masaüstüne iğneleme", "Notları masaüstünde ayrı bir pencere olarak tutar.");
        AddFeature(Feature.Tags, "Etiketler", "Notları #etiket ile düzenler ve filtreler.");
        AddFeature(Feature.Checklist, "Kontrol listesi (Checklist)", "Notları işaretlenebilir yapılacaklar listesi olarak gösterir.");
        AddFeature(Feature.Board, "Pano (proje takibi)", "Notları sütunlarda düzenler ve işlerin ilerleyişini gösterir.");
        AddFeature(Feature.BoardDeadlines, "Son tarih ve hatırlatma", "Kartlara tarih ve Windows bildirimi ekler. Pano açık olmalıdır.");
        AddFeature(Feature.CloudSync, "Bulut senkronu", "Bağlı bulut hesabıyla notları eşitler ve yedekler.");
        var sync = new CheckBox { Content = "Özellik seçimlerimi diğer cihazlarla eşitle",
            IsChecked = Features.SyncSelections, Margin = new Thickness(0, 12, 0, 0) };
        sync.Click += (_, _) =>
        {
            Features.SyncSelections = sync.IsChecked == true;
            if (syncPassword != null && syncAfterSave != null) Restart(syncAfterSave);
            ScheduleCloudAfterEdit();
        };
        sync.SetResourceReference(StyleProperty, "SettingsSwitch");
        panel.Children.Add(sync);
        archive.Children.Add(new TextBlock { Text = "Bitti’deki kartları kaç gün sonra arşivle? (0 = kapalı)" });
        var days = new ComboBox { ItemsSource = Enumerable.Range(0, 366),
            SelectedIndex = int.TryParse(store.Setting("boardArchiveDays"), out var value) ? Math.Clamp(value, 0, 365) : 0 };
        days.SelectionChanged += (_, _) =>
        {
            store.Set("boardArchiveDays", days.SelectedIndex.ToString());
            ArchiveBoardCards();
        };
        archive.Children.Add(days);
        archive.Visibility = Features.Enabled(Feature.Board) ? Visibility.Visible : Visibility.Collapsed;
        panel.Children.Add(archive);
        return panel;

        void AddFeature(Feature feature, string title, string description)
        {
            var toggle = new CheckBox { Content = title, IsChecked = Features.Enabled(feature),
                IsEnabled = Features.Available(feature), Margin = new Thickness(0, 10, 0, 3) };
            toggle.Click += (_, _) =>
            {
                if (!Flush(false)) { toggle.IsChecked = Features.Enabled(feature); return; }
                Features.Set(feature, toggle.IsChecked == true);
                RefreshFeatures();
                foreach (var (key, item) in toggles)
                {
                    item.IsEnabled = Features.Available(key);
                    item.IsChecked = Features.Enabled(key);
                }
                archive.Visibility = Features.Enabled(Feature.Board) ? Visibility.Visible : Visibility.Collapsed;
                if (syncPassword != null && syncAfterSave != null) Restart(syncAfterSave);
                ScheduleCloudAfterEdit();
                if (feature == Feature.CloudSync && Features.Enabled(feature))
                    _ = RunScheduledCloudSync(afterEdit: false, startup: true);
            };
            toggles.Add(feature, toggle);
            toggle.SetResourceReference(StyleProperty, "SettingsSwitch");
            panel.Children.Add(toggle);
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        }
    }

    private void RefreshFeatures()
    {
        if (!Features.Enabled(Feature.Authenticator)) authenticatorWindow?.Close();
        if (!Features.Enabled(Feature.CloudSync)) SetCloudStatus("", "");
        if (Features.Enabled(Feature.DesktopPins))
        {
            foreach (var id in store.DesktopPins())
                if (!desktopWindows.ContainsKey(id)) PinCard(id);
        }
        else
        {
            foreach (var (id, window) in desktopWindows.ToArray())
            {
                desktopWindows.Remove(id);
                window.Close();
                if (editors.TryGetValue(id, out var pane) && !tabs.Items.Contains(pane.Tab)) RemoveTab(pane);
            }
        }
        foreach (var pane in editors.Values) pane.RefreshFeatureVisibility();
        ((App)Application.Current).RefreshFeatureHotkeys();
        RefreshCards();
    }

    private void RefreshSyncedFeatures(bool[] previous)
    {
        if (!Enum.GetValues<Feature>().Select(Features.Enabled).SequenceEqual(previous))
        {
            settings?.Close();
            RefreshFeatures();
        }
    }
}
