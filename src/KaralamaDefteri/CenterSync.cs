using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private string? syncFolder;
    private string? syncPassword;
    private TextBlock? syncIndicator;
    private TextBlock? syncSettingsStatus;
    private DispatcherTimer? syncAfterSave;

    internal void StartFolderSync()
    {
        var savedFolder = store.Setting("syncFolder");
        syncFolder = string.IsNullOrWhiteSpace(savedFolder) ? null : savedFolder;
        if (syncFolder != null) syncPassword = ReadFolderPassword();
        syncAfterSave = Timer(5000, RunFolderSync);
        var periodic = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) };
        periodic.Tick += (_, _) => RunFolderSync();
        periodic.Start();
        Closed += (_, _) => { periodic.Stop(); syncAfterSave.Stop(); };
        if (syncPassword != null) Dispatcher.BeginInvoke(RunFolderSync, DispatcherPriority.ApplicationIdle);
        else if (syncFolder != null) SetSyncStatus("Senkron parolası bekleniyor");
    }

    private void SetSyncStatus(string value)
    {
        if (syncIndicator != null) syncIndicator.Text = value;
        if (syncSettingsStatus != null) syncSettingsStatus.Text = value;
    }

    private void RunFolderSync()
    {
        if (exiting || RestoreInProgress || cloudWriting || syncFolder == null || syncPassword == null || !Flush(false)) return;
        syncAfterSave?.Stop();
        try
        {
            var previous = Enum.GetValues<Feature>().Select(Features.Enabled).ToArray();
            var conflicts = store.SyncFolder(syncFolder, syncPassword, editors.Values.Select(e => Guid.Parse(e.Note.Uuid)).ToHashSet());
            SaveFolderPassword(syncPassword);
            RefreshSyncedFeatures(previous);
            SetSyncStatus(conflicts == 0 ? "Senkron tamamlandı" : $"Senkron tamamlandı · {conflicts} çakışma kopyası");
            RefreshCards();
        }
        catch (System.Security.Cryptography.CryptographicException)
        { SetSyncStatus("Senkron parolası veya klasör verisi doğrulanamadı"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException)
        { SetSyncStatus("Senkron bekliyor · " + error.Message); }
    }

    private StackPanel FolderSyncSettings(Window owner)
    {
        var panel = new StackPanel();
        var status = syncSettingsStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            Text = syncIndicator?.Text ?? "" };
        panel.Unloaded += (_, _) =>
        {
            if (syncSettingsStatus == status) syncSettingsStatus = null;
        };
        SettingsAction(panel, Button("Klasörü seç ve eşitle…", () =>
        {
            var dialog = new OpenFolderDialog { Title = "Paylaşılan senkron klasörünü seçin" };
            if (dialog.ShowDialog(owner) != true) return;
            var password = (syncFolder == dialog.FolderName ? syncPassword : null) ??
                store.CloudDefaultPassword ?? AskFolderPassword(owner);
            if (password == null || !Flush(false)) return;
            syncFolder = dialog.FolderName;
            syncPassword = password;
            store.Set("syncFolder", syncFolder);
            RunFolderSync();
        }));
        SettingsAction(panel, Button("Şimdi eşitle", () =>
        {
            if (syncFolder == null) { SetSyncStatus("Önce senkron klasörünü seçin"); return; }
            syncPassword ??= store.CloudDefaultPassword ?? AskFolderPassword(owner);
            if (syncPassword != null) RunFolderSync();
        }));
        SettingsAction(panel, Button("Senkronu kapat", () =>
        {
            syncFolder = syncPassword = null;
            store.Set("syncFolder", "");
            store.Set("syncPassword", "");
            store.Set("folderSyncProtected", "");
            SetSyncStatus("");
        }));
        panel.Children.Add(status);
        return panel;
    }

    private static string? AskFolderPassword(Window owner) => SecurityPrompt.Ask(owner, "Senkron için parolanız", "Parola", false,
        description: "Notlarınız bu parolayla şifrelenerek eşitlenir. Bir kez girmeniz yeterli.");

    private string? ReadFolderPassword()
    {
        if (store.IsEncrypted && store.Setting("syncPassword") is { Length: > 0 } saved) return saved;
        if (store.Setting("folderSyncProtected") is not { Length: > 0 } protectedValue) return null;
        try
        {
            var bytes = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            try { return System.Text.Encoding.UTF8.GetString(bytes); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception error) when (error is FormatException or System.Security.Cryptography.CryptographicException) { return null; }
    }

    private void SaveFolderPassword(string password)
    {
        if (ReadFolderPassword() == password) return;
        if (store.IsEncrypted) { store.Set("syncPassword", password); return; }
        var bytes = System.Text.Encoding.UTF8.GetBytes(password);
        try
        {
            var encoded = System.Security.Cryptography.ProtectedData.Protect(bytes, null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            store.Set("folderSyncProtected", Convert.ToBase64String(encoded));
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
