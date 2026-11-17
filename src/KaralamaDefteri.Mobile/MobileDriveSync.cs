using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace KaralamaDefteri.Mobile;

internal sealed class MobileDriveSync
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string SessionKey = "karalama.drive.session.v1";
    internal const string SeparateArea = "personal-v1-";
    private readonly NoteStore store;
    private readonly string database;
    private readonly IDispatcherTimer poll;
    private readonly IDispatcherTimer debounce;
    private DateTimeOffset nextAttempt;
    private bool busy;
    private bool pending;
    private bool foreground = true;
    private Guid? openNote;
    private readonly object editorLock = new();

    private sealed record Session(string Account, string Area, byte[] Salt, byte[] Key, string? AccountName = null);

    internal MobileDriveSync(NoteStore store, string database, IDispatcher dispatcher)
    {
        this.store = store;
        this.database = database;
        poll = dispatcher.CreateTimer();
        poll.Interval = TimeSpan.FromSeconds(30);
        poll.Tick += async (_, _) => await Scheduled(false);
        debounce = dispatcher.CreateTimer();
        debounce.Interval = TimeSpan.FromSeconds(5);
        debounce.IsRepeating = false;
        debounce.Tick += async (_, _) => await Scheduled(true);
    }

    internal event Action? LocalChanged;
    internal event Action? StatusChanged;
    internal bool Connected => store.Setting("mobileDriveConnected") == "1";
    internal bool Busy => busy;
    internal string Area => store.Setting("mobileDriveArea") ?? "";
    internal string Status { get; private set; } = "";
    internal string? AccountName { get; private set; }

    internal async Task LoadAccountName()
    {
        if (!Connected || AccountName is not null) return;
        try
        {
            if (await SecureStorage.Default.GetAsync(SessionKey) is not { } stored) return;
            var session = JsonSerializer.Deserialize<Session>(stored);
            if (session is null) return;
            try
            {
                if (Connected && AccountName is null) AccountName = session.AccountName;
            }
            finally { CryptographicOperations.ZeroMemory(session.Key); }
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Drive account display: {0}", error.GetType().Name);
        }
    }
    internal int Frequency
    {
        get => int.TryParse(store.Setting("cloudFrequency"), out var frequency) ? Math.Clamp(frequency, 0, 2) : 0;
        set => store.Set("cloudFrequency", value.ToString());
    }
    internal string DailyTime
    {
        get => store.Setting("cloudDailyTime") ?? "09:00";
        set => store.Set("cloudDailyTime", value);
    }

    internal void Editing(Guid? id)
    {
        lock (editorLock) openNote = id;
    }

    private bool IsEditing(Guid id)
    {
        lock (editorLock) return openNote == id;
    }

    internal void Changed()
    {
        if (!foreground || !Connected || Frequency != 0) return;
        debounce.Stop();
        debounce.Start();
    }

    internal void Pause()
    {
        foreground = false;
        poll.Stop();
        debounce.Stop();
    }

    internal async Task Resume()
    {
        foreground = true;
        if (!poll.IsRunning) poll.Start();
        await Scheduled(false);
    }

    private async Task Scheduled(bool edited)
    {
        if (!foreground || !Connected || DateTimeOffset.Now < nextAttempt) return;
        var last = long.TryParse(store.Setting("mobileDriveLast"), out var seconds) &&
            seconds is > 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime() : default;
        var daily = TimeOnly.TryParseExact(DailyTime, "HH:mm", out var time) ? time : new TimeOnly(9, 0);
        if (CloudSyncSchedule.ShouldRun(Frequency, DateTimeOffset.Now, last, daily, edited))
            await SyncCore(false, null, Area);
    }

    internal Task Connect(string password, string area, string? accountName) => SyncCore(true, password, area, accountName);
    internal Task SyncNow() => Connected ? SyncCore(true, null, Area) : Task.CompletedTask;

    internal void Disconnect()
    {
        if (busy) return;
        SecureStorage.Default.Remove(SessionKey);
        AccountName = null;
        store.Set("mobileDriveConnected", "");
        store.Set("mobileDriveLast", "");
        pending = false;
        nextAttempt = default;
        SetStatus("Bağlantı kesildi.");
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    private async Task SyncCore(bool interactive, string? password, string area, string? accountName = null)
    {
        if (busy)
        {
            if (!interactive) pending = true;
            return;
        }
        busy = true;
        SetStatus("Google Drive eşitleniyor…");
        var localChanged = false;
        Session? saved = null;
        byte[]? resultKey = null;
        var stage = "stored session";
        try
        {
            if (password is null && await SecureStorage.Default.GetAsync(SessionKey) is { } stored)
                saved = JsonSerializer.Deserialize<Session>(stored);
            if (password is null && saved is null)
            {
                SetStatus("Bağlantıyı senkron parolasıyla yeniden kurun.");
                return;
            }
            stage = "account selection";
            if (password is not null && accountName is null)
            {
                accountName = await GoogleDriveAuthorization.ChooseAccount();
                if (accountName is null)
                {
                    SetStatus("Google hesabı seçilmedi.");
                    return;
                }
            }
            stage = "Google authorization";
            var token = await GoogleDriveAuthorization.GetToken(interactive, accountName ?? saved?.AccountName);
            if (string.IsNullOrEmpty(token))
            {
                SetStatus("Google Drive iznini Ayarlar üzerinden tamamlayın.");
                return;
            }
            stage = "Drive account lookup";
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://www.googleapis.com/drive/v3/about?fields=user(permissionId,emailAddress)");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var identity = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            var user = identity.RootElement.GetProperty("user");
            var account = user.GetProperty("permissionId").GetString();
            if (string.IsNullOrWhiteSpace(account)) throw new InvalidDataException();
            if (saved is not null && (account != saved.Account || area != saved.Area))
            {
                SetStatus("Google hesabı değişti. Senkron bağlantısını yeniden kurun.");
                return;
            }
            stage = "Drive exchange";
            var databasePassword = store.CloudDefaultPassword;
            var result = await Task.Run(() => DriveSyncExchange.Run(Http, token, database, databasePassword, area,
                password, saved?.Salt, saved?.Key, () => localChanged = true, IsEditing));
            resultKey = result.Key;
            stage = "session save";
            var email = user.TryGetProperty("emailAddress", out var address) ? address.GetString() : null;
            email ??= accountName ?? saved?.AccountName;
            if (saved is null || saved.AccountName != email)
                await SecureStorage.Default.SetAsync(SessionKey, JsonSerializer.Serialize(new Session(account, area, result.Salt, result.Key, email)));
            AccountName = email;
            store.Set("mobileDriveArea", area);
            store.Set("mobileDriveConnected", "1");
            store.Set("mobileDriveLast", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            nextAttempt = default;
            SetStatus(result.Conflicts == 0 ? "Google Drive eşitlendi." : $"Google Drive eşitlendi. {result.Conflicts} çakışma kopyası.");
        }
        catch (CloudSyncKeyMismatchException)
        {
            SetStatus("Senkron parolası uyuşmuyor. Windows’ta bu alan için kullanılan parolayla yeniden bağlanın.");
        }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            SetStatus("Google Drive erişimi reddedildi. Hesabı ve uygulama iznini kontrol edin.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            SetStatus("Google Drive’a ulaşılamadı. Bağlantıyı kontrol edin.");
        }
        catch (Android.Gms.Common.Apis.ApiException error)
        {
            SetStatus($"Google yetkilendirmesi tamamlanamadı ({error.StatusCode}). Google Play Hizmetleri ve OAuth kaydını kontrol edin.");
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Drive {0}: {1}", stage, error.GetType().Name);
            SetStatus("Google Drive eşitlemesi tamamlanamadı. Bağlantıyı yeniden deneyin.");
        }
        finally
        {
            if (saved is not null) CryptographicOperations.ZeroMemory(saved.Key);
            if (resultKey is not null) CryptographicOperations.ZeroMemory(resultKey);
            busy = false;
            if (Status.StartsWith("Google Drive eşitlendi.", StringComparison.Ordinal))
            {
                if (pending) Changed();
            }
            else nextAttempt = DateTimeOffset.Now.AddMinutes(1);
            pending = false;
            if (localChanged) LocalChanged?.Invoke();
            StatusChanged?.Invoke();
        }
    }
}
