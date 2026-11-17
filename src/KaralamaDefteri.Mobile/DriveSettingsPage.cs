namespace KaralamaDefteri.Mobile;

internal sealed class DriveSettingsPage : ContentPage
{
    private readonly MobileDriveSync drive;
    private readonly Label status = MobileStyle.Label("");
    private readonly Label connectedAccount = MobileStyle.Label("", 12);
    private readonly Entry password = MobileStyle.Entry("Senkron parolası");
    private readonly Picker area = new() { Title = "Drive alanı", ItemsSource = new[] { "Standart", "Ayrı günlük alan" } };
    private readonly Button connect;
    private readonly Button chooseAccount;
    private readonly Label selectedAccountLabel = MobileStyle.Label("");
    private readonly Button now;
    private readonly Button disconnect;
    private string? selectedAccount;

    internal DriveSettingsPage(MobileDriveSync drive)
    {
        this.drive = drive;
        Title = "Google Drive";
        MobileStyle.Page(this);
        var frequency = new Picker { Title = "Eşitleme sıklığı", ItemsSource = new[] { "Değişiklik olunca", "Saatlik", "Günde bir" }, SelectedIndex = drive.Frequency };
        var daily = new TimePicker { Time = TimeOnly.TryParseExact(drive.DailyTime, "HH:mm", out var time) ? time.ToTimeSpan() : TimeSpan.FromHours(9) };
        var dailyLabel = MobileStyle.Label("Günlük saat");
        daily.IsVisible = dailyLabel.IsVisible = drive.Frequency == 2;
        frequency.SelectedIndexChanged += (_, _) =>
        {
            if (frequency.SelectedIndex < 0) return;
            drive.Frequency = frequency.SelectedIndex;
            daily.IsVisible = dailyLabel.IsVisible = drive.Frequency == 2;
        };
        daily.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(TimePicker.Time))
                drive.DailyTime = TimeOnly.FromTimeSpan(daily.Time).ToString("HH:mm");
        };
        frequency.SetAppThemeColor(Picker.TextColorProperty, MobileStyle.LightText, MobileStyle.DarkText);
        area.SetAppThemeColor(Picker.TextColorProperty, MobileStyle.LightText, MobileStyle.DarkText);
        daily.SetAppThemeColor(TimePicker.TextColorProperty, MobileStyle.LightText, MobileStyle.DarkText);
        area.SelectedIndex = drive.Area == MobileDriveSync.SeparateArea ? 1 : 0;
        password.IsPassword = true;
        chooseAccount = MobileStyle.Button("Google hesabını değiştir…", async () =>
        {
            chooseAccount!.IsEnabled = false;
            try
            {
                if (await GoogleDriveAuthorization.ChooseAccount() is not { } account) return;
                selectedAccount = account;
                selectedAccountLabel.Text = account;
                selectedAccountLabel.IsVisible = true;
                password.Focus();
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceError("Drive account selection: {0}", error.GetType().Name);
                status.Text = "Google hesap seçimi açılamadı.";
            }
            finally { chooseAccount.IsEnabled = !drive.Busy; }
        });
        selectedAccountLabel.IsVisible = false;
        connect = MobileStyle.Button("Google Drive’a bağlan", async () =>
        {
            var entered = password.Text;
            if (string.IsNullOrWhiteSpace(entered))
            {
                status.Text = "Windows’ta bu alan için kullanılan senkron parolasını girin.";
                return;
            }
            password.Text = "";
            await drive.Connect(entered, area.SelectedIndex == 1 ? MobileDriveSync.SeparateArea : "", selectedAccount);
        }, true);
        now = MobileStyle.Button("Şimdi eşitle", async () => await drive.SyncNow());
        disconnect = MobileStyle.Button("Bağlantıyı kes", drive.Disconnect);
        var layout = new VerticalStackLayout
        {
            Padding = 16, Spacing = 10,
            Children =
            {
                status, connectedAccount, MobileStyle.Label("Windows ile aynı Drive alanını ve senkron parolasını kullanın."),
                chooseAccount, selectedAccountLabel, MobileStyle.Label("Drive alanı", bold: true), area,
                MobileStyle.Label("Standart ve Ayrı günlük alan, aynı Google hesabındaki iki ayrı not alanıdır. Windows’ta Ayarlar → Senkron altında görünen alanı seçin.", 12),
                password, connect, now, disconnect,
                MobileStyle.Label("Eşitleme sıklığı", bold: true), frequency, dailyLabel, daily
            }
        };
        MobileStyle.SetContent(this, new ScrollView { Content = layout });
        Update();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        drive.StatusChanged += Update;
        Update();
        await drive.LoadAccountName();
        Update();
    }
    protected override void OnDisappearing() { drive.StatusChanged -= Update; base.OnDisappearing(); }

    private void Update()
    {
        status.Text = drive.Status.Length != 0 ? drive.Status : drive.Connected ? "Google Drive bağlı." : "Google Drive bağlı değil.";
        connectedAccount.IsVisible = drive.Connected;
        connectedAccount.Text = drive.AccountName is { Length: > 0 } account ? $"Bağlı Google hesabı · {account}" :
            "Hesap adresini görmek için Şimdi eşitle’yi kullan.";
        connect.IsEnabled = area.IsEnabled = password.IsEnabled = chooseAccount.IsEnabled = !drive.Busy;
        now.IsVisible = disconnect.IsVisible = drive.Connected;
        now.IsEnabled = disconnect.IsEnabled = !drive.Busy;
    }
}
