using SkiaSharp;
using ZXing;
using FormatException = System.FormatException;

namespace KaralamaDefteri.Mobile;

internal sealed partial class MobileAuthenticatorPage : ContentPage
{
    private readonly NoteStore store;
    private readonly MobileDriveSync drive;
    private readonly long? group;
    private readonly VerticalStackLayout accounts = new() { Spacing = 10 };
    private readonly Label status = MobileStyle.Label("");
    private readonly AuthenticatorMigration migration = new();
    private readonly IDispatcherTimer timer;
    private readonly List<(OtpAccount Account, Label Code)> codes = [];
    private bool importing;

    internal MobileAuthenticatorPage(NoteStore store, MobileDriveSync drive, long? group)
    {
        this.store = store;
        this.drive = drive;
        this.group = group;
        Title = "Doğrulayıcı";
        MobileStyle.Page(this);
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => UpdateCodes();
        var layout = new VerticalStackLayout { Padding = 16, Spacing = 12 };
        if (!store.IsEncrypted)
        {
            layout.Children.Add(MobileStyle.Label("Doğrulayıcı için önce şifrelemeyi açın."));
            layout.Children.Add(MobileStyle.Button("Şifrelemeyi aç", async () => await Navigation.PushAsync(new MobileSecurityPage(store)), true));
        }
        else
        {
            if (store.Setting("mobileAuthenticatorNotice") != "1")
            {
                layout.Children.Add(MobileStyle.Label("Şifre ipuçları ile 2FA kodlarını aynı yerde tutmak 2FA’nın korumasını azaltır. Kritik hesaplar için ayrı bir cihaz önerilir."));
                store.Set("mobileAuthenticatorNotice", "1");
            }
            var name = MobileStyle.Entry("Hesap adı (elle ekleme)");
            var input = MobileStyle.Entry("Kurulum anahtarı veya otpauth bağlantısı");
            input.IsPassword = true;
            layout.Children.Add(name);
            layout.Children.Add(input);
            layout.Children.Add(MobileStyle.Button("Hesap ekle", () =>
            {
                var value = input.Text;
                input.Text = "";
                if (!string.IsNullOrWhiteSpace(value)) Import(value, name.Text);
            }, true));
            layout.Children.Add(MobileStyle.Button("QR kodu tara", async () => await ScanQr()));
            layout.Children.Add(MobileStyle.Button("QR görselinden aktar", async () => await ImportPicture()));
            layout.Children.Add(MobileStyle.Button("Hesapları QR ile dışa aktar", async () => await ExportQr()));
            layout.Children.Add(status);
            layout.Children.Add(accounts);
        }
        MobileStyle.SetContent(this, new ScrollView { Content = layout });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!store.IsEncrypted) return;
        Refresh();
        drive.LocalChanged += Refresh;
        timer.Start();
    }

    protected override void OnDisappearing()
    {
        drive.LocalChanged -= Refresh;
        timer.Stop();
        accounts.Children.Clear();
        codes.Clear();
        base.OnDisappearing();
    }

    private void Refresh()
    {
        codes.Clear();
        accounts.Children.Clear();
        if (!new FeatureFlags(store).Enabled(Feature.Authenticator)) return;
        foreach (var account in store.AuthenticatorAccounts().Where(account => account.Counter is null &&
                     (group is null || (account.Group ?? 0) == group)))
        {
            var code = MobileStyle.Label("", 23, true);
            var info = string.IsNullOrEmpty(account.Issuer) ? account.Name : $"{account.Issuer} · {account.Name}";
            var remove = MobileStyle.Button("Kaldır", async () =>
            {
                if (await DisplayActionSheet(info, "Kapat", "Hesabı kaldır") != "Hesabı kaldır") return;
                store.RemoveAuthenticatorAccount(account.Id);
                drive.Changed();
                Refresh();
            });
            codes.Add((account, code));
            accounts.Children.Add(new VerticalStackLayout
            {
                Spacing = 5, Children = { MobileStyle.Label(info, 16, true), code,
                    MobileStyle.Button("Kodu kopyala", async () =>
                        await Clipboard.Default.SetTextAsync(Authenticator.Code(account, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))),
                    remove }
            });
        }
        if (codes.Count == 0) accounts.Children.Add(MobileStyle.Label("Henüz hesap yok."));
        UpdateCodes();
    }

    private void UpdateCodes()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var (account, code) in codes)
        {
            var remaining = account.Period - now % account.Period;
            var interval = remaining > 5 ? ((remaining + 4) / 5 * 5) : remaining;
            var text = $"{Authenticator.Code(account, now)} · {interval} sn";
            if (code.Text != text) code.Text = text;
        }
    }

    private async Task ImportPicture()
    {
        if (importing) return;
        importing = true;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                FileTypes = FilePickerFileType.Images, PickerTitle = "QR görseli seç"
            });
            if (file is null) return;
            await using var source = await file.OpenReadAsync();
            var picture = await MobileEntry.ReadPicture(source, file.FileName);
            var value = await Task.Run(() =>
            {
                using var original = SKBitmap.Decode(picture.Bytes);
                using var bitmap = original?.Copy(SKColorType.Bgra8888);
                if (bitmap is null) return null;
                var reader = new BarcodeReaderGeneric { AutoRotate = true };
                reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
                return reader.Decode(bitmap.Bytes, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.BGRA32)?.Text;
            });
            if (value is null) throw new FormatException();
            Import(value, null);
        }
        catch (PermissionException) { status.Text = "Görsele erişim izni verilmedi."; }
        catch (Exception) { status.Text = "QR okunamadı. QR’ın tamamını içeren bir görsel seç."; }
        finally { importing = false; }
    }

    private bool Import(string value, string? name)
    {
        if (!new FeatureFlags(store).Enabled(Feature.Authenticator)) return false;
        try
        {
            value = value.Trim();
            OtpAccount[]? imported = value.StartsWith("otpauth", StringComparison.OrdinalIgnoreCase)
                ? migration.Import(value)
                : [Authenticator.Validate(new(Guid.NewGuid(), "", name?.Trim() ?? "", value, "SHA1", 6, 30, null))];
            if (imported is null)
            {
                status.Text = "Aktarımın sonraki QR bölümünü okut veya görselini seç.";
                return false;
            }
            var supported = imported.Where(account => account.Counter is null)
                .Select(account => account with { Group = group is > 0 ? group : null }).ToArray();
            if (supported.Length == 0) throw new FormatException();
            store.SaveAuthenticatorAccounts(supported);
            drive.Changed();
            status.Text = $"{supported.Length} TOTP hesabı eklendi.";
            Refresh();
            return true;
        }
        catch (Exception error) when (error is FormatException or OverflowException or ArgumentException)
        {
            status.Text = "TOTP kurulumu okunamadı. Kurulum anahtarını veya bağlantıyı kontrol et.";
            return false;
        }
    }
}
