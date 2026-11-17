using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZXing;
using FormatException = System.FormatException;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private Window? authenticatorWindow;
    private static readonly HttpClient ClockClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private IEnumerable<OtpAccount> FindAuthenticator(string query, long? group = null)
    {
        if (!Features.Enabled(Feature.Authenticator)) return [];
        var groups = store.Groups().ToDictionary(item => item.Id, item => item.Name);
        return store.AuthenticatorAccounts().Where(account =>
            (group == null || account.Group == group) &&
            NoteStore.Fold($"{account.Issuer} {account.Name} {groups.GetValueOrDefault(account.Group ?? 0)}")
                .Contains(NoteStore.Fold(query.Trim()), StringComparison.Ordinal));
    }

    internal void OpenAuthenticator() => OpenAuthenticator(null, null);

    internal void OpenAuthenticator(BitmapSource? picture, Guid? note)
    {
        if (!Features.Enabled(Feature.Authenticator)) return;
        if (authenticatorWindow != null && picture == null)
        {
            authenticatorWindow.Activate();
            return;
        }
        authenticatorWindow?.Close();
        var window = Owned("Doğrulayıcı", 820, 580);
        authenticatorWindow = window;
        window.Closed += (_, _) => authenticatorWindow = null;
        var root = new DockPanel { Margin = new Thickness(24) };
        window.Content = root;

        var controls = new StackPanel();
        Add(root, controls, Dock.Top);
        if (store.Setting("otpNotice") != "1")
        {
            controls.Children.Add(new TextBlock { Text = "Şifre ipuçları ile 2FA kodlarını aynı yerde tutmak 2FA’nın korumasını azaltır. Kritik hesaplar için ayrı bir cihaz önerilir.", TextWrapping = TextWrapping.Wrap });
            store.Set("otpNotice", "1");
        }
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        controls.Children.Add(status);
        var query = new TextBox();
        System.Windows.Automation.AutomationProperties.SetName(query, "Yayıncı, hesap veya firma ara");
        controls.Children.Add(new TextBlock { Text = "Hesap veya firma ara", Margin = new Thickness(0,8,0,4) });
        controls.Children.Add(query);
        var hide = new CheckBox { Content = "Kodları gizle", IsChecked = store.Setting("otpHide") == "1" };
        var hello = new CheckBox { Content = "Kod göstermek için Windows Hello iste", IsChecked = store.Setting("otpHello") == "1" };

        var group = NewNoteGroup;
        var migration = new AuthenticatorMigration();
        var rows = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var refreshCodes = new List<Action>();
        var header = AccountColumns("Hizmet", "Hesap", "Grup", "Kod", "Süre");
        header.Margin = new Thickness(8, 4, 24, 4);
        header.SetValue(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.SemiBold);
        Add(root, header, Dock.Top);
        root.Children.Add(rows);
        Guid? revealed = null;
        OtpAccount? Selected() => (rows.SelectedItem as ListBoxItem)?.Tag as OtpAccount;

        async Task Copy(OtpAccount account)
        {
            if (!await CopyAuthenticator(account, window)) return;
            if (account.Counter != null)
            {
                ScheduleCloudAfterEdit();
                Reload();
            }
            status.Text = "Kod kopyalandı.";
        }
        async Task Show(OtpAccount account)
        {
            if (hello.IsChecked == true && !await VerifyAuthenticatorHello(window)) return;
            revealed = account.Id;
            foreach (var refresh in refreshCodes) refresh();
        }
        void Edit(OtpAccount account)
        {
            EditAuthenticator(window, account);
            ScheduleCloudAfterEdit();
            Reload();
        }
        void Remove(OtpAccount account)
        {
            if (RestoreInProgress || exiting) return;
            try { store.RemoveAuthenticatorAccount(account.Id); }
            catch (Microsoft.Data.Sqlite.SqliteException) { status.Text = "Hesap kaldırılamadı."; return; }
            if (revealed == account.Id) revealed = null;
            ScheduleCloudAfterEdit();
            Reload();
            status.Text = "Hesap kaldırıldı.";
        }
        void AddAccount()
        {
            Edit(new(Guid.NewGuid(), "", "", "", "SHA1", 6, 30, null, group, null, note));
        }

        void Reload()
        {
            var selected = Selected()?.Id;
            rows.Items.Clear();
            refreshCodes.Clear();
            var accounts = FindAuthenticator(query.Text, group).ToArray();
            var groupNames = store.Groups().ToDictionary(entry => entry.Id, entry => entry.Name);
            if (accounts.Length == 0)
                rows.Items.Add(new ListBoxItem
                {
                    IsEnabled = false,
                    Content = query.Text.Length == 0 ? "Henüz hesap yok. QR kodu ekleyerek başlayın." : "Eşleşen hesap bulunamadı."
                });
            foreach (var account in accounts)
            {
                var cells = AccountColumns(account.Issuer, account.Name,
                    groupNames.GetValueOrDefault(account.Group ?? 0, "Grupsuz"), "", "");
                var code = (TextBlock)cells.Children[3];
                code.FontFamily = new FontFamily("Consolas");
                var remainingLabel = (TextBlock)cells.Children[4];
                var item = new ListBoxItem { Tag = account, Content = cells, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                System.Windows.Automation.AutomationProperties.SetName(item,
                    string.IsNullOrEmpty(account.Issuer) ? account.Name : $"{account.Issuer} · {account.Name}");
                item.MouseDoubleClick += (_, _) => Edit(account);
                item.PreviewMouseRightButtonDown += (_, _) => item.IsSelected = true;
                refreshCodes.Add(() =>
                {
                    var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var visible = revealed == account.Id || (hide.IsChecked != true && hello.IsChecked != true);
                    code.Text = visible ? DisplayCode(Authenticator.Code(account, time)) : "••• •••";
                    remainingLabel.Text = account.Counter == null ? $"{account.Period - time % account.Period} sn" : "HOTP";
                });
                var menu = new ContextMenu();
                Menu(menu, "Kodu kopyala · Ctrl+T", async () => await Copy(account));
                Menu(menu, "Kodu göster · Ctrl+Shift+T", async () => await Show(account));
                Menu(menu, "Düzenle · Enter", () => Edit(account));
                Menu(menu, "QR olarak göster", () => ExportAuthenticator(window, account));
                Menu(menu, "Açık nota bağla", () =>
                {
                    if (!Guid.TryParse((tabs.SelectedContent as EditorPane)?.Note.Uuid, out var id)) return;
                    store.SaveAuthenticatorAccounts([account with { Note = id }]);
                    ScheduleCloudAfterEdit();
                    Notify("Hesap açık nota bağlandı.");
                });
                Menu(menu, "Hesabı kaldır · Del", () => Remove(account));
                item.ContextMenu = menu;
                rows.Items.Add(item);
                if (account.Id == selected) rows.SelectedItem = item;
            }
            if (rows.SelectedItem == null && accounts.Length > 0) rows.SelectedIndex = 0;
            foreach (var refresh in refreshCodes) refresh();
        }

        static Grid AccountColumns(params string[] values)
        {
            var grid = new Grid();
            foreach (var weight in new[] { 2d, 2.5d, 1.55d, 1.25d, .8d })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
            for (var i = 0; i < values.Length; i++)
            {
                var field = new TextBlock
                {
                    Text = values[i], TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3)
                };
                Grid.SetColumn(field, i);
                grid.Children.Add(field);
            }
            return grid;
        }

        void Import(Func<string?> read)
        {
            if (!store.IsEncrypted || RestoreInProgress || exiting) return;
            try
            {
                var value = read();
                if (string.IsNullOrWhiteSpace(value)) { status.Text = "Doğrulayıcı QR kodu bulunamadı."; return; }
                var accounts = migration.Import(value);
                if (accounts == null) { status.Text = "Aktarımın kalan QR kodlarını okutun."; return; }
                store.SaveAuthenticatorAccounts(accounts.Select(account => account with { Group = group, Note = note }));
                ScheduleCloudAfterEdit();
                status.Text = $"{accounts.Length} hesap eklendi.";
                Reload();
            }
            catch (Exception error) when (error is FormatException or OverflowException or IOException or ArgumentException
                or NotSupportedException or System.Runtime.InteropServices.ExternalException or Microsoft.Data.Sqlite.SqliteException)
            { status.Text = "Doğrulayıcı verisi okunamadı."; }
        }

        var toolbar = new WrapPanel { Margin = new Thickness(0,8,0,12) };
        controls.Children.Add(toolbar);
        var importButton = Button("＋ QR veya bağlantı ekle", () => Import(() =>
            ClipboardPictures.Read() is { } image ? ReadAuthenticatorQr(image) : Clipboard.GetText()));
        importButton.SetResourceReference(StyleProperty, "PrimaryButton");
        SettingsAction(toolbar, importButton);
        SettingsAction(toolbar, Button("＋ Elle hesap ekle", AddAccount));
        SettingsAction(toolbar, Button("Kodu kopyala", async () => { if (Selected() is { } account) await Copy(account); }));
        SettingsAction(toolbar, Button("Düzenle", () => { if (Selected() is { } account) Edit(account); }));
        importButton.Margin = new Thickness(0, 8, 8, 0);
        var options = new Expander { Header = "Ayarlar", Margin = new Thickness(0,0,0,12) };
        options.SetResourceReference(ForegroundProperty, "InkBrush");
        var preferences = new StackPanel { Margin = new Thickness(0,8,0,0) };
        hide.SetResourceReference(StyleProperty, "SettingsSwitch");
        preferences.Children.Add(hide);
        hello.SetResourceReference(StyleProperty, "SettingsSwitch");
        preferences.Children.Add(hello);
        options.Content = preferences;
        controls.Children.Add(options);
        window.AllowDrop = true;
        window.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var file in files)
                    Import(() => new FileInfo(file).Length <= 20 * 1024 * 1024 ? ReadAuthenticatorQr(Pictures.Decode(File.ReadAllBytes(file))) : null);
        };
        window.PreviewKeyDown += async (_, e) =>
        {
            var modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.Control && e.Key == Key.N)
            { e.Handled = true; AddAccount(); }
            else if (modifiers == ModifierKeys.Control && e.Key == Key.F)
            { e.Handled = true; query.Focus(); query.SelectAll(); }
            else if (modifiers == ModifierKeys.Control && e.Key == Key.V && !query.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                Import(() => ClipboardPictures.Read() is { } image ? ReadAuthenticatorQr(image) : Clipboard.GetText());
            }
            else if (Selected() is { } account)
            {
                if (e.Key == Key.T && modifiers == ModifierKeys.Control)
                { e.Handled = true; await Copy(account); }
                else if (e.Key == Key.T && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                { e.Handled = true; await Show(account); }
                else if (e.Key == Key.Enter && modifiers == ModifierKeys.None && rows.IsKeyboardFocusWithin)
                { e.Handled = true; Edit(account); }
                else if (e.Key == Key.Delete && modifiers == ModifierKeys.None && rows.IsKeyboardFocusWithin)
                { e.Handled = true; Remove(account); }
            }
        };
        query.TextChanged += (_, _) => Reload();
        rows.SelectionChanged += (_, _) =>
        {
            if (Selected()?.Id == revealed) return;
            revealed = null;
            foreach (var refresh in refreshCodes) refresh();
        };
        hide.Click += (_, _) => { revealed = null; store.Set("otpHide", hide.IsChecked == true ? "1" : "0"); Reload(); };
        hello.Click += async (_, _) =>
        {
            if (hello.IsChecked != true && store.Setting("otpHello") == "1" && !await VerifyAuthenticatorHello(window))
                hello.IsChecked = true;
            if (hello.IsChecked == true && !WindowsHelloUnlock.Configured)
            {
                status.Text = "Önce Güvenlik ayarlarından Windows Hello’yu etkinleştirin.";
                hello.IsChecked = false;
            }
            revealed = null;
            store.Set("otpHello", hello.IsChecked == true ? "1" : "0");
            Reload();
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => { foreach (var refresh in refreshCodes) refresh(); };
        window.Closed += (_, _) => timer.Stop();
        Reload();
        timer.Start();
        window.Show();
        if (picture != null) Import(() => ReadAuthenticatorQr(picture));
    }

    private async Task<bool> VerifyAuthenticatorHello(Window owner)
    {
        try { return store.MatchesOpenPassword(await WindowsHelloUnlock.Unlock(owner)); }
        catch (Exception error) when (error is IOException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        { Notify("Windows Hello doğrulaması tamamlanamadı."); return false; }
    }

    private async Task<bool> CopyAuthenticator(OtpAccount account, Window owner)
    {
        if (!Features.Enabled(Feature.Authenticator) || RestoreInProgress || exiting) return false;
        if (store.Setting("otpHello") == "1" && !await VerifyAuthenticatorHello(owner)) return false;
        if (store.AuthenticatorAccounts().FirstOrDefault(item => item.Id == account.Id) is not { } current) return false;
        try
        {
            if (current.Counter == long.MaxValue) { Notify("HOTP sayacı üst sınıra ulaştı."); return false; }
            var code = Authenticator.Code(current, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Clipboard.SetText(code);
            var sequence = ClipboardSequence();
            _ = ClearCode(code, sequence);
            if (current.Counter is long counter)
                store.SaveAuthenticatorAccounts([current with { Counter = counter + 1 }]);
            return true;
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { Notify("HOTP sayacı kaydedilemedi."); return false; }
        catch (System.Runtime.InteropServices.ExternalException) { Notify("Pano kullanılamadı."); return false; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
    private static extern uint ClipboardSequence();

    private static async Task ClearCode(string code, uint sequence)
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        try { if (ClipboardSequence() == sequence && Clipboard.GetText() == code) Clipboard.Clear(); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    private static string DisplayCode(string code) => code.Insert(code.Length / 2, " ");

    internal void RefreshAuthenticatorBadge(Button badge, Note note)
    {
        badge.Visibility = Features.Enabled(Feature.Authenticator) && !RestoreInProgress && !exiting &&
            Guid.TryParse(note.Uuid, out var id) && store.HasAuthenticatorForNote(id)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    internal async Task CopyNoteAuthenticator(Note note)
    {
        if (!Guid.TryParse(note.Uuid, out var id)) return;
        var matches = FindAuthenticator("").Where(account => account.Note == id).ToArray();
        if (matches.Length == 1 && await CopyAuthenticator(matches[0], this)) Notify("Kopyalandı");
        else if (matches.Length > 1)
        {
            var menu = new ContextMenu();
            foreach (var account in matches)
                Menu(menu, $"{account.Issuer} · {account.Name}", async () => { if (await CopyAuthenticator(account, this)) Notify("Kopyalandı"); });
            menu.IsOpen = true;
        }
    }

    private static string? ReadAuthenticatorQr(BitmapSource image)
    {
        if ((long)image.PixelWidth * image.PixelHeight > 40_000_000) return null;
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = pixels.PixelWidth * 4;
        var bytes = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(bytes, stride, 0);
        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        reader.Options.TryHarder = true;
        return reader.Decode(bytes, pixels.PixelWidth, pixels.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32)?.Text;
    }

    private void EditAuthenticator(Window owner, OtpAccount account)
    {
        var window = Owned("Doğrulayıcı hesabı", 450, 590);
        window.Owner = owner;
        var form = new StackPanel { Margin = new Thickness(14) };
        window.Content = new ScrollViewer { Content = form };
        void Label(string text) => form.Children.Add(new TextBlock { Text = text });
        TextBox Field(string label, string value)
        {
            Label(label);
            var field = new TextBox { Text = value };
            form.Children.Add(field);
            return field;
        }
        var issuer = Field("Yayıncı", account.Issuer);
        var name = Field("Hesap", account.Name);
        Label("Base32 gizli anahtarı");
        var secret = new PasswordBox { Password = account.Secret };
        form.Children.Add(secret);
        var algorithm = Field("Algoritma (SHA1, SHA256, SHA512)", account.Algorithm);
        var digits = Field("Hane (6 veya 8)", account.Digits.ToString());
        var period = Field("Süre (30 veya 60 sn)", account.Period.ToString());
        var counter = Field("HOTP sayacı (TOTP için boş)", account.Counter?.ToString() ?? "");
        var color = Field("Renk (isteğe bağlı)", account.Color ?? "");
        Label("Firma");
        var available = store.Groups();
        var groups = new ComboBox { ItemsSource = new[] { "Grupsuz" }.Concat(available.Select(item => item.Name)) };
        groups.SelectedIndex = Array.FindIndex(available.ToArray(), item => item.Id == account.Group) + 1;
        form.Children.Add(groups);
        var result = new TextBlock();
        form.Children.Add(result);
        form.Children.Add(Button("Hesabı kaydet", () =>
        {
            if (!store.IsEncrypted || RestoreInProgress || exiting) return;
            try
            {
                store.SaveAuthenticatorAccounts([account with
                {
                    Issuer = issuer.Text.Trim(), Name = name.Text.Trim(), Secret = secret.Password,
                    Algorithm = algorithm.Text.Trim().ToUpperInvariant(), Digits = int.Parse(digits.Text), Period = int.Parse(period.Text),
                    Counter = string.IsNullOrWhiteSpace(counter.Text) ? null : long.Parse(counter.Text),
                    Group = groups.SelectedIndex > 0 ? available[groups.SelectedIndex - 1].Id : null,
                    Color = string.IsNullOrWhiteSpace(color.Text) ? null : color.Text.Trim()
                }]);
                window.Close();
            }
            catch (Exception error) when (error is FormatException or OverflowException or Microsoft.Data.Sqlite.SqliteException)
            { result.Text = "Hesap alanlarını ve gizli anahtarı kontrol edin."; }
        }));
        window.ShowDialog();
    }

    private void ExportAuthenticator(Window owner, OtpAccount account)
    {
        if (!store.IsEncrypted || !store.MatchesOpenPassword(SecurityPrompt.Ask(owner, "Hesabı dışa aktar", "Ana parola", false) ?? "")) return;
        var qr = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new ZXing.Common.EncodingOptions { Width = 380, Height = 380, Margin = 3 } }
            .Write(Authenticator.Export(account));
        var image = BitmapSource.Create(qr.Width, qr.Height, 96, 96, PixelFormats.Bgra32, null, qr.Pixels, qr.Width * 4);
        var window = Owned("Hesap QR kodu · 60 saniye", 440, 470);
        window.Owner = owner;
        window.Content = new Image { Source = image, Margin = new Thickness(12) };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += (_, _) => window.Close();
        window.Closed += (_, _) => { timer.Stop(); window.Content = null; };
        timer.Start();
        window.ShowDialog();
    }

    internal async Task CheckAuthenticatorClock()
    {
        if (!Features.Enabled(Feature.Authenticator)) return;
        try
        {
            var before = DateTimeOffset.UtcNow;
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://github.com/");
            using var response = await ClockClient.SendAsync(request);
            var after = DateTimeOffset.UtcNow;
            if (response.IsSuccessStatusCode && response.Headers.Date is { } server && !exiting &&
                Math.Abs((server - (before + (after - before) / 2)).TotalSeconds) is > 30 and var difference)
                Notify($"Bilgisayar saati {difference:F0} sn kaymış, kodlar hatalı olabilir.");
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
    }
}
