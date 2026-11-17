using System.Text.Json;

namespace KaralamaDefteri.Mobile;

internal sealed class AndroidUpdatePage : ContentPage
{
    private readonly Label status = MobileStyle.Label("");
    private readonly Button action;
    private UpdateManifest? update;
    private string? downloaded;
    private bool busy;

    internal AndroidUpdatePage()
    {
        Title = "Güncellemeler";
        MobileStyle.Page(this);
        action = MobileStyle.Button("Şimdi kontrol et", async () => await Run(), true);
        MobileStyle.SetContent(this, new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16, Spacing = 10,
                Children = { MobileStyle.Label($"Kurulu sürüm v{AppInfo.Current.VersionString}"), status, action }
            }
        });
    }

    private async Task Run()
    {
        if (busy) return;
        busy = true;
        action.IsEnabled = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            if (downloaded is not null && File.Exists(downloaded))
            {
                status.Text = AndroidUpdate.Install(downloaded) ? "Android kurulum ekranında güncellemeyi onayla." :
                    "Bu kaynaktan kuruluma izin ver, ardından Kur’a tekrar dokun.";
                return;
            }
            if (update is null)
            {
                status.Text = "Güncellemeler kontrol ediliyor…";
                update = await AndroidUpdate.Check(timeout.Token);
                status.Text = update is null ? "Yeni Android sürümü bulunamadı." : $"v{update.Version} hazır.\n{update.Notes}";
                action.Text = update is null ? "Şimdi kontrol et" : "İndir";
                return;
            }
            var ready = update;
            status.Text = "Güncelleme indiriliyor…";
            var progress = new Progress<long>(bytes =>
            {
                if (busy && downloaded is null) status.Text = $"Güncelleme indiriliyor… %{bytes * 100 / ready.Size}";
            });
            downloaded = await AndroidUpdate.Fetch(ready, timeout.Token, progress);
            status.Text = $"v{ready.Version} doğrulandı.";
            action.Text = "Kur";
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or JsonException
            or InvalidDataException or Java.Lang.Exception)
        {
            downloaded = null;
            update = null;
            action.Text = "Şimdi kontrol et";
            status.Text = error is InvalidDataException ? "Güncelleme doğrulanamadı. Tekrar kontrol et." :
                "Güncelleme tamamlanamadı. Bağlantıyı ve kurulum iznini kontrol et.";
        }
        finally { busy = false; action.IsEnabled = true; }
    }
}
