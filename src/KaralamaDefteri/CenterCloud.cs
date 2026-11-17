using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private static readonly HttpClient CloudHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private StackPanel SyncSettings(Window owner)
    {
        var panel = new StackPanel();
        var syncState = SettingsCard(panel, "Eşitleme durumu");
        syncState.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(cloudDetail) ? "Bulut eşitlemesi bekliyor." : cloudDetail, TextWrapping = TextWrapping.Wrap });
        SettingsCard(panel, "Klasör").Children.Add(FolderSyncSettings(owner));
        if (!Features.Enabled(Feature.CloudSync)) return panel;
        var frequency = new ComboBox { ItemsSource = new[] { "Değişiklik olunca", "Saatlik", "Günde bir" },
            SelectedIndex = int.TryParse(store.Setting("cloudFrequency"), out var mode) ? Math.Clamp(mode, 0, 2) : 0 };
        var time = new TextBox { Text = store.Setting("cloudDailyTime") ?? "09:00" };
        var timeLabel = new TextBlock { Text = "Günlük saat (SS:dd)" };
        void SetTimeVisibility() => time.Visibility = timeLabel.Visibility =
            frequency.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        frequency.SelectionChanged += (_, _) =>
        {
            store.Set("cloudFrequency", frequency.SelectedIndex.ToString());
            SetTimeVisibility();
        };
        time.LostKeyboardFocus += (_, _) =>
        {
            if (TimeOnly.TryParseExact(time.Text, "HH:mm", out var selected))
                store.Set("cloudDailyTime", selected.ToString("HH:mm"));
            else time.Text = store.Setting("cloudDailyTime") ?? "09:00";
        };
        var provider = SettingsCard(panel, "Bulut sağlayıcıları");
        var schedule = SettingsCard(panel, "Eşitleme sıklığı");
        SettingsField(schedule, "Sıklık", frequency);
        schedule.Children.Add(timeLabel);
        schedule.Children.Add(time);
        time.Width = 80;
        time.HorizontalAlignment = HorizontalAlignment.Left;
        SetTimeVisibility();
        foreach (var google in new[] { true, false })
        {
            var name = google ? "Google Drive" : "OneDrive";
            var button = Button($"{name} ile eşitle…");
            button.IsEnabled = CloudConfiguration(google) != null;
            SettingsAction(provider, button);
            if (google) provider.Children.Add(Brand("Google Drive ve gizlilik", PrivacyWebsite));
            if (!button.IsEnabled) { provider.Children.Add(UnavailableSync()); continue; }
            var identity = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 2, 0, 8) };
            if (google)
            {
                provider.Children.Add(identity);
                identity.Loaded += async (_, _) => await ShowGoogleDriveIdentity(identity);
            }
            button.Click += async (_, _) =>
            {
                await RunCloudSync(google, owner);
                if (google) await ShowGoogleDriveIdentity(identity);
            };
            var disconnect = Button($"{name} bağlantısını kes", () =>
            {
                if (cloudBusy) return;
                store.Set(CloudStateKey(google), "");
                store.Set(CloudStateKey(google) + "Last", "");
                cloudLastSuccess.Remove(google);
                cloudNextAttempt.Remove(google);
                File.Delete(AppPaths.CloudSession(google));
                if (google) identity.Text = "Google Drive bağlı değil.";
                SetCloudStatus("", "");
            });
            disconnect.SetResourceReference(ForegroundProperty, "WarningInkBrush");
            SettingsAction(provider, disconnect);
        }
        var icloud = Button("iCloud ile eşitle…");
        icloud.IsEnabled = false;
        SettingsAction(provider, icloud);
        provider.Children.Add(UnavailableSync());
        return panel;
    }

    private async Task ShowGoogleDriveIdentity(TextBlock label)
    {
        label.Text = "Google Drive bağlı değil.";
        try
        {
            if (ReadCloudSession(true) is not { } session || CloudConfiguration(true) is not { } config ||
                session.ClientId != config.ClientId) return;
            var area = session.Prefix == "personal-v1-" ? "Ayrı günlük alan" : "Standart";
            label.Text = $"Drive alanı · {area}";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var tokens = session.Tokens;
            if (tokens.Expires <= DateTimeOffset.UtcNow.AddMinutes(1))
                tokens = await new DesktopOAuth(CloudHttp, config).RefreshAsync(tokens.Refresh, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://www.googleapis.com/drive/v3/about?fields=user(emailAddress)");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access);
            using var response = await CloudHttp.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var result = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(timeout.Token));
            if (ReadCloudSession(true) is not { } current || current.Tokens.Refresh != session.Tokens.Refresh ||
                current.Prefix != session.Prefix) return;
            if (result.RootElement.TryGetProperty("user", out var user) &&
                user.TryGetProperty("emailAddress", out var address) && address.GetString() is { Length: > 0 } email)
                label.Text = $"Google hesabı · {email}\nDrive alanı · {area}";
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or
            CryptographicException or FormatException or System.Text.Json.JsonException)
        {
            // Keep the saved area visible if the account lookup is temporarily unavailable.
        }
    }

    private (string? Password, bool Reset)? ResolveCloudKey(Window owner, string provider)
    {
        var window = Owned("Bulut parolası uyuşmuyor", 490, 320);
        window.Owner = owner;
        var panel = new StackPanel { Margin = new Thickness(18) };
        window.Content = panel;
        panel.Children.Add(new TextBlock
        {
            Text = $"Bu {provider} alanında başka bir parolayla şifrelenmiş Karalama Defteri verisi var.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        (string? Password, bool Reset)? choice = null;
        panel.Children.Add(Button("O parolayı gir", () =>
        {
            var password = SecurityPrompt.Ask(window, "Bulut parolası", "Bu alan için önceki parola", false);
            if (password == null) return;
            choice = (password, false);
            window.DialogResult = true;
        }));
        panel.Children.Add(Button("Kurtarma anahtarı gir", () =>
        {
            if (!File.Exists(AppPaths.Recovery))
            {
                Notify("Bu cihazda kurtarma kaydı bulunamadı.");
                return;
            }
            var phrase = SecurityPrompt.Ask(window, "Kurtarma anahtarı", "24 kelimelik yerel kurtarma anahtarı", false);
            if (phrase == null) return;
            try
            {
                choice = (RecoveryPhrase.Unprotect(File.ReadAllBytes(AppPaths.Recovery), phrase), false);
                window.DialogResult = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Notify("Kurtarma anahtarı bu cihazda doğrulanamadı.");
            }
        }));
        panel.Children.Add(Button("Buluttaki veriyi sil ve bu cihazdan başlat", () =>
        {
            var confirmation = SecurityPrompt.Ask(window, "Bulut alanını sıfırla",
                "Yalnız seçili bulut alanı silinir. Yerel notlar korunur. Onaylamak için SİL yazın.", false);
            if (!string.Equals(confirmation, "SİL", StringComparison.Ordinal)) return;
            choice = (null, true);
            window.DialogResult = true;
        }));
        window.ShowDialog();
        return choice;
    }

    private static TextBlock UnavailableSync()
    {
        var label = new TextBlock { Text = "Bu sürümde henüz etkin değil", Margin = new Thickness(6, 2, 0, 8) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return label;
    }

    private static OAuthConfiguration? CloudConfiguration(bool google)
    {
        var key = google ? "GoogleOAuthClientId" : "OneDriveOAuthClientId";
        var id = typeof(Center).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(entry => entry.Key == key)?.Value;
        var secret = google ? Environment.GetEnvironmentVariable("KARALAMA_GOOGLE_DESKTOP_SECRET") : null;
        if (google)
        {
            var local = ReadGoogleOAuthConfiguration(() => File.Exists(AppPaths.GoogleOAuthConfiguration)
                ? File.OpenRead(AppPaths.GoogleOAuthConfiguration) : null);
            var installed = local.Id != null ? local : ReadGoogleOAuthConfiguration(() =>
                typeof(Center).Assembly.GetManifestResourceStream("KaralamaDefteri.GoogleDesktopOAuth.json"));
            if (installed.Id != null) (id, secret) = installed;
        }
        if (string.IsNullOrWhiteSpace(id)) return null;
        return google
            ? new(new("https://accounts.google.com/o/oauth2/v2/auth"), new("https://oauth2.googleapis.com/token"), id,
                "https://www.googleapis.com/auth/drive.appdata", true, secret)
            : new(new("https://login.microsoftonline.com/common/oauth2/v2.0/authorize"), new("https://login.microsoftonline.com/common/oauth2/v2.0/token"), id,
                "offline_access https://graph.microsoft.com/Files.ReadWrite.AppFolder", false);
    }

    private static (string? Id, string? Secret) ReadGoogleOAuthConfiguration(Func<Stream?> open)
    {
        try
        {
            using var stream = open();
            if (stream == null) return default;
            using var json = System.Text.Json.JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("installed", out var installed) ||
                installed.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !installed.TryGetProperty("client_id", out var id) || id.ValueKind != System.Text.Json.JsonValueKind.String ||
                !installed.TryGetProperty("client_secret", out var secret) || secret.ValueKind != System.Text.Json.JsonValueKind.String ||
                id.GetString() is not { } clientId || !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(secret.GetString())) return default;
            return (clientId, secret.GetString());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return default;
        }
    }
}
