using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private StackPanel SecuritySettings(Window owner)
    {
        var panel = new StackPanel();
        var encryption = SettingsCard(panel, "Yerel şifreleme");
        var action = Button(store.IsEncrypted ? "Şifrelemeyi kapat…" : "Tüm notları şifrele…", () =>
        {
            if (cloudWriting || dailyBackupRunning) { Notify("Arka plan işlemi tamamlanınca tekrar deneyin."); return; }
            if (store.HasAuthenticatorAccounts())
            {
                Notify("Şifrelemeyi kapatmadan önce doğrulayıcı hesaplarını kaldırın.");
                return;
            }
            var password = SecurityPrompt.Ask(owner, "Veritabanı şifreleme", "Ana parola", !store.IsEncrypted);
            if (password == null || cloudWriting || dailyBackupRunning || !Flush(false)) return;
            if (store.IsEncrypted && !store.MatchesOpenPassword(password))
            {
                Notify("Ana parola yanlış.");
                return;
            }
            var phrase = store.IsEncrypted ? null : RecoveryPhrase.Generate();
            if (phrase != null && !SecurityPrompt.ShowRecovery(owner, phrase)) return;
            RestoreInProgress = true;
            try
            {
                if (phrase != null)
                {
                    SaveSecurityFile(AppPaths.Recovery, RecoveryPhrase.Protect(password, phrase));
                    SaveSecurityFile(AppPaths.LockedShortcuts,
                        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(((App)Application.Current).EffectiveHotkeys));
                }
                SaveSecurityFile(AppPaths.EncryptionTransition, RecoveryPhrase.TransitionMarker(!store.IsEncrypted, password));
                store.ApplyDatabasePassword(store.IsEncrypted ? null : password, AppPaths.Backups);
                File.Delete(AppPaths.EncryptionTransition);
                if (store.IsEncrypted)
                {
                    File.Delete(AppPaths.Recovery);
                    File.Delete(AppPaths.HelloEnvelope);
                }
                ((App)Application.Current).ReleaseStoreForRestart();
                RestartApplication();
            }
            catch (Exception error) when (error is IOException or SqliteException or UnauthorizedAccessException)
            {
                MessageBox.Show(owner, "Şifreleme dönüşümü tamamlanamadı. Uygulama yeniden açılacak.\n" + error.Message,
                    "Karalama Defteri", MessageBoxButton.OK, MessageBoxImage.Warning);
                ((App)Application.Current).ReleaseStoreForRestart();
                RestartApplication();
            }
        });
        encryption.Children.Add(new TextBlock { Text = store.IsEncrypted ? "Notlar şifreli" : "Notlar şifresiz" });
        if (store.IsEncrypted) action.SetResourceReference(ForegroundProperty, "WarningInkBrush");
        SettingsAction(encryption, action);
        if (store.IsEncrypted)
        {
            var change = Button("Ana parolayı değiştir…");
            change.Click += async (_, _) =>
            {
                if (RestoreInProgress || cloudWriting || dailyBackupRunning) return;
                var current = SecurityPrompt.Ask(owner, "Ana parolayı değiştir", "Mevcut ana parola", false);
                if (current == null) return;
                if (!store.MatchesOpenPassword(current)) { Notify("Mevcut ana parola yanlış."); return; }
                var next = SecurityPrompt.Ask(owner, "Ana parolayı değiştir", "Yeni ana parola", true,
                    description: "Önceki yedekler eski parolayla açılır. Yeni kurtarma kelimeleri oluşturulacak.");
                if (next == null || next == current) return;
                var phrase = RecoveryPhrase.Generate();
                if (!SecurityPrompt.ShowRecovery(owner, phrase) || cloudWriting || dailyBackupRunning || !Flush(false)) return;
                var helloConfigured = WindowsHelloUnlock.Configured;
                RestoreInProgress = true;
                var released = false;
                try
                {
                    SaveSession();
                    store.Backup(AppPaths.Backups);
                    SaveSecurityFile(AppPaths.PendingRecovery,
                        [.. RecoveryPhrase.TransitionMarker(true, next), .. RecoveryPhrase.Protect(next, phrase)]);
                    store.Checkpoint();
                    ((App)Application.Current).ReleaseStoreForRestart();
                    released = true;
                    NoteStore.ReplaceDatabaseFormat(AppPaths.Database, current, next);
                    if (helloConfigured)
                    {
                        try { await WindowsHelloUnlock.Enable(next); }
                        catch (Exception error) when (error is System.Security.Cryptography.CryptographicException
                            or IOException or UnauthorizedAccessException)
                        {
                            File.Delete(AppPaths.HelloEnvelope);
                            MessageBox.Show(owner, "Parola değişti. Windows Hello’yu Güvenlik ayarlarından yeniden etkinleştirebilirsiniz.");
                        }
                    }
                }
                catch (Exception error) when (error is IOException or SqliteException or UnauthorizedAccessException)
                {
                    MessageBox.Show(owner, "Ana parola işlemi sırasında hata oluştu.\n" + error.Message);
                }
                finally
                {
                    if (released) RestartApplication();
                    else RestoreInProgress = false;
                }
            };
            SettingsAction(encryption, change);
            var hello = Button(WindowsHelloUnlock.Configured ? "Windows Hello bağlantısını kaldır" : "Windows Hello’yu etkinleştir…");
            var helloStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
            hello.IsEnabled = WindowsHelloUnlock.Configured;
            async Task CheckHelloSupport()
            {
                var supported = await WindowsHelloUnlock.IsSupported();
                hello.IsEnabled = supported || WindowsHelloUnlock.Configured;
                if (!hello.IsEnabled) helloStatus.Text = "Bu bilgisayarda Windows Hello kurulu değil.";
            }
            _ = CheckHelloSupport();
            hello.Click += async (_, _) =>
            {
                hello.IsEnabled = false;
                try
                {
                    if (WindowsHelloUnlock.Configured) await WindowsHelloUnlock.Disable();
                    else
                    {
                        var password = SecurityPrompt.Ask(owner, "Windows Hello", "Ana parola", false);
                        if (password == null) return;
                        if (!store.MatchesOpenPassword(password))
                        {
                            helloStatus.Text = "Ana parola yanlış.";
                            return;
                        }
                        await WindowsHelloUnlock.Enable(password);
                    }
                    hello.Content = WindowsHelloUnlock.Configured ? "Windows Hello bağlantısını kaldır" : "Windows Hello’yu etkinleştir…";
                    helloStatus.Text = "";
                }
                catch (SqliteException) { helloStatus.Text = "Ana parola doğrulanamadı."; }
                catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.TraceWarning("Windows Hello: {0:X8} {1}", error.HResult, error.Message);
                    helloStatus.Text = "Windows Hello şu an etkinleştirilemedi. Parolayla devam edebilirsiniz.";

                }
                finally { await CheckHelloSupport(); }
            };
            var locking = SettingsCard(panel, "Kilit");
            SettingsAction(locking, Button("Şimdi kilitle", () => { if (Flush(false)) RestartApplication(); }));
            var helloCard = SettingsCard(panel, "Windows Hello");
            SettingsAction(helloCard, hello);
            helloCard.Children.Add(helloStatus);
            var idle = new ComboBox { ItemsSource = new[] { 0, 5, 15, 30, 60 },
                SelectedItem = int.TryParse(store.Setting("lockMinutes"), out var minutes) ? minutes : 0 };
            SettingsField(locking, "Otomatik kilit (dakika, 0 kapalı)", idle, 80);
            idle.SelectionChanged += (_, _) => store.Set("lockMinutes", idle.SelectedItem!.ToString()!);
        }
        return panel;
    }

    internal static void SaveSecurityFile(string path, byte[] contents)
    {
        var temporary = path + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            output.Write(contents);
            output.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    internal void StartAutoLock()
    {
        if (!store.IsEncrypted) return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            if (exiting || RestoreInProgress || !int.TryParse(store.Setting("lockMinutes"), out var minutes) || minutes <= 0)
                return;
            var info = new LastInput { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LastInput>() };
            if (GetLastInputInfo(ref info) && unchecked((uint)Environment.TickCount - info.Time) >= minutes * 60000L && Flush(false))
                RestartApplication();
        };
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LastInput
    {
        internal uint Size;
        internal uint Time;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInput info);
}
