using System.Windows.Controls;
using System.Windows.Input;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private sealed record PaletteCommand(string Title, string Aliases, Action Run)
    {
        internal bool Matches(string query)
        {
            var searchable = NoteStore.Fold(Title + " " + Aliases);
            return NoteStore.SearchTerms(query).All(term => searchable.Contains(term, StringComparison.Ordinal));
        }
    }

    private IEnumerable<PaletteCommand> PaletteCommands()
    {
        yield return new("Yeni not", "oluştur yaz boş", () => NewNote());
        yield return new("Panodan not", "yapıştır kopya ekran görüntüsü", FromClipboard);
        yield return new("Ayarlar", "tercihler tema yazı tipi seçenekler", OpenSettings);
        yield return new("Yedek ve aktarım", "yedekle geri yükle içe aktar dosya al", () => OpenSettingsTab(2));
        yield return new("Özellikler", "modül", () => OpenSettingsTab(7));
        if (Features.Enabled(Feature.Authenticator))
            yield return new("Doğrulayıcı", "otp totp kod", OpenAuthenticator);
        yield return new("Kısayollar", "tuş klavye global windows", () => OpenSettingsTab(1));
        yield return new("Hakkında", "sürüm derleme tekin teknoloji", OpenAbout);
        yield return new("Destek ol", "bağış sponsor github", OpenSupport);
        yield return new("Yeni grup", "klasör firma oluştur", () => EditGroup(null));
        yield return new("Notları göster", "tümü liste aktif", () => SelectGroup(null, 0));
        yield return new("Arşivi aç", "arşivlenmiş eski notlar", () => SelectGroup(null, 1));
        yield return new("Çöp kutusunu aç", "silinen notlar geri yükle", () => SelectGroup(null, 2));
        yield return new("Kart görünümü", "ızgara kutu", () => SetCardView(false));
        yield return new("Liste görünümü", "satır tablo", () => SetCardView(true));
        if (Features.Enabled(Feature.Board))
            yield return new("Pano", "sütun proje kanban", OpenBoard);
        if (Features.Enabled(Feature.BoardDeadlines))
            yield return new("Yaklaşanlar", "son tarih bugün hafta", () => ShowUpcoming("hafta"));
        yield return new("Renk filtresini temizle", "tüm renkler sıfırla", () => color.SelectedIndex = 0);
        for (var i = 1; i < ColorNames.Length; i++)
        {
            var index = i;
            yield return new($"Renge göre göster · {ColorNames[i]}", "filtrele süz", () => color.SelectedIndex = index);
        }
        if (store.Groups().FirstOrDefault(group => group.Id == SelectedGroup) is { } selected)
        {
            yield return new("Grubu yeniden adlandır", "grup firma isim değiştir", () => EditGroup(selected));
            yield return new("Grubu dışa aktar", "grup firma markdown resim", () => ExportGroup(selected));
            yield return new("Grubu sil", "grup firma kaldır", () => DeleteGroup(selected));
        }
        if (tabs.SelectedContent is not EditorPane pane) yield break;
        yield return new("Resim ekle", "görsel dosya fotoğraf içe aktar", pane.PickPictures);
        yield return new("Notu kaydet", "yaz kayıt diske", () => Flush());
        yield return new("Not sekmesini kapat", "sekme kapat", () => CloseTab(pane));
        if (Features.Enabled(Feature.DesktopPins)) yield return new("Masaüstüne iğnele", "yapışkan üstte sabitle", () => PinCard(pane.Note.Id));
        yield return new("Notu dışa aktar", "markdown metin txt md kaydet", () => ExportNote(pane.Note.Id));
        if (Features.Enabled(Feature.Checklist)) yield return new(pane.Note.Checklist ? "Düz metne çevir" : "Listeye çevir", "yapılacaklar kontrol listesi checklist", pane.ToggleChecklist);
        yield return new("Notta bul", "ara regex eşleşme", () => pane.HandleShortcut(Key.F, ModifierKeys.Control));
        yield return new("Notta değiştir", "bul değiştir regex", () => pane.HandleShortcut(Key.H, ModifierKeys.Control));
        yield return new("JSON veya XML biçimlendir", "girinti düzenle format", () => pane.HandleShortcut(Key.F, ModifierKeys.Control | ModifierKeys.Shift));
        yield return new("Kelime kaydırmayı değiştir", "satır sar kaydır", () => pane.HandleShortcut(Key.Z, ModifierKeys.Alt));
        yield return new("Notu sabitle", "üste al sırala", () => Change(pane.Note.Id, note => note.Pinned = !note.Pinned, "Sabitleme değiştirildi."));
        yield return new("Notun arşiv durumunu değiştir", "arşivle arşivden çıkar", () => Change(pane.Note.Id, note => note.Archived = !note.Archived, "Arşiv durumu değiştirildi."));
        yield return new("Notu çöp kutusuna taşı", "sil kaldır", () => Change(pane.Note.Id, note => note.Deleted = NoteStore.Now(), "Not çöp kutusuna taşındı."));
        for (var i = 0; i < Colors.Length; i++)
        {
            var chosen = Colors[i];
            yield return new($"Not rengi · {ColorNames[i]}", "boya kart renk", () => SetNoteColor(pane.Note.Id, chosen));
        }
    }

    private void OpenSettingsTab(int index)
    {
        OpenSettings();
        if (settingsNavigation != null) settingsNavigation.SelectedIndex = index;
    }

    private void SetCardView(bool useList)
    {
        store.Set("cardView", useList ? "list" : "card");
        RefreshCards();
    }
}
