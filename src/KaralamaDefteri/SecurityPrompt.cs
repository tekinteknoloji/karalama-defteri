using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KaralamaDefteri;

internal static class SecurityPrompt
{
    internal static string? Ask(Window? owner, string title, string label, bool confirm, bool allowRecovery = false,
        Action<Window>? setup = null, bool allowHello = false, string? description = null)
    {
        var window = new Window
        {
            Title = title, Width = 440, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = owner == null,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };
        if (owner != null) window.Owner = owner;
        Theme.Configure(window);
        var layout = new StackPanel { Margin = new Thickness(20) };
        var password = new PasswordBox { Padding = new Thickness(6), Margin = new Thickness(0, 5, 0, 10) };
        var repeated = new PasswordBox { Padding = new Thickness(6), Margin = new Thickness(0, 5, 0, 10) };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (description != null)
            layout.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        layout.Children.Add(new TextBlock { Text = label });
        layout.Children.Add(password);
        if (confirm)
        {
            layout.Children.Add(new TextBlock { Text = "Ana parolayı yeniden yazın" });
            layout.Children.Add(repeated);
        }
        layout.Children.Add(feedback);
        var accept = Center.Button(confirm ? "Devam et" : "Kilidi aç", () =>
        {
            if (password.Password.Length == 0 || confirm && password.Password != repeated.Password)
            {
                feedback.Text = confirm ? "İki alana da aynı, boş olmayan parolayı yazın." : "Ana parolayı girin.";
                return;
            }
            window.DialogResult = true;
        });
        accept.IsDefault = true;
        accept.SetResourceReference(Control.StyleProperty, "PrimaryButton");
        layout.Children.Add(accept);
        string? recovered = null;
        if (allowHello && WindowsHelloUnlock.Configured)
        {
            var hello = Center.Button("Windows Hello ile aç");
            hello.Click += async (_, _) =>
            {
                hello.IsEnabled = false;
                try
                {
                    var candidate = await WindowsHelloUnlock.Unlock(window);
                    if (!window.IsVisible) return;
                    NoteStore.VerifyPassword(AppPaths.Database, candidate);
                    recovered = candidate;
                    window.DialogResult = true;
                }
                catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or IOException or Microsoft.Data.Sqlite.SqliteException)
                {
                    recovered = null;
                    feedback.Text = "Windows Hello ile açılamadı. Ana parolanızı veya kurtarma anahtarınızı kullanın.";
                }
                finally { hello.IsEnabled = true; }
            };
            layout.Children.Add(hello);
        }
        if (allowRecovery && (File.Exists(AppPaths.Recovery) || File.Exists(AppPaths.PendingRecovery)))
            layout.Children.Add(Center.Button("24 kelimeyle aç…", () =>
            {
                var phrase = Ask(window, "Kurtarma anahtarı", "24 kelimeyi aralarında boşlukla girin", false);
                if (phrase == null) return;
                try
                {
                    foreach (var path in new[] { AppPaths.PendingRecovery, AppPaths.Recovery })
                    {
                        if (!File.Exists(path)) continue;
                        var payload = File.ReadAllBytes(path);
                        try
                        {
                            var candidate = RecoveryPhrase.Unprotect(path == AppPaths.PendingRecovery ? payload[49..] : payload, phrase);
                            if (!File.Exists(AppPaths.EncryptionTransition)) NoteStore.VerifyPassword(AppPaths.Database, candidate);
                            recovered = candidate;
                            break;
                        }
                        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException
                            or Microsoft.Data.Sqlite.SqliteException or ArgumentOutOfRangeException) { }
                    }
                    if (recovered == null) throw new System.Security.Cryptography.CryptographicException();
                    window.DialogResult = true;
                }
                catch (System.Security.Cryptography.CryptographicException) { feedback.Text = "Kurtarma anahtarı doğrulanamadı."; }
                catch (IOException) { feedback.Text = "Kurtarma dosyası okunamadı."; }
            }));
        window.Content = layout;
        setup?.Invoke(window);
        window.Loaded += (_, _) => Keyboard.Focus(password);
        return window.ShowDialog() == true ? recovered ?? password.Password : null;
    }

    internal static bool ShowRecovery(Window owner, string phrase)
    {
        var window = new Window
        {
            Owner = owner, Title = "24 kelimelik kurtarma anahtarı", Width = 530,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize
        };
        Theme.Configure(window);
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap,
            Text = "Kelimeleri sırasıyla güvenli bir yerde saklayın. Ana parola ve kurtarma anahtarı kaybolursa notlar açılamaz." });
        layout.Children.Add(new TextBox { Text = phrase, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
        layout.Children.Add(Center.Button("Kelimeleri sakladım", () => window.DialogResult = true));
        window.Content = layout;
        return window.ShowDialog() == true;
    }
}
