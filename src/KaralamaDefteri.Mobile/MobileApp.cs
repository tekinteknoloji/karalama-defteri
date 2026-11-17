namespace KaralamaDefteri.Mobile;

public sealed class MobileApp : Application
{
    public MobileApp() => RequestedThemeChanged += (_, _) =>
        (Platform.CurrentActivity as MainActivity)?.ApplySystemBarTheme();

    private readonly Queue<MobileEntry> entries = new();
    private readonly string database = Path.Combine(FileSystem.AppDataDirectory, "notes.db");
    private Window? window;
    private NavigationPage? navigation;
    private NoteStore? store;
    private MobileDriveSync? drive;
    private NotesPage? notes;
    private Func<Task>? autoUnlock;
    private bool draining;
    private bool unlocking;
    private bool converting;
    private bool navigatingBack;
    internal bool BiometricEnabled => Preferences.Default.Get("mobileBiometric", false);

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var encrypted = File.Exists(database) && !NoteStore.IsPlainDatabase(database);
        Page initial = BiometricEnabled ? BiometricGate(encrypted) :
            encrypted ? PasswordGate() : BuildNotes(null);
        window = new Window(initial);
        window.Created += async (_, _) => await Resume();
        window.Resumed += async (_, _) => await Resume();
        return window;
    }

    internal void OnAndroidStopped()
    {
        if (navigation?.CurrentPage is NotePage editor) editor.Flush();
        drive?.Pause();
    }

    internal async Task OnAndroidPostResume()
    {
        QueueBiometricPrompt();
        await Resume();
    }

    private NavigationPage BuildNotes(string? password)
    {
        store = new NoteStore(database, password);
        UserAppTheme = Enum.TryParse<AppTheme>(store.Setting("mobileTheme"), out var theme) ? theme : AppTheme.Unspecified;
        drive = new MobileDriveSync(store, database, Dispatcher);
        notes = new NotesPage(store, drive);
        return navigation = new NavigationPage(notes);
    }

    internal void QueueEntry(MobileEntry entry)
    {
        entries.Enqueue(entry);
        _ = DrainEntries();
    }

    private async Task DrainEntries()
    {
        if (draining || converting || notes is null || window?.Page != navigation) return;
        draining = true;
        try
        {
            while (entries.TryDequeue(out var entry))
                await notes.OpenEntry(entry);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(error.GetType().Name);
            if (navigation?.CurrentPage is { } page)
                await page.DisplayAlert("Hızlı not", "Not açılamadı.", "Tamam");
        }
        finally { draining = false; }
    }

    private async Task Resume()
    {
        UpdateScreenProtection();
        (Platform.CurrentActivity as MainActivity)?.ApplySystemBarTheme();
        if (autoUnlock is not null) { QueueBiometricPrompt(); return; }
        if (converting || window?.Page != navigation) return;
        if (drive is not null) await drive.Resume();
        await DrainEntries();
    }

    internal void QueueBiometricPrompt()
    {
        if (autoUnlock is null || unlocking || Platform.CurrentActivity is null) return;
        Dispatcher.Dispatch(async () =>
        {
            if (autoUnlock is not { } authenticate || unlocking) return;
            autoUnlock = null;
            await authenticate();
        });
    }

    internal bool HandleBack()
    {
        if (navigatingBack || unlocking || window?.Page != navigation) return true;
        if (navigation?.CurrentPage is not { } current) return false;
        if (current.SendBackButtonPressed()) return true;
        if (navigation.Navigation.NavigationStack.Count <= 1) return false;
        if (current is NotePage editor && !editor.Flush()) return true;
        navigatingBack = true;
        _ = Pop();
        return true;

        async Task Pop()
        {
            try { await navigation.Navigation.PopAsync(); }
            finally { navigatingBack = false; }
        }
    }

    private void UpdateScreenProtection()
    {
        if (BiometricEnabled || store?.IsEncrypted == true || window?.Page != navigation)
            AndroidUnlock.ProtectScreen();
        else AndroidUnlock.ReleaseScreen();
    }

    internal async Task<bool> SetBiometric(bool enabled)
    {
        if (enabled)
        {
            if (store?.CloudDefaultPassword is { } password)
            {
                if (!await EnrollBiometric(password)) return false;
            }
            else if (!await VerifyDevice()) return false;
        }
        else BiometricPassword.Forget();
        Preferences.Default.Set("mobileBiometric", enabled);
        UpdateScreenProtection();
        return true;
    }

    private async Task<bool> EnrollBiometric(string password)
    {
        if (unlocking) return false;
        unlocking = true;
        try { return await BiometricPassword.Enroll(password); }
        finally { unlocking = false; }
    }

    private async Task<bool> VerifyDevice()
    {
        if (unlocking) return false;
        unlocking = true;
        try { return await AndroidUnlock.Verify(); }
        finally { unlocking = false; }
    }

    private ContentPage BiometricGate(bool encrypted)
    {
        var page = new LockPage();
        var message = MobileStyle.Label("Notlar kilitli.");
        var input = MobileStyle.Entry("Ana parola");
        input.IsPassword = true;
        var passwordButton = MobileStyle.Button("Ana parolayla kilidi aç", async () =>
        {
            if (unlocking || string.IsNullOrEmpty(input.Text)) return;
            var password = input.Text;
            input.Text = "";
            unlocking = true;
            try { await Task.Run(() => NoteStore.VerifyPassword(database, password)); }
            catch (Exception error) when (error is Microsoft.Data.Sqlite.SqliteException or IOException)
            { message.Text = "Ana parola doğrulanamadı."; return; }
            finally { unlocking = false; }
            if (!BiometricPassword.HasPassword) await EnrollBiometric(password);
            await Complete(password);
        }, true);
        var fallback = MobileStyle.Button("Parolayla aç", () =>
        {
            input.IsVisible = passwordButton.IsVisible = true;
            input.Focus();
        });
        input.IsVisible = passwordButton.IsVisible = false;
        fallback.IsVisible = encrypted;
        var button = MobileStyle.Button("Biyometriyle aç", async () => await Authenticate(), true);
        MakeGate(page, message, button, fallback, input, passwordButton);
        if (!encrypted || BiometricPassword.HasPassword) autoUnlock = Authenticate;
        else
        {
            message.Text = "Biyometriyi bu cihazda kurmak için önce ana parolayla aç.";
            button.IsEnabled = false;
        }
        return page;

        async Task Authenticate()
        {
            autoUnlock = null;
            if (unlocking) return;
            if (!encrypted)
            {
                if (await VerifyDevice()) await Complete(null);
                else message.Text = "Doğrulama tamamlanmadı. Tekrar dene.";
                return;
            }
            unlocking = true;
            string? password;
            try { password = await BiometricPassword.Unlock(); }
            finally { unlocking = false; }
            if (password is null)
            {
                message.Text = "Biyometrik kilit açılmadı. Tekrar dene veya ana parolayla aç.";
                return;
            }
            try { await Complete(password); }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                BiometricPassword.Forget();
                message.Text = "Biyometriyi yeniden kurmak için ana parolayla aç.";
            }
        }

        async Task Complete(string? password)
        {
            autoUnlock = null;
            window!.Page = BuildNotes(password);
            await Resume();
        }
    }

    private ContentPage PasswordGate()
    {
        var page = new LockPage();
        var input = MobileStyle.Entry("Ana parola");
        input.IsPassword = true;
        var message = MobileStyle.Label("Şifreli notların kilidini aç.");
        var button = MobileStyle.Button("Kilidi aç", async () =>
        {
            if (unlocking || string.IsNullOrEmpty(input.Text)) return;
            unlocking = true;
            var password = input.Text;
            input.Text = "";
            try
            {
                await Task.Run(() => NoteStore.VerifyPassword(database, password));
                window!.Page = BuildNotes(password);
            }
            catch (Exception error) when (error is Microsoft.Data.Sqlite.SqliteException or IOException)
            {
                message.Text = "Notlar açılamadı. Ana parolayı kontrol et.";
                return;
            }
            finally { unlocking = false; }
            await Resume();
        }, true);
        MakeGate(page, message, input, button);
        return page;
    }

    private static void MakeGate(ContentPage page, params View[] views)
    {
        page.Title = "Karalama Defteri";
        MobileStyle.Page(page);
        var layout = new VerticalStackLayout { Padding = 24, Spacing = 12, VerticalOptions = LayoutOptions.Center };
        foreach (var view in views) layout.Children.Add(view);
        MobileStyle.SetContent(page, layout, canGoBack: false);
    }

    internal Task EnableEncryption(string password) => UpdateEncryption(null, password);

    internal Task ChangePassword(string current, string password) => UpdateEncryption(current, password);

    private async Task UpdateEncryption(string? current, string password)
    {
        if (converting || store is null || drive is null || window is null || drive.Busy)
            throw new InvalidOperationException("Eşitleme bitince tekrar dene.");
        if (current is null ? store.IsEncrypted : !store.MatchesOpenPassword(current))
            throw new InvalidOperationException("Mevcut ana parola yanlış.");
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Yeni parola boş olamaz.");
        converting = true;
        drive.Pause();
        var pending = new ContentPage();
        MakeGate(pending, MobileStyle.Label(current is null ? "Notlar şifreleniyor…" : "Ana parola değiştiriliyor…"));
        window.Page = pending;
        UpdateScreenProtection();
        var replaced = false;
        var biometricNeedsSetup = false;
        try
        {
            store.Checkpoint();
            store.Dispose();
            store = null;
            await Task.Run(() => NoteStore.ReplaceDatabaseFormat(database, current, password));
            replaced = true;
            if (BiometricEnabled && !await EnrollBiometric(password))
            {
                BiometricPassword.Forget();
                Preferences.Default.Set("mobileBiometric", false);
                biometricNeedsSetup = true;
            }
            window.Page = BuildNotes(password);
        }
        catch
        {
            store?.Dispose();
            window.Page = BuildNotes(replaced ? password : current);
            throw;
        }
        finally { converting = false; await Resume(); }
        if (current is not null && navigation?.CurrentPage is { } home)
            await home.DisplayAlert("Ana parola değiştirildi",
                biometricNeedsSetup ? "Yeni parola etkin. Biyometriyi Güvenlik ekranından yeniden açabilirsin." :
                    "Sonraki parola girişinde yeni ana parolanı kullan.", "Tamam");
    }

    private sealed class LockPage : ContentPage
    {
        protected override bool OnBackButtonPressed() => true;
    }
}

