namespace KaralamaDefteri.Mobile;

internal sealed class MobileSecurityPage : ContentPage
{
    internal MobileSecurityPage(NoteStore store)
    {
        Title = "Güvenlik";
        MobileStyle.Page(this);
        var app = (MobileApp)Application.Current!;
        var status = MobileStyle.Label("");
        var biometric = MobileStyle.Button(app.BiometricEnabled ? "Biyometrik kilidi kapat" : "Biyometrik kilidi aç", async () =>
        {
            if (!app.BiometricEnabled && !(store.IsEncrypted ? BiometricPassword.Available() : AndroidUnlock.Available()))
            {
                status.Text = store.IsEncrypted ? "Şifreli notlar için Android 11 veya üzeri ve kayıtlı güçlü biyometri gerekiyor." :
                    "Biyometrik kilit için Android 11 veya üzeri ve cihaz ekran kilidi gerekiyor.";
                return;
            }
            if (!await app.SetBiometric(!app.BiometricEnabled))
            {
                status.Text = "Cihaz doğrulaması tamamlanmadı.";
                return;
            }
            await Navigation.PopAsync();
        });
        var layout = new VerticalStackLayout
        {
            Padding = 16, Spacing = 10,
            Children = { MobileStyle.Label(store.IsEncrypted ? "Yerel veritabanı şifreli." : "Yerel veritabanı şifresiz."), biometric }
        };
        if (store.IsEncrypted)
        {
            var current = MobileStyle.Entry("Mevcut ana parola");
            var password = MobileStyle.Entry("Yeni ana parola");
            var confirmation = MobileStyle.Entry("Yeni ana parolayı tekrar gir");
            current.IsPassword = password.IsPassword = confirmation.IsPassword = true;
            var change = MobileStyle.Button("Ana parolayı değiştir", primary: true);
            change.Clicked += async (_, _) =>
            {
                var oldPassword = current.Text ?? "";
                var newPassword = password.Text;
                if (!store.MatchesOpenPassword(oldPassword))
                {
                    status.Text = "Mevcut ana parola yanlış.";
                    return;
                }
                if (string.IsNullOrEmpty(newPassword) || newPassword != confirmation.Text)
                {
                    status.Text = "İki yeni parola alanına da aynı parolayı yaz.";
                    return;
                }
                current.Text = password.Text = confirmation.Text = "";
                change.IsEnabled = false;
                try { await app.ChangePassword(oldPassword, newPassword); }
                catch (Exception)
                {
                    if (app.Windows.FirstOrDefault()?.Page is NavigationPage root && root.CurrentPage is { } page)
                        await page.DisplayAlert("Ana parola", "Parola değiştirme tamamlanamadı. Eşitleme bitince tekrar dene.", "Tamam");
                }
                finally { change.IsEnabled = true; }
            };
            layout.Children.Add(current);
            layout.Children.Add(password);
            layout.Children.Add(confirmation);
            layout.Children.Add(change);
        }
        else
        {
            var password = MobileStyle.Entry("Ana parola");
            var confirmation = MobileStyle.Entry("Ana parolayı tekrar gir");
            password.IsPassword = confirmation.IsPassword = true;
            Button? encrypt = null;
            encrypt = MobileStyle.Button("Şifrelemeyi aç", async () =>
            {
                var entered = password.Text;
                if (string.IsNullOrEmpty(entered) || entered != confirmation.Text)
                {
                    status.Text = "İki alana da aynı parolayı yaz.";
                    return;
                }
                password.Text = confirmation.Text = "";
                encrypt!.IsEnabled = false;
                try { await app.EnableEncryption(entered); }
                catch (Exception)
                {
                    if (app.Windows.FirstOrDefault()?.Page is NavigationPage root && root.CurrentPage is { } page)
                        await page.DisplayAlert("Şifreleme", "Şifreleme tamamlanamadı. Eşitleme bittikten sonra tekrar dene.", "Tamam");
                }
                finally { encrypt!.IsEnabled = true; }
            }, true);
            layout.Children.Add(password);
            layout.Children.Add(confirmation);
            layout.Children.Add(encrypt);
        }
        layout.Children.Add(status);
        MobileStyle.SetContent(this, new ScrollView { Content = layout });
    }
}
