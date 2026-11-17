using SkiaSharp;
using ZXing;
using ZXing.QrCode;

namespace KaralamaDefteri.Mobile;

internal sealed partial class MobileAuthenticatorPage
{
    private async Task ScanQr()
    {
        if (importing || Platform.CurrentActivity is not { } activity) return;
        importing = true;
        try
        {
            if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            {
                status.Text = "Kamera izni verilmedi. QR görselinden aktarabilirsin.";
                return;
            }
            using var scanner = new LiveAuthenticatorScanner(activity, value => Import(value, null));
            await scanner.Run();
        }
        catch (Exception error) when (error is PermissionException or Java.Lang.Exception or InvalidOperationException)
        {
            status.Text = "Tarayıcı açılamadı. QR görselinden aktarabilirsin.";
        }
        finally { importing = false; }
    }

    private async Task ExportQr()
    {
        if (!new FeatureFlags(store).Enabled(Feature.Authenticator)) return;
        var available = store.AuthenticatorAccounts().Where(account => account.Counter is null && account.Period == 30 &&
            (group is null || (account.Group ?? 0) == group)).ToArray();
        if (available.Length == 0) { status.Text = "Aktarılabilecek 30 saniyelik TOTP hesabı yok."; return; }
        var page = new ContentPage { Title = "Hesapları aktar" };
        MobileStyle.Page(page);
        var layout = new VerticalStackLayout { Padding = 12, Spacing = 8 };
        var password = MobileStyle.Entry("Ana parola");
        password.IsPassword = true;
        layout.Children.Add(password);
        var selected = new HashSet<Guid>();
        foreach (var account in available)
        {
            var checkbox = new CheckBox();
            checkbox.CheckedChanged += (_, e) =>
            {
                if (e.Value) selected.Add(account.Id);
                else selected.Remove(account.Id);
            };
            var label = string.IsNullOrEmpty(account.Issuer) ? account.Name : $"{account.Issuer} · {account.Name}";
            SemanticProperties.SetDescription(checkbox, label);
            layout.Children.Add(new HorizontalStackLayout { Children = { checkbox, MobileStyle.Label(label) } });
        }
        var feedback = MobileStyle.Label("");
        layout.Children.Add(MobileStyle.Button("Seçilenleri QR olarak göster", () =>
        {
            if (selected.Count == 0) { feedback.Text = "Aktarılacak hesapları seç."; return; }
            var entered = password.Text ?? "";
            password.Text = "";
            if (!store.MatchesOpenPassword(entered)) { feedback.Text = "Ana parola yanlış."; return; }
            try
            {
                var batches = AuthenticatorMigration.Export(available.Where(account => selected.Contains(account.Id)).ToArray());
                var image = new Image { Aspect = Aspect.AspectFit, HeightRequest = 310, WidthRequest = 310 };
                var label = MobileStyle.Label("");
                var index = 0;
                var next = MobileStyle.Button("Sonraki QR");
                var previous = MobileStyle.Button("Önceki QR");
                void Show()
                {
                    var bits = new QRCodeWriter().encode(batches[index], BarcodeFormat.QR_CODE, 400, 400);
                    using var bitmap = new SKBitmap(bits.Width, bits.Height);
                    for (var y = 0; y < bits.Height; y++)
                        for (var x = 0; x < bits.Width; x++)
                            bitmap.SetPixel(x, y, bits[x, y] ? SKColors.Black : SKColors.White);
                    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    var bytes = data.ToArray();
                    image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                    label.Text = $"QR {index + 1} / {batches.Length}";
                    previous.IsEnabled = index > 0;
                    next.IsEnabled = index + 1 < batches.Length;
                }
                next.Clicked += (_, _) => { index++; Show(); };
                previous.Clicked += (_, _) => { index--; Show(); };
                Show();
                layout.Children.Clear();
                layout.Children.Add(label);
                layout.Children.Add(image);
                if (batches.Length > 1)
                    layout.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { previous, next } });
                page.Disappearing += (_, _) => image.Source = null;
            }
            catch (Exception error) when (error is System.FormatException or ArgumentException or WriterException)
            {
                feedback.Text = "QR oluşturulamadı. Daha az hesap seçip tekrar dene.";
            }
        }, true));
        layout.Children.Add(feedback);
        MobileStyle.SetContent(page, new ScrollView { Content = layout });
        await Navigation.PushAsync(page);
    }
}
