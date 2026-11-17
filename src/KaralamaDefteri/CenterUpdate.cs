using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private static readonly UpdateDownload Updates = new(new HttpClient { Timeout = TimeSpan.FromSeconds(25) });
    private readonly TextBlock updateBadge = new() { Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
    private UpdateManifest? availableUpdate;
    private bool checkingUpdate;
    private bool installingUpdate;
    private int updateChannelRevision;

    internal void StartUpdateCheck()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        timer.Tick += async (_, _) => await CheckAutomatically();
        Closed += (_, _) => timer.Stop();
        timer.Start();
        Dispatcher.BeginInvoke(new Action(async () => await CheckAutomatically()));
    }

    private async Task CheckAutomatically()
    {
        if (!exiting && !RestoreInProgress && store.Setting("updateAuto") != "0")
            await CheckUpdate(false);
    }

    private static string? UpdateMetadata(string key) =>
        typeof(Center).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value;

    private async Task CheckUpdate(bool manual)
    {
        if (checkingUpdate || installingUpdate || exiting || RestoreInProgress) return;
        var beta = store.Setting("updateChannel") == "beta";
        var channelRevision = updateChannelRevision;
        var setting = beta ? "checkedBetaUpdate" : "checkedStableUpdate";
        if (!manual && long.TryParse(store.Setting(setting), out var time) &&
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() - time < 86400) return;
        if (!Uri.TryCreate(UpdateMetadata(beta ? "BetaUpdateFeed" : "StableUpdateFeed"), UriKind.Absolute, out var feed))
        {
            if (manual) DisplayUpdate(null, "Güncelleme adresi bu derlemede yapılandırılmamış.");
            return;
        }
        checkingUpdate = true;
        try
        {
            store.Set(setting, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            SupportOptions? support = null;
            var release = await Updates.CheckAsync(feed, BuildInfo.Version, beta, CancellationToken.None, value => support = value);
            if (exiting || RestoreInProgress || channelRevision != updateChannelRevision) return;
            if (support != null) CacheSupport(support);
            DisplayUpdate(release, manual ? "Bu kanal için uygun yeni sürüm bulunamadı." : "");
        }
        catch (Exception error) when (IsUpdateError(error))
        {
            if (manual && !exiting && !RestoreInProgress && channelRevision == updateChannelRevision && availableUpdate == null)
                DisplayUpdate(null, "Güncelleme kontrolü tamamlanamadı.");
        }
        finally { checkingUpdate = false; }
    }

    private void DisplayUpdate(UpdateManifest? release, string emptyMessage = "")
    {
        availableUpdate = release;
        updateBadge.Inlines.Clear();
        if (release == null)
        {
            if (emptyMessage.Length != 0) updateBadge.Inlines.Add(new Run(emptyMessage));
            return;
        }
        var link = new Hyperlink(new Run($"↑ v{release.Version.TrimStart('v')} hazır"));
        link.Click += (_, _) => ShowUpdate();
        updateBadge.Inlines.Add(link);
    }

    private void ShowUpdate()
    {
        if (availableUpdate is not { } release || installingUpdate) return;
        var window = Owned("Güncelleme", 450, 300);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var releaseCard = SettingsCard(panel, $"Sürüm {release.Version}");
        releaseCard.Children.Add(new TextBlock { Text = release.Notes, TextWrapping = TextWrapping.Wrap });
        var apply = Button("Güncelle");
        apply.SetResourceReference(StyleProperty, "PrimaryButton");
        SettingsAction(releaseCard, apply);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var progress = new ProgressBar { Height = 8, Maximum = Math.Max(1, release.Size), Margin = new Thickness(0,8,0,4),
            Visibility = Visibility.Collapsed };
        panel.Children.Add(progress);
        panel.Children.Add(status);
        apply.Click += async (_, _) =>
        {
            if (installingUpdate || availableUpdate != release) return;
            installingUpdate = true;
            apply.IsEnabled = false;
            apply.Content = "Güncelleniyor…";
            progress.Value = 0;
            progress.Visibility = Visibility.Visible;
            try
            {
                Directory.CreateDirectory(AppPaths.Updates);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                status.Text = "Güncelleme indiriliyor…";
                await Updates.DownloadAsync(release, UpdateStartup.Download, timeout.Token, new Progress<long>(bytes =>
                {
                    progress.Value = bytes;
                    status.Text = $"{100.0 * bytes / release.Size:0}% · {bytes / 1048576.0:0.0} / {release.Size / 1048576.0:0.0} MB";
                }));
                UpdateSignature.Verify(UpdateStartup.Download, UpdateMetadata("UpdateSignerThumbprint"));
                if (exiting || RestoreInProgress || !Flush(false)) return;
                store.Backup(AppPaths.Backups, "guncelleme_oncesi");
                ((App)Application.Current).BeginUpdate(release.Sha256);
            }
            catch (Exception error) when (IsUpdateError(error))
            { System.Diagnostics.Trace.TraceWarning("Güncelleme: {0}", error.Message); status.Text = "Güncelleme uygulanamadı. Bağlantınızı kontrol edip tekrar deneyin."; }
            finally { installingUpdate = false; apply.IsEnabled = true; apply.Content = "Güncelle"; }
        };
        window.Content = new ScrollViewer { Content = panel };
        window.Show();
    }

    private StackPanel UpdateSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        var automatic = new CheckBox { Content = "Güncellemeleri otomatik kontrol et", IsChecked = store.Setting("updateAuto") != "0" };
        automatic.Click += (_, _) => store.Set("updateAuto", automatic.IsChecked == true ? "1" : "0");
        var preferences = SettingsCard(panel, "Güncelleme tercihleri");
        automatic.SetResourceReference(StyleProperty, "SettingsSwitch");
        preferences.Children.Add(automatic);
        var channel = new ComboBox { ItemsSource = new[] { "Kararlı", "Beta" }, SelectedIndex = store.Setting("updateChannel") == "beta" ? 1 : 0 };
        channel.SelectionChanged += (_, _) =>
        {
            store.Set("updateChannel", channel.SelectedIndex == 1 ? "beta" : "stable");
            updateChannelRevision++;
            DisplayUpdate(null);
        };
        SettingsField(preferences, "Güncelleme kanalı", channel);
        var previousCheck = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        void RefreshLastCheck()
        {
            var key = channel.SelectedIndex == 1 ? "checkedBetaUpdate" : "checkedStableUpdate";
            var last = long.TryParse(store.Setting(key), out var timestamp) && timestamp is > 0 and <= 253402300799
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                : "Henüz kontrol edilmedi";
            previousCheck.Text = $"Son kontrol · {last}\nKurulu sürüm · v{BuildInfo.Version}";
        }
        preferences.Children.Add(previousCheck);
        channel.SelectionChanged += (_, _) => RefreshLastCheck();
        RefreshLastCheck();
        var check = Button("Şimdi kontrol et");
        check.Click += async (_, _) => { await CheckUpdate(true); RefreshLastCheck(); };
        SettingsAction(preferences, check);
        return panel;
    }

    private static bool IsUpdateError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or
        System.ComponentModel.Win32Exception or Microsoft.Data.Sqlite.SqliteException or System.Security.Cryptography.CryptographicException;
}
