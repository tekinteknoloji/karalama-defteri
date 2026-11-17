using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private const string ProjectPage = "https://github.com/tekinteknoloji/karalama-defteri";
    private readonly TextBlock supportReminder = new()
    {
        Visibility = Visibility.Collapsed, Margin = new Thickness(6, 2, 6, 2),
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private string SupportCacheKey => store.Setting("updateChannel") == "beta" ? "supportBeta" : "supportStable";

    private void CacheSupport(SupportOptions support)
    {
        store.Set(SupportCacheKey, JsonSerializer.Serialize(support));
    }

    private SupportOptions SupportLinks()
    {
        SupportOptions? local = null;
        SupportOptions? cached = null;
        try
        {
            if (File.Exists(AppPaths.SupportConfiguration))
                local = JsonSerializer.Deserialize<SupportConfiguration>(File.ReadAllText(AppPaths.SupportConfiguration),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Support;
            if (store.Setting(SupportCacheKey) is { } json)
                cached = JsonSerializer.Deserialize<SupportOptions>(json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return new(cached?.Sponsors ?? local?.Sponsors, cached?.Payment ?? local?.Payment, cached?.Label ?? local?.Label);
    }

    private void RefreshSupportReminder()
    {
        if (store.Setting("supportReminderSeen") == "1") return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!long.TryParse(store.Setting("supportFirstUse"), out var first) || first <= 0)
        {
            store.Set("supportFirstUse", now.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }
        if (!SupportPolicy.Eligible(first, now, store.SupportNoteCount(), false)) return;
        store.Set("supportReminderSeen", "1");
        var open = new Hyperlink(new Run("Destek ol"));
        var dismiss = new Hyperlink(new Run("Bir daha gösterme"));
        open.Click += (_, _) => { supportReminder.Visibility = Visibility.Collapsed; OpenSupport(); };
        dismiss.Click += (_, _) => supportReminder.Visibility = Visibility.Collapsed;
        supportReminder.Inlines.Add(new Run("Karalama Defteri işine yarıyorsa geliştirmeye destek olabilirsin · "));
        supportReminder.Inlines.Add(open);
        supportReminder.Inlines.Add(new Run(" · "));
        supportReminder.Inlines.Add(dismiss);
        supportReminder.Visibility = Visibility.Visible;
    }

    private void OpenSupport() => ShowAbout(true);

    private FrameworkElement SupportSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "❤ Destek ol", FontWeight = FontWeights.SemiBold, FontSize = 17 });
        panel.Children.Add(new TextBlock
        {
            Text = "Karalama Defteri ücretsiz ve reklamsızdır. İşine yarıyorsa geliştirmeye destek olabilirsin. Bağış gönüllüdür ve tüm kullanıcılar aynı özelliklere erişir.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8)
        });
        var support = SupportLinks();
        if (SupportPolicy.Link(support.Sponsors, "github.com") is { } sponsor)
            panel.Children.Add(Button("GitHub Sponsors", () => OpenSupportLink(sponsor)));
        if (SupportPolicy.Link(support.Payment, UpdateMetadata("SupportPaymentHost")) is { } payment)
            panel.Children.Add(Button(support.Label is { Length: > 0 and <= 60 } && !string.IsNullOrWhiteSpace(support.Label)
                ? support.Label : "TL ile destek ol", () => OpenSupportLink(payment)));
        var actions = new WrapPanel();
        actions.Children.Add(Button("GitHub’da yıldız ver", () => OpenSupportLink(new Uri(ProjectPage))));
        actions.Children.Add(Button("Hata bildir / öneri yap", () => OpenSupportLink(new Uri(ProjectPage + "/issues"))));
        actions.Children.Add(Button("Paylaş", () =>
        {
            try { Clipboard.SetText(ProjectPage + "/releases/latest"); Notify("İndirme bağlantısı kopyalandı."); }
            catch (System.Runtime.InteropServices.ExternalException) { Notify("Pano şu anda kullanılıyor."); }
        }));
        panel.Children.Add(actions);
        return panel;
    }

    private void OpenSupportLink(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Notify("Bağlantı açılamadı."); }
    }
}
