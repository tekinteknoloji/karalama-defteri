using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private sealed record CloudSession(string ClientId, OAuthTokens Tokens, byte[] Key, byte[] Salt, string Prefix);
    private readonly Button cloudIndicator = Button("");
    private DispatcherTimer? cloudAfterSave;
    private bool cloudBusy;
    private bool cloudPending;
    private bool cloudWriting;
    private bool cloudCardsChanged;
    private readonly Dictionary<bool, DateTimeOffset> cloudLastSuccess = [];
    private readonly Dictionary<bool, DateTimeOffset> cloudNextAttempt = [];
    private string cloudDetail = "";

    private static string CloudStateKey(bool google) => google ? "cloudSessionGoogle" : "cloudSessionOneDrive";

    private CloudSession? ReadCloudSession(bool google)
    {
        var saved = store.Setting(CloudStateKey(google));
        if (string.IsNullOrEmpty(saved)) return null;
        if (saved.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(saved[6..]), null, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<CloudSession>(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        return store.IsEncrypted ? JsonSerializer.Deserialize<CloudSession>(saved) : null;
    }

    private void SaveCloudSession(bool google, CloudSession session)
    {
        if (store.IsEncrypted)
        {
            store.Set(CloudStateKey(google), JsonSerializer.Serialize(session));
            return;
        }
        var plain = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            store.Set(CloudStateKey(google), "dpapi:" + Convert.ToBase64String(protectedBytes));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private void SetCloudStatus(string symbol, string detail)
    {
        cloudDetail = detail;
        cloudIndicator.Content = symbol;
        cloudIndicator.ToolTip = detail;
        cloudIndicator.Visibility = symbol.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void StartCloudSync()
    {
        SetCloudStatus("", "");
        cloudIndicator.Click += (_, _) =>
        {
            if (cloudDetail.Length > 0)
                new ContextMenu { PlacementTarget = cloudIndicator,
                    Items = { new MenuItem { Header = cloudDetail, IsEnabled = false } }, IsOpen = true };
        };
        cloudAfterSave = Timer(5000, () =>
        {
            cloudAfterSave!.Stop();
            _ = RunScheduledCloudSync(afterEdit: true);
        });
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += async (_, _) => await RunScheduledCloudSync(afterEdit: false);
        Closed += (_, _) => { timer.Stop(); cloudAfterSave.Stop(); };
        timer.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => _ = RunScheduledCloudSync(afterEdit: false, startup: true)));
    }

    private int CloudFrequency => int.TryParse(store.Setting("cloudFrequency"), out var value) ? value : 0;

    private void ScheduleCloudAfterEdit()
    {
        if (cloudAfterSave != null && CloudFrequency == 0 && HasCloudConnection())
            Restart(cloudAfterSave);
    }

    private bool HasCloudConnection() => new[] { true, false }.Any(provider => !string.IsNullOrEmpty(store.Setting(CloudStateKey(provider))));

    private async Task RunScheduledCloudSync(bool afterEdit, bool startup = false)
    {
        if (exiting || RestoreInProgress || !Features.Enabled(Feature.CloudSync)) return;
        foreach (var google in new[] { true, false })
        {
            if (exiting || RestoreInProgress || !Features.Enabled(Feature.CloudSync)) return;
            if (string.IsNullOrEmpty(store.Setting(CloudStateKey(google)))) continue;
            var now = DateTimeOffset.Now;
            if (cloudNextAttempt.TryGetValue(google, out var next) && now < next) continue;
            var last = cloudLastSuccess.GetValueOrDefault(google);
            if (last == default && long.TryParse(store.Setting(CloudStateKey(google) + "Last"), out var timestamp) &&
                timestamp is > 0 and <= 253402300799)
                last = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime();
            var mode = CloudFrequency;
            var scheduled = TimeOnly.TryParseExact(store.Setting("cloudDailyTime"), "HH:mm", out var time) ? time : new TimeOnly(9, 0);
            if ((startup && mode == 0) || CloudSyncSchedule.ShouldRun(mode, now, last, scheduled, afterEdit))
                await RunCloudSync(google, null);
        }
    }

    private async Task RunCloudSync(bool google, Window? owner)
    {
        if (!Features.Enabled(Feature.CloudSync) || exiting || RestoreInProgress || CloudConfiguration(google) is not { } config) return;
        if (cloudBusy) { cloudPending = true; return; }
        cloudBusy = true;
        string name = google ? "Google Drive" : "OneDrive";
        string path = AppPaths.CloudSession(google);
        var changed = false;
        try
        {
            CloudSession? saved;
            try { saved = ReadCloudSession(google); }
            catch (Exception error) when (error is CryptographicException or FormatException or JsonException)
            {
                SetCloudStatus("⚠", "Bu cihazda bulut oturumu açılamadı. Ayarlar → Senkron üzerinden yeniden bağlanın.");
                if (owner == null) return;
                saved = null;
            }
            if (saved?.ClientId != config.ClientId) saved = null;
            if (saved == null && owner == null) return;
            var prefix = owner == null && saved != null ? saved.Prefix :
                google && store.Setting("googleSeparateSync") == "1" ? "personal-v1-" : "";
            var password = store.CloudDefaultPassword;
            if (password == null && owner != null && (saved == null || saved.Prefix != prefix))
                password = SecurityPrompt.Ask(owner, "Senkron için parolanız", "Parola", false,
                    description: "Notlarınız buluta bu parolayla şifrelenerek gönderilir. Bir kez girmeniz yeterli.");
            if ((password == null && (saved == null || saved.Prefix != prefix)) || !Flush(false)) return;
            var initialPassword = password;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var oauth = new DesktopOAuth(CloudHttp, config);
            var legacyPassword = password;
            var tokens = saved?.Tokens;
            if (tokens == null)
                tokens = await Task.Run(() => OpenLegacyCloudSession(path, google, config, legacyPassword!));
            void OpenBrowser(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            SetCloudStatus("⟳", name + " eşitleniyor.");
            if (tokens == null) tokens = await oauth.LoginAsync(OpenBrowser, timeout.Token);
            else
            {
                try { tokens = await oauth.RefreshAsync(tokens.Refresh, timeout.Token); }
                catch (HttpRequestException error) when (owner != null && error.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
                { tokens = await oauth.LoginAsync(OpenBrowser, timeout.Token); }
            }
            if (exiting || RestoreInProgress || !Features.Enabled(Feature.CloudSync) || !Flush(false)) return;
            // Keep the refreshed token if the cloud key needs explicit recovery.
            if (saved != null) SaveCloudSession(google, saved with { Tokens = tokens });
            var provider = new CloudSyncProvider(google
                ? new GoogleDriveObjectStore(CloudHttp, () => tokens.Access)
                : new OneDriveObjectStore(CloudHttp, () => tokens.Access), prefix);
            int conflicts;
            while (true)
            {
                try
                {
                    var previous = Enum.GetValues<Feature>().Select(Features.Enabled).ToArray();
                    var openNotes = editors.Values.Select(editor => Guid.Parse(editor.Note.Uuid)).ToHashSet();
                    var databasePassword = store.CloudDefaultPassword;
                    cloudWriting = true;
                    (int Count, byte[] Salt, byte[] Key) result;
                    try
                    {
                        result = await Task.Run(() =>
                        {
                            var salt = provider.GetSalt();
                            var useSavedKey = saved?.Prefix == prefix && saved.Salt.AsSpan().SequenceEqual(salt);
                            if (!useSavedKey && password == null)
                                throw new CloudSyncKeyMismatchException(new CryptographicException());
                            using var crypto = useSavedKey ? new SyncEncryption(saved!.Key) : new SyncEncryption(password!, salt);
                            using var connection = new NoteStore(AppPaths.Database, databasePassword, syncConnection: true);
                            var count = connection.SyncNotes(provider, crypto, salt, provider.SaveDailyBackup, openNotes, () => changed = true);
                            return (Count: count, Salt: salt, Key: crypto.CopyKey());
                        });
                    }
                    finally { cloudWriting = false; }
                    try
                    {
                        if (exiting || RestoreInProgress) return;
                        conflicts = result.Count;
                        SaveCloudSession(google, new(config.ClientId, tokens, result.Key, result.Salt, prefix));
                    }
                    finally { CryptographicOperations.ZeroMemory(result.Key); }
                    RefreshSyncedFeatures(previous);
                    break;
                }
                catch (CloudSyncKeyMismatchException) when (owner != null)
                {
                    var choice = ResolveCloudKey(owner, name);
                    if (choice == null || !Flush(false))
                    {
                        SetCloudStatus("⚠", "Bulut parolası farklı. Ayarlar → Senkron üzerinden bağlantıyı tamamlayın.");
                        return;
                    }
                    if (choice.Value.Reset)
                    {
                        var replacement = initialPassword ?? SecurityPrompt.Ask(owner, "Senkron için parolanız", "Yeni parola", false);
                        if (replacement == null) return;
                        store.Backup(AppPaths.Backups, "bulut_sifirlama_oncesi");
                        await Task.Run(provider.ResetArea);
                        password = replacement;
                    }
                    else password = choice.Value.Password!;
                    saved = null;
                }
            }
            File.Delete(path);
            cloudLastSuccess[google] = DateTimeOffset.Now;
            store.Set(CloudStateKey(google) + "Last", cloudLastSuccess[google].ToUnixTimeSeconds().ToString());
            cloudNextAttempt.Remove(google);
            SetCloudStatus("✓", conflicts == 0 ? name + " eşitlendi." : $"{name} eşitlendi · {conflicts} çakışma kopyası.");
            if (!Features.Enabled(Feature.CloudSync)) SetCloudStatus("", "");
        }
        catch (CloudSyncKeyMismatchException)
        {
            SetCloudStatus("⚠", "Bulut parolası farklı. Ayarlar → Senkron üzerinden bağlantıyı tamamlayın.");
        }
        catch (AuthenticationTagMismatchException)
        {
            SetCloudStatus("⚠", "Bulut oturumu açılamadı. Bağlantıyı kesip yeniden bağlanın.");
        }
        catch (Exception error) when (CloudObjectStore.IsTransient(error) &&
            error is HttpRequestException or OperationCanceledException)
        {
            SetCloudStatus("⚠", "Eşitleme sürüyor, bağlantı yavaş. Yeniden denenecek.");
        }
        catch (HttpRequestException error)
        {
            SetCloudStatus("⚠", error.StatusCode switch
            {
                System.Net.HttpStatusCode.InsufficientStorage => "Bulut depolama alanı dolu.",
                System.Net.HttpStatusCode.TooManyRequests => "Bulut istek sınırına ulaşıldı. İşlem daha sonra yeniden denenecek.",
                System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized => "Bulut oturumu geçersiz. Ayarlar → Senkron üzerinden yeniden bağlanın.",
                null => "Buluta bağlanılamadı. Bağlantı sağlandığında yeniden denenecek.",
                _ => $"Bulut isteği başarısız oldu (HTTP {(int)error.StatusCode.Value})."
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or
            Microsoft.Data.Sqlite.SqliteException or JsonException or InvalidOperationException or
            System.ComponentModel.Win32Exception or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            SetCloudStatus("⚠", "Bulut eşitlemesi tamamlanamadı. Bağlantıyı kontrol edin ve yeniden deneyin.");
        }
        finally
        {
            var failed = Equals(cloudIndicator.Content, "⚠");
            if (failed)
                cloudNextAttempt[google] = DateTimeOffset.Now.AddMinutes(1);
            cloudBusy = false;
            if ((changed || cloudCardsChanged) && !exiting && !RestoreInProgress)
            {
                cloudCardsChanged = false;
                RefreshCards();
            }
            if (cloudPending)
            {
                cloudPending = false;
                if (!failed && !exiting && !RestoreInProgress && cloudAfterSave != null && CloudFrequency == 0 &&
                    !cloudAfterSave.IsEnabled)
                    Restart(cloudAfterSave);
            }
        }
    }

    private static OAuthTokens? OpenLegacyCloudSession(string path, bool google, OAuthConfiguration config, string password)
    {
        if (!File.Exists(path)) return null;
        var saved = File.ReadAllBytes(path);
        if (saved.Length < 60) return null;
        var id = google ? Guid.Parse("bfde9e94-11ed-4030-a77a-c89dd9c08d41") : Guid.Parse("7d0009cf-bce3-49cc-92a7-849763816a3a");
        using var vault = new SyncEncryption(password, saved[..32]);
        try { return vault.Open<OAuthTokens>(id, config.ClientId, saved[32..]); }
        catch (AuthenticationTagMismatchException) { return null; }
    }
}