internal static class MobileStyle
{
    internal const string LightBackground = "#F5F4ED";
    internal const string DarkBackground = "#262624";
    internal static readonly Color LightBar = Color.FromArgb("#EFEEE6");
    internal static readonly Color DarkBar = Color.FromArgb("#1F1E1D");
    internal static readonly Color LightText = Color.FromArgb("#141413");
    internal static readonly Color DarkText = Color.FromArgb("#F5F4ED");
    internal static readonly Color LightCard = Colors.White;
    internal static readonly Color DarkCard = Color.FromArgb("#30302E");
    internal static readonly Color LightLine = Color.FromArgb("#E3E1D7");
    internal static readonly Color DarkLine = Color.FromArgb("#45443F");

    internal static void Page(ContentPage page) =>
        page.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb(LightBackground), Color.FromArgb(DarkBackground));

    internal static void SetContent(ContentPage page, View content, bool canGoBack = true)
    {
        NavigationPage.SetHasNavigationBar(page, false);
        var heading = Label(page.Title ?? "", 21, true);
        heading.VerticalOptions = LayoutOptions.Center;
        var bar = new Grid { Padding = new Thickness(12, 6), ColumnSpacing = 10, MinimumHeightRequest = 52 };
        bar.SetAppThemeColor(VisualElement.BackgroundColorProperty, LightBar, DarkBar);
        if (canGoBack)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var back = Button("‹", async () => await page.Navigation.PopAsync());
            SemanticProperties.SetDescription(back, "Geri");
            bar.Add(back, 0);
        }
        bar.Add(heading, canGoBack ? 1 : 0);
        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        root.Add(bar);
        root.Add(content, 0, 1);
        page.Content = root;
    }

    internal static Label Label(string text, double size = 14, bool bold = false)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
        label.SetAppThemeColor(Microsoft.Maui.Controls.Label.TextColorProperty, LightText, DarkText);
        return label;
    }

    internal static Button Button(string text, Action? action = null, bool primary = false)
    {
        var button = new Button { Text = text, CornerRadius = 6, MinimumHeightRequest = 44, Padding = new Thickness(12, 6) };
        button.SetAppThemeColor(VisualElement.BackgroundColorProperty,
            primary ? Color.FromArgb("#C96442") : LightCard, primary ? Color.FromArgb("#D97757") : DarkCard);
        button.SetAppThemeColor(Microsoft.Maui.Controls.Button.TextColorProperty, LightText, primary ? DarkBar : DarkText);
        button.SetAppThemeColor(Microsoft.Maui.Controls.Button.BorderColorProperty, LightLine, DarkLine);
        button.BorderWidth = 1;
        if (action is not null) button.Clicked += (_, _) => action();
        return button;
    }

    internal static Entry Entry(string placeholder)
    {
        var input = new Entry { Placeholder = placeholder, MinimumHeightRequest = 44 };
        input.SetAppThemeColor(InputView.TextColorProperty, LightText, DarkText);
        input.SetAppThemeColor(InputView.PlaceholderColorProperty, Color.FromArgb("#6B6A64"), Color.FromArgb("#A8A69C"));
        input.SetAppThemeColor(VisualElement.BackgroundColorProperty, LightCard, DarkCard);
        return input;
    }

    internal static Image Picture(byte[] bytes) => new()
    {
        Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false)),
        Aspect = Aspect.AspectFit
    };
}
