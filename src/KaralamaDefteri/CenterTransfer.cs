using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace KaralamaDefteri;

public sealed partial class Center
{
    internal bool RestoreInProgress { get; private set; }

    private bool dailyBackupRunning;

    internal void ScheduleDailyBackup()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        async void Run()
        {
            if (dailyBackupRunning || exiting || RestoreInProgress || !Flush(false)) return;
            dailyBackupRunning = true;
            var password = store.CloudDefaultPassword;
            try
            {
                await Task.Run(() =>
                {
                    using var connection = new NoteStore(AppPaths.Database, password, syncConnection: true);
                    connection.DailyBackup(AppPaths.Backups);
                });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
            {
                if (!exiting) Notify("Günlük yedek oluşturulamadı.");
            }
            finally { dailyBackupRunning = false; }
        }
        timer.Tick += (_, _) => Run();
        Closed += (_, _) => timer.Stop();
        Dispatcher.BeginInvoke(Run, DispatcherPriority.ApplicationIdle);
        timer.Start();
    }

    private StackPanel TransferSettings(Window owner)
    {
        var panel = new StackPanel();
        var backup = SettingsCard(panel, "Yedekleme");
        var import = SettingsCard(panel, "İçe aktarma");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        void Run(Action action) => Transfer(action, message => status.Text = message);
        var backupButton = Button("Yedek al", () => Run(() =>
        {
            if (!Flush(false)) return;
            SaveSession();
            status.Text = $"Yedek oluşturuldu · {Path.GetFileName(store.Backup(AppPaths.Backups))}";
        }));
        backupButton.SetResourceReference(StyleProperty, "PrimaryButton");
        SettingsAction(backup, backupButton);
        SettingsAction(backup, Button("Yedekten geri yükle…", () =>
        {
            if (cloudWriting || dailyBackupRunning) { Notify("Arka plan işlemi tamamlanınca tekrar deneyin."); return; }
            var dialog = new OpenFileDialog
            {
                Title = "Geri yüklenecek yedeği seçin", Filter = "Karalama Defteri yedeği (*.db)|*.db",
                InitialDirectory = AppPaths.Backups
            };
            if (dialog.ShowDialog(owner) != true || cloudWriting || dailyBackupRunning || !Flush(false)) return;
            Run(() =>
            {
                SaveSession();
                RestoreInProgress = true;
                try
                {
                    try { store.RestoreBackup(dialog.FileName, AppPaths.Backups); }
                    catch (Microsoft.Data.Sqlite.SqliteException error) when (store.IsEncrypted && error.SqliteErrorCode == 26)
                    {
                        var previous = SecurityPrompt.Ask(owner, "Yedek parolası", "Yedeği oluştururken kullanılan ana parola", false);
                        if (previous == null) return;
                        store.RestoreBackup(dialog.FileName, AppPaths.Backups, previous);
                    }
                    RestartApplication();
                }
                finally { RestoreInProgress = false; }
            });
        }));
        SettingsAction(import, Button("Metin dosyalarını içe aktar…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Metin ve Markdown (*.txt;*.md)|*.txt;*.md", Multiselect = true };
            if (dialog.ShowDialog(owner) == true) Run(() => ImportNotes(dialog.FileNames));
        }));
        var drop = new Border { Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 0), AllowDrop = true,
            Child = new TextBlock { Text = ".txt ve .md dosyalarını buraya bırakın" } };
        drop.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        drop.DragOver += (_, e) =>
        {
            e.Effects = TextFiles(e.Data).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        drop.Drop += (_, e) => { e.Handled = true; Run(() => ImportNotes(TextFiles(e.Data))); };
        import.Children.Add(drop);
        backup.Children.Add(status);
        return panel;
    }

    private void RestartApplication()
    {
        if (cloudWriting || dailyBackupRunning) { Notify("Arka plan işlemi tamamlanınca tekrar deneyin."); return; }
        exiting = true;
        try
        {
            var restart = new ProcessStartInfo(AppPaths.Executable) { UseShellExecute = false };
            restart.ArgumentList.Add("--restore-restart");
            restart.ArgumentList.Add(Environment.ProcessId.ToString());
            using var process = Process.Start(restart);
        }
        finally { Application.Current.Shutdown(); }
    }

    private static string[] TextFiles(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] paths ? paths.Where(IsTextFile).ToArray() : [];

    private static bool IsTextFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md";

    private void ImportNotes(string[] paths)
    {
        var files = paths.Where(IsTextFile).Select(path => (Title: Path.GetFileNameWithoutExtension(path), Text: File.ReadAllText(path))).ToArray();
        if (files.Length == 0) return;
        var group = NewNoteGroup;
        store.Atomic(() =>
        {
            foreach (var file in files) store.Create(file.Text, group, file.Title);
        });
        RefreshCards();
        Notify($"{files.Length} dosya içe aktarıldı.");
    }

    private void ExportNote(long id)
    {
        if (!Flush(false) || store.Get(id) is not { } note) return;
        var dialog = new SaveFileDialog { Filter = "Markdown (*.md)|*.md|Düz metin (*.txt)|*.txt", FileName = ExportName(note.Title) };
        if (dialog.ShowDialog(this) != true) return;
        Transfer(() =>
        {
            var content = Path.GetExtension(dialog.FileName).Equals(".md", StringComparison.OrdinalIgnoreCase)
                ? store.ExportMarkdown(note, Path.GetDirectoryName(dialog.FileName)!)
                : ImageMarkers.Strip(note.Content);
            File.WriteAllText(dialog.FileName, content);
            Notify("Not dışa aktarıldı.");
        });
    }

    private void ExportGroup(NoteGroup group)
    {
        if (!Flush(false)) return;
        var dialog = new OpenFolderDialog { Title = "Grup çıktısının oluşturulacağı klasörü seçin" };
        if (dialog.ShowDialog(this) != true) return;
        Transfer(() =>
        {
            var directory = Path.Combine(dialog.FolderName, $"{ExportName(group.Name)}_{DateTime.Now:yyyyMMdd_HHmmssfff}");
            Directory.CreateDirectory(directory);
            foreach (var note in store.GroupNotes(group.Id))
                File.WriteAllText(Path.Combine(directory, $"{note.Id}_{ExportName(note.Title)}.md"), store.ExportMarkdown(note, directory));
            Notify("Grup dışa aktarıldı.");
        });
    }

    private static string ExportName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(title.Take(80).Select(c => invalid.Contains(c) || char.IsSurrogate(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return safe.Length == 0 ? "Not" : safe;
    }

    private void Transfer(Action action, Action<string>? warning = null)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or Win32Exception)
        {
            if (warning != null) warning(error.Message);
            else Notify(error.Message);
        }
    }
}
