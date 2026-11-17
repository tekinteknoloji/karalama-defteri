using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace KaralamaDefteri;

public partial class App : Application
{
    static App() => AppContext.SetSwitch("Switch.System.Windows.Controls.Text.UseAdornerForTextboxSelectionRendering", false);

    private EventWaitHandle? activation;
    private EventWaitHandle? activationReply;
    private RegisteredWaitHandle? listener;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private NoteStore? store;
    private Center? center;
    private HwndSource? source;
    private readonly List<int> hotkeys = [];
    private Mutex? instance;
    private int pendingHotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (UpdateStartup.Handle(e.Args)) { Shutdown(); return; }
        if (e.Args is ["--restore-restart", var previous] && int.TryParse(previous, out var previousId))
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(previousId);
                if (!process.WaitForExit(10000)) { Shutdown(); return; }
            }
            catch (ArgumentException) { }
        }
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\KaralamaDefteri.Activate.{AppPaths.InstanceId}");
        activationReply = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\KaralamaDefteri.Activated.{AppPaths.InstanceId}");
        instance = new Mutex(false, $@"Local\KaralamaDefteri.{AppPaths.InstanceId}", out var first);
        if (!first)
        {
            RequestActivation();
            Shutdown();
            return;
        }
        try
        {
            if (UpdateStartup.RecoverInterrupted(e.Args)) { Shutdown(); return; }
            Directory.CreateDirectory(AppPaths.Data);
            if (!ResumeEncryptionTransition()) { Shutdown(); return; }
            string? password = null;
            if (File.Exists(AppPaths.Database) && !NoteStore.IsPlainDatabase(AppPaths.Database))
            {
                Theme.Apply(Theme.Resolve(null));
                while (true)
                {
                    password = SecurityPrompt.Ask(null, "Notların kilidini aç", "Ana parola", false, allowRecovery: true,
                        setup: RegisterLockedShortcuts, allowHello: true);
                    if (password == null) { Shutdown(); return; }
                    try { NoteStore.VerifyPassword(AppPaths.Database, password); break; }
                    catch (Microsoft.Data.Sqlite.SqliteException error) when (error.SqliteErrorCode == 26) { }
                }
            }
            if (password != null && File.Exists(AppPaths.PendingRecovery))
            {
                var pending = File.ReadAllBytes(AppPaths.PendingRecovery);
                if (pending.Length > 49 && RecoveryPhrase.VerifyTransition(pending[..49], password))
                    Center.SaveSecurityFile(AppPaths.Recovery, pending[49..]);
                File.Delete(AppPaths.PendingRecovery);
            }
            store = new NoteStore(AppPaths.Database, password);
            store.VerifyWritable();
            if (int.TryParse(store.Setting("autoArchiveDays"), out var days) && days is > 0 and <= 3650)
                store.ArchiveInactive(days);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            store?.Dispose();
            store = null;
            Theme.Apply(Theme.Resolve(null));
            var warning = new Window
            {
                Title = "Karalama Defteri", Width = 670, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "Bu klasöre yazılamıyor, notlar kaydedilemez", TextWrapping = TextWrapping.Wrap,
                    Background = Theme.Brush("Warning"), Foreground = Theme.Brush("WarningInk"), Padding = new Thickness(16),
                    VerticalAlignment = VerticalAlignment.Top
                }
            };
            Theme.Configure(warning);
            warning.Closed += (_, _) => Shutdown();
            warning.Show();
            ListenForActivation(warning);
            return;
        }
        center = new Center(store);
        MainWindow = center;
        var handle = new WindowInteropHelper(center).EnsureHandle();
        source = HwndSource.FromHwnd(handle);
        source.AddHook(WindowMessage);
        LoadHotkeys();
        RegisterConfiguredHotkeys();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Yeni not", null, (_, _) => center.NewNote());
        menu.Items.Add("Panodan not", null, (_, _) => center.FromClipboard());
        menu.Items.Add("Ara", null, (_, _) => center.OpenPalette());
        menu.Items.Add("Kontrol merkezi", null, (_, _) => center.Reveal());
        menu.Items.Add("Ayarlar", null, (_, _) => center.OpenSettings());
        menu.Items.Add("Hakkında", null, (_, _) => center.OpenAbout());
        menu.Items.Add("Çıkış", null, (_, _) => ExitApp());
        trayIcon = Theme.TrayIcon();
        tray = new Forms.NotifyIcon
        {
            Text = $"Karalama Defteri v{BuildInfo.Version}", Icon = trayIcon, ContextMenuStrip = menu, Visible = true
        };
        tray.BalloonTipClicked += (_, _) => center.Reveal();
        tray.DoubleClick += (_, _) => center.Reveal();
        var startupWarning = store.Setting("startup") == "1" ? SetStartup(true) : "";
        if (startupWarning.Length > 0) center.Notify(startupWarning);
        center.Show();
        if (File.Exists(UpdateFiles.Previous(AppPaths.Executable)))
            File.WriteAllText(UpdateStartup.Healthy, "");
        if (File.Exists(UpdateStartup.Recovered))
        {
            File.Delete(UpdateStartup.Recovered);
            center.Notify("Güncelleme açılamadı. Önceki sürüm geri yüklendi.");
        }
        center.StartUpdateCheck();
        _ = center.CheckAuthenticatorClock();
        center.RestoreDesktopPins();
        center.StartBoardArchive();
        center.StartDeadlineReminders();
        center.ScheduleDailyBackup();
        center.StartAutoLock();
        center.StartFolderSync();
        center.StartCloudSync();
        center.StartUiStallMonitor();
        ListenForActivation(center);
        if (pendingHotkey != 0) InvokeHotkey(pendingHotkey);
    }

    internal bool ShowDeadlineBalloon(string title, DateTimeOffset deadline)
    {
        if (tray?.Visible != true) return false;
        tray.ShowBalloonTip(7000, "Karalama Defteri · Son tarih",
            $"{title} · {deadline:dd.MM.yyyy HH:mm}", Forms.ToolTipIcon.Info);
        return true;
    }

    private void RegisterLockedShortcuts(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var lockedSource = HwndSource.FromHwnd(handle);
        var bindings = LoadLockedShortcuts();
        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (binding.Enabled)
                RegisterHotKey(handle, index + 1, 0x4000u | (uint)binding.Modifiers,
                    (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(binding.Key));
        }
        lockedSource.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == 0x0312)
            {
                pendingHotkey = wParam.ToInt32();
                window.Activate();
                handled = true;
            }
            return IntPtr.Zero;
        });
        window.Closing += (_, _) =>
        {
            for (var id = 1; id <= bindings.Count; id++) UnregisterHotKey(handle, id);
        };
        ListenForActivation(window);
        window.Closed += (_, _) => { listener?.Unregister(null); listener = null; };
    }

    private static bool ResumeEncryptionTransition()
    {
        if (!File.Exists(AppPaths.EncryptionTransition)) return true;
        Theme.Apply(Theme.Resolve(null));
        var marker = File.ReadAllBytes(AppPaths.EncryptionTransition);
        if (marker.Length != 49 || marker[0] > 1) throw new InvalidDataException("Şifreleme işlem kaydı okunamadı.");
        var enabling = marker[0] == 1;
        var plaintext = NoteStore.IsPlainDatabase(AppPaths.Database);
        if (enabling == plaintext)
        {
            var password = SecurityPrompt.Ask(null, "Şifreleme işlemine devam et", "İşlemi başlatırken kullanılan ana parola", false, allowRecovery: true);
            if (password == null) return false;
            if (!RecoveryPhrase.VerifyTransition(marker, password))
            {
                MessageBox.Show("Ana parola yanlış. Şifreleme işlemine devam etmek için uygulamayı yeniden açın.");
                return false;
            }
            if (!enabling) NoteStore.VerifyPassword(AppPaths.Database, password);
            NoteStore.ConvertBackups(AppPaths.Backups, enabling ? null : password, enabling ? password : null);
            NoteStore.ReplaceDatabaseFormat(AppPaths.Database, enabling ? null : password, enabling ? password : null);
        }
        File.Delete(AppPaths.EncryptionTransition);
        if (!enabling) File.Delete(AppPaths.Recovery);
        return true;
    }

    private void ListenForActivation(Window window)
    {
        listener = ThreadPool.RegisterWaitForSingleObject(activation!,
            (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                if (window is Center main) main.Reveal();
                else window.Activate();
                if (window.IsVisible && window.WindowState != WindowState.Minimized) activationReply?.Set();
            })), null, Timeout.Infinite, false);
    }

    private void RequestActivation()
    {
        using var gate = new Mutex(false, $@"Local\KaralamaDefteri.ActivationGate.{AppPaths.InstanceId}");
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired)
            {
                activationReply!.Reset();
                AllowSetForegroundWindow(-1);
                activation!.Set();
                if (activationReply.WaitOne(TimeSpan.FromSeconds(2))) return;
            }
        }
        finally { if (acquired) gate.ReleaseMutex(); }

        MessageBox.Show("Çalışan Karalama Defteri yanıt vermedi. Sistem tepsisinden Çıkış'ı deneyin. " +
            "Uygulama hâlâ yanıt vermiyorsa Görev Yöneticisi'nden bu klasörde çalışan Karalama Defteri'ni kapatıp yeniden açın.",
            "Karalama Defteri", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public string SetStartup(bool enabled)
    {
        try
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var run = enabled ? Registry.CurrentUser.CreateSubKey(runKey) : Registry.CurrentUser.OpenSubKey(runKey, writable: true);
            if (run == null) return "";
            if (enabled)
            {
                var command = $"\"{AppPaths.Executable}\"";
                if (!Equals(run.GetValue("KaralamaDefteri"), command)) run.SetValue("KaralamaDefteri", command);
            }
            else run.DeleteValue("KaralamaDefteri", false);
            return "";
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return $"Windows başlangıç ayarı uygulanamadı · {error.Message}";
        }
    }

    private void ExitApp()
    {
        if (center?.PrepareExit() == true) Shutdown();
    }

    internal bool BeginUpdate(string hash)
    {
        if (center?.PrepareExit() != true) return false;
        try
        {
            UpdateStartup.LaunchHelper("--apply-update", hash);
            Shutdown();
            return true;
        }
        catch { Shutdown(); throw; }
    }

    internal void ReleaseStoreForRestart()
    {
        store?.Dispose();
        store = null;
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && center?.RestoreInProgress != true)
        {
            handled = true;
            InvokeHotkey(wParam.ToInt32());
        }
        if (message == 0x0011) center?.PrepareExit();
        return IntPtr.Zero;
    }

    private void InvokeHotkey(int id)
    {
        switch (id)
        {
            case 1: center!.NewNote(); break;
            case 2: center!.FromClipboard(); break;
            case 3: center!.OpenPalette(); break;
            case 5: center!.OpenAuthenticator(); break;
            case 4: if (center!.IsVisible) center.Hide(); else center.Reveal(); break;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        listener?.Unregister(null);
        if (source is not null)
        {
            if (!source.IsDisposed)
            {
                foreach (var id in hotkeys) UnregisterHotKey(source.Handle, id);
                source.RemoveHook(WindowMessage);
            }
        }
        tray?.Dispose();
        trayIcon?.Dispose();
        activation?.Dispose();
        activationReply?.Dispose();
        if (store != null)
        {
            try { store.Checkpoint(); }
            catch (Microsoft.Data.Sqlite.SqliteException error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            finally { store.Dispose(); }
        }
        instance?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
