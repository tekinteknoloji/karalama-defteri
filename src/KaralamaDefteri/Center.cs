using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class Center : Window
{
    private readonly NoteStore store;
    private readonly TabControl tabs = new();
    private readonly Dictionary<long, EditorPane> editors = [];
    private readonly TextBox search = new() { MinWidth = 150, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox scope = new() { ItemsSource = new[] { "Notlar", "Arşiv", "Çöp kutusu" }, SelectedIndex = 0 };
    private readonly ComboBox color = new() { ItemsSource = new[] { "Tüm renkler", "Sarı", "Yeşil", "Mavi", "Pembe" }, SelectedIndex = 0 };
    internal static readonly string?[] Colors = [null, "#FFF475", "#A7FFEB", "#AECBFA", "#FDCFE8"];
    internal static readonly string[] ColorNames = ["Varsayılan", "Sarı", "Yeşil", "Mavi", "Pembe"];
    internal static readonly string[] ColorBrushes = ["SurfaceBrush", "YellowBrush", "GreenBrush", "BlueBrush", "PinkBrush"];
    private readonly WrapPanel pinned = new();
    private readonly WrapPanel regular = new();
    private readonly Button viewToggle = Button("Liste");
    private bool listView;
    private readonly TextBlock pinnedLabel = new() { Text = "Sabitlenenler", FontWeight = FontWeights.SemiBold, Margin = new Thickness(5) };
    private readonly TextBlock message = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button undoButton = Button("Geri al");
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer searchTimer;
    private readonly DispatcherTimer undoTimer;
    private Action? undo;
    private bool exiting;
    private bool restoring;
    private string theme;
    internal double EditorSize { get; private set; }
    internal bool EditorWrap { get; private set; }
    internal string EditorFont { get; private set; }

    public Center(NoteStore store)
    {
        this.store = store;
        Features = new FeatureFlags(store);
        Title = "Karalama Defteri";
        Width = 1320; Height = 780; MinWidth = 760; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        LoadBounds();
        Theme.Configure(this);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;
        theme = store.Setting("theme") ?? "Sistem";
        EditorSize = double.TryParse(store.Setting("size"), System.Globalization.CultureInfo.InvariantCulture, out var size) ? Math.Clamp(size, 9, 40) : 15;
        EditorWrap = store.Setting("wrap") != "0";
        EditorFont = store.Setting("font") ?? (Fonts.SystemFontFamilies.Any(font => font.Source == "Cascadia Mono") ? "Cascadia Mono" : "Consolas");
        saveTimer = Timer(500, () => Flush());
        searchTimer = Timer(150, () => { Flush(refresh: false); RefreshCards(); });
        undoTimer = Timer(5000, () => Notify(""));
        var root = new DockPanel { Margin = new Thickness(14) };
        Content = root;
        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 480 });
        for (var i = 0; i < 5; i++) toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var toolbarSurface = new Border { Child = toolbar, CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10) };
        toolbarSurface.SetResourceReference(Border.BackgroundProperty, "BarBrush");
        Add(root, toolbarSurface, Dock.Top);
        var newNote = Button("Yeni not", NewNote);
        newNote.SetResourceReference(StyleProperty, "PrimaryButton");
        AddToolbar(newNote, 0);
        var more = Button("…");
        AutomationProperties.SetName(more, "Diğer işlemler");
        var moreMenu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        Menu(moreMenu, "Panodan not", FromClipboard);
        Menu(moreMenu, "Komut paleti · Ctrl+K", OpenPalette);
        var viewMenu = new MenuItem { Header = "Görünüm" };
        Menu(viewMenu, "Pano", OpenBoard);
        var boardMenu = (MenuItem)viewMenu.Items[viewMenu.Items.Count - 1];
        viewMenu.SubmenuOpened += (_, _) => boardMenu.Visibility =
            Features.Enabled(Feature.Board) ? Visibility.Visible : Visibility.Collapsed;
        var separateGroups = new MenuItem { Header = "Gruplara ayır", IsCheckable = true };
        separateGroups.Click += (_, _) => SetSplitGroups(separateGroups.IsChecked);
        viewMenu.Items.Add(separateGroups);
        moreMenu.Items.Add(viewMenu);
        moreMenu.Opened += (_, _) => separateGroups.IsChecked = splitGroups;
        Menu(moreMenu, "Ayarlar", OpenSettings);
        Menu(moreMenu, "Hakkında", OpenAbout);
        more.Click += (_, _) => moreMenu.IsOpen = true;
        AddToolbar(more, 6);
        color.MinWidth = 108;
        color.Margin = new Thickness(2, 0, 0, 0);
        AddToolbar(color, 4);
        color.ToolTip = "Renge göre göster";
        AutomationProperties.SetName(color, "Renge göre göster");
        search.ToolTip = "Notlarda ara";
        search.SetValue(AutomationProperties.NameProperty, "Notlarda ara");
        AddToolbar(clearTag, 2);
        clearTag.ToolTip = "Etiket filtresini kaldır";
        AutomationProperties.SetName(clearTag, "Etiket filtresini kaldır");
        clearTag.Click += (_, _) => { search.Clear(); search.Focus(); };
        AddToolbar(search, 1);
        AddToolbar(tagsToggle, 3);
        tagsToggle.Click += (_, _) =>
        {
            if (tagPanel == null) OpenTags();
            else CloseTags();
        };
        AddToolbar(viewToggle, 5);
        viewToggle.Click += (_, _) => SetCardView(!listView && !ShowingBoard);

        void AddToolbar(UIElement child, int column)
        {
            Grid.SetColumn(child, column);
            toolbar.Children.Add(child);
        }

        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        Add(footer, Brand("Tekin Teknoloji"), Dock.Right);
        var version = new Hyperlink(new Run(BuildInfo.Summary)) { ToolTip = "Hakkında" };
        version.SetResourceReference(TextElement.ForegroundProperty, "MutedBrush");
        version.Click += (_, _) => OpenAbout();
        var versionLabel = new TextBlock { Margin = new Thickness(6), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        versionLabel.Inlines.Add(version);
        versionLabel.Inlines.Add(new Run(store.IsEncrypted ? "  🔒 Şifreli" : "  🔓 Şifresiz"));
        Add(footer, versionLabel, Dock.Left);
        Add(footer, updateBadge, Dock.Left);
        syncIndicator = new TextBlock { Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center };
        Add(footer, syncIndicator, Dock.Left);
        Add(footer, cloudIndicator, Dock.Left);
        var status = new StackPanel { Orientation = Orientation.Horizontal };
        status.Children.Add(message);
        status.Children.Add(undoButton);
        undoButton.Visibility = Visibility.Collapsed;
        undoButton.Click += (_, _) => { var action = undo; Notify(""); action?.Invoke(); };
        footer.Children.Add(status);
        Add(root, footer, Dock.Bottom);
        Add(root, supportReminder, Dock.Bottom);
        Add(root, BuildSidebar(), Dock.Left);

        var body = cardBody = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380), MinWidth = 240 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        var cards = new StackPanel();
        cards.Children.Add(pinnedLabel);
        cards.Children.Add(pinned);
        cards.Children.Add(regular);
        var scroll = cardScroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(scroll);
        var splitter = cardSplitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);
        Grid.SetColumn(tabs, 2);
        body.Children.Add(tabs);
        Grid.SetColumnSpan(boardView, 2);
        body.Children.Add(boardView);
        root.Children.Add(body);

        search.TextChanged += (_, _) =>
        {
            clearTag.Visibility = Equals(search.Text, clearTag.Tag) ? Visibility.Visible : Visibility.Collapsed;
            Restart(searchTimer);
        };
        SetupGroupSuggestions(search);
        search.KeyDown += (_, e) => { if (e.Key == Key.Enter) OpenSearchResult(search.Text, scope.SelectedIndex, Colors[Math.Max(0, color.SelectedIndex)]); };
        scope.SelectionChanged += (_, _) => RefreshCards();
        color.SelectionChanged += (_, _) => RefreshCards();
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs) return;
            UpdateWorkspaceLayout(ShowingBoard);
            if (!restoring) Flush(refresh: false);
            if (ShowingBoard && !restoring) RefreshCards();
        };
        PreviewKeyDown += Shortcuts;
        Deactivated += (_, _) => Flush();
        Closing += OnClosing;
        IsVisibleChanged += (_, _) => { if (!IsVisible && !exiting) Flush(); };
        LocationChanged += (_, _) => ScheduleSave();
        SizeChanged += (_, _) => ScheduleSave();
        SystemParameters.StaticPropertyChanged += SystemThemeChanged;
        ApplyTheme();
        RestoreSession();
        RefreshCards();
        Loaded += (_, _) =>
        {
            if (tabs.SelectedContent is EditorPane pane) pane.FocusText("");
        };
    }

    internal static Button Button(string text, Action? action = null)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(3, 0, 3, 0) };
        if (action != null) button.Click += (_, _) => action();
        return button;
    }

    private static void Add(DockPanel panel, UIElement child, Dock dock)
    {
        DockPanel.SetDock(child, dock);
        panel.Children.Add(child);
    }
    private DispatcherTimer Timer(int ms, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        return timer;
    }
    private static void Restart(DispatcherTimer timer) { timer.Stop(); timer.Start(); }

    internal void ScheduleSave()
    {
        // Keep a deadline during continuous typing, even when no typing pause occurs.
        if (!saveTimer.IsEnabled) saveTimer.Start();
    }

    internal bool Flush(bool refresh = true)
    {
        if (restoring || exiting) return true;
        saveTimer.Stop();
        var changed = false;
        try
        {
            foreach (var editor in editors.Values)
            {
                if (!editor.Note.Dirty) continue;
                editor.ReadContent();
                store.SaveWithPictures(editor.Note);
                editor.UpdateStatus();
                changed = true;
            }
            SaveSession();
            SaveBounds();
            if (changed && syncPassword != null && syncAfterSave != null) Restart(syncAfterSave);
            if (changed) ScheduleCloudAfterEdit();
            if (changed && refresh && !cloudWriting) RefreshCards();
            else if (changed && refresh) cloudCardsChanged = true;
            return true;
        }
        catch (SqliteException error)
        {
            Notify($"Kayıt başarısız · {error.Message}");
            ScheduleSave();
            return false;
        }
    }

    private sealed record Session(long[] Tabs, long Selected, Dictionary<long, int> Carets);
    private string? lastSession;
    private string? lastBounds;
    private void SaveBounds()
    {
        var bounds = RestoreBoundsValue();
        if (!bounds.All(double.IsFinite) || bounds[2] < MinWidth || bounds[3] < MinHeight) return;
        var json = JsonSerializer.Serialize(bounds);
        if (json == lastBounds) return;
        store.Set("bounds", json);
        lastBounds = json;
    }

    private double[] RestoreBoundsValue() => WindowState == WindowState.Normal
        ? [Left, Top, ActualWidth, ActualHeight]
        : [base.RestoreBounds.Left, base.RestoreBounds.Top, base.RestoreBounds.Width, base.RestoreBounds.Height];

    private void LoadBounds()
    {
        if (store.Setting("bounds") is not { } json) return;
        try
        {
            if (JsonSerializer.Deserialize<double[]>(json) is not [var x, var y, var width, var height]) return;
            var area = SystemParameters.WorkArea;
            Width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, area.Width));
            Height = Math.Clamp(height, MinHeight, Math.Max(MinHeight, area.Height));
            if (x + 80 < SystemParameters.VirtualScreenLeft || y + 40 < SystemParameters.VirtualScreenTop ||
                x + 80 > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ||
                y + 40 > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x;
            Top = y;
        }
        catch (JsonException) { }
    }
    private void SaveSession()
    {
        var session = new Session(tabs.Items.Cast<TabItem>().Select(tab => (long)tab.Tag).ToArray(),
            (tabs.SelectedItem as TabItem)?.Tag as long? ?? 0, editors.ToDictionary(pair => pair.Key, pair => pair.Value.Offset));
        var json = JsonSerializer.Serialize(session);
        if (json == lastSession) return;
        store.Set("session", json);
        lastSession = json;
    }

    private void RestoreSession()
    {
        restoring = true;
        try
        {
            if (store.Setting("session") is not { } json) return;
            if (JsonSerializer.Deserialize<Session>(json) is not { } session) return;
            foreach (var id in session.Tabs)
                if (store.Get(id) is { Deleted: null } note)
                {
                    note.Caret = session.Carets.GetValueOrDefault(id);
                    Open(note, focus: false);
                }
            if (editors.TryGetValue(session.Selected, out var selected)) tabs.SelectedItem = selected.Tab;
        }
        catch (JsonException) { }
        finally { restoring = false; }
    }

    public void Reveal()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void NewNote() => Create("");
    public void FromClipboard()
    {
        try
        {
            if (ClipboardPictures.Read() is { } bitmap) CreatePictureNote(Pictures.FromClipboard(bitmap, CompressionThreshold));
            else if (Clipboard.ContainsText()) Create(Clipboard.GetText());
            else { Reveal(); Notify("Panoda metin veya resim bulunamadı."); }
        }
        catch (System.Runtime.InteropServices.ExternalException) { Reveal(); Notify("Pano şu anda başka bir uygulama tarafından kullanılıyor."); }
        catch (Exception error) when (error is SqliteException or IOException or NotSupportedException)
        { Reveal(); Notify($"Panodan not oluşturulamadı · {error.Message}"); }
    }
    private void Create(string text)
    {
        Reveal();
        try { Open(store.Create(text, NewNoteGroup)); RefreshCards(); }
        catch (SqliteException error) { Notify($"Not oluşturulamadı · {error.Message}"); }
    }

    private void Open(Note note, bool focus = true, string query = "")
    {
        if (!editors.TryGetValue(note.Id, out var editor))
        {
            editor = new EditorPane(this, note);
            editors.Add(note.Id, editor);
        }
        if (!tabs.Items.Contains(editor.Tab)) tabs.Items.Add(editor.Tab);
        tabs.SelectedItem = editor.Tab;
        if (!restoring) SaveSession();
        if (focus) editor.FocusText(query);
    }

    private void OpenCard(Card card, string query = "")
    {
        if (editors.TryGetValue(card.Id, out var existing)) Open(existing.Note, query: query);
        else if (store.Get(card.Id) is { } note) Open(note, query: query);
    }

    internal void CloseTab(EditorPane editor)
    {
        if (!Flush(false)) return;
        if (desktopWindows.ContainsKey(editor.Note.Id)) tabs.Items.Remove(editor.Tab);
        else
        {
            store.RemoveBlank(editor.Note);
            RemoveTab(editor);
        }
        SaveSession();
        RefreshCards();
    }
    private void RemoveTab(EditorPane editor)
    {
        if (desktopWindows.Remove(editor.Note.Id, out var window)) window.Close();
        editors.Remove(editor.Note.Id);
        tabs.Items.Remove(editor.Tab);
        editor.Stop();
    }

    private void RefreshCards()
    {
        RefreshSupportReminder();
        listView = store.Setting("cardView") == "list";
        viewToggle.Content = listView || ShowingBoard ? "Kartlar" : "Liste";
        viewToggle.ToolTip = listView || ShowingBoard ? "Kart görünümüne geç" : "Liste görünümüne geç";
        AutomationProperties.SetName(viewToggle, (string)viewToggle.ToolTip);
        UpdateTagsToggle();
        pinned.Children.Clear();
        regular.Children.Clear();
        cardOrder.Clear();
        deadlineCards.Clear();
        RefreshSidebar();
        foreach (var editor in editors.Values)
            if (!editor.IsKeyboardFocusWithin || !editor.GroupInputFocused) editor.RefreshGroupBox();
        var cards = store.Search(search.Text, Math.Max(0, scope.SelectedIndex), Colors[Math.Max(0, color.SelectedIndex)],
            group: SelectedGroup, tagFiltersEnabled: Features.Enabled(Feature.Tags), deadlineFiltersEnabled: Features.Enabled(Feature.BoardDeadlines));
        RefreshBoard(cards);
        if (ShowingBoard) return;
        boardLabels = Features.Enabled(Feature.Board)
            ? store.BoardColumns(BoardScope).ToDictionary(column => column.Id, column => column.Name) : [];
        cardPositions = Features.Enabled(Feature.Board) ? store.BoardPositions() : [];
        var singleCard = cards.Count == 1;
        foreach (var card in cards.Where(card => card.Pinned && scope.SelectedIndex == 0))
            pinned.Children.Add(CardView(card, singleCard: singleCard));
        var ordinary = cards.Where(card => !card.Pinned || scope.SelectedIndex != 0);
        if (splitGroups && SelectedGroup == null && scope.SelectedIndex == 0)
        {
            foreach (var section in ordinary.GroupBy(card => card.Group))
            {
                var layout = new StackPanel();
                layout.Children.Add(new TextBlock { Text = section.Key ?? "Grupsuz", FontWeight = FontWeights.SemiBold });
                var grid = new WrapPanel();
                foreach (var card in section) grid.Children.Add(CardView(card, singleCard: singleCard));
                layout.Children.Add(grid);
                regular.Children.Add(layout);
            }
        }
        else foreach (var card in ordinary) regular.Children.Add(CardView(card, singleCard: singleCard));
        pinnedLabel.Visibility = pinned.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (pinned.Children.Count + regular.Children.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(search.Text) || color.SelectedIndex > 0)
                regular.Children.Add(Button("Sonuç yok · Filtreleri temizle", () =>
                {
                    search.Clear();
                    color.SelectedIndex = 0;
                    RefreshCards();
                }));
            else regular.Children.Add(new TextBlock { Text = "Not bulunamadı.", Margin = new Thickness(8) });
        }
    }

    private Border CardView(Card card, bool boardTile = false, bool singleCard = false)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = card.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Height = 105, Margin = new Thickness(0, 5, 0, 5) };
        if (card.Match.Length == 0) preview.Text = card.Preview;
        else Highlight(preview, card.Match);
        panel.Children.Add(preview);
        if (card.Thumbnail is { } thumbnail)
        {
            var image = new Image { Height = 62, Stretch = Stretch.Uniform };
            image.Loaded += (_, _) =>
            {
                if (image.Source != null) return;
                image.Source = ClipboardPictures.Visible(Pictures.Decode(thumbnail));
                if (image.Source == null && store.GetFirstPicture(card.Id) is { } original)
                    image.Source = ClipboardPictures.Visible(Pictures.Decode(original.Bytes));
            };
            panel.Children.Add(image);
            preview.Height = 41;
        }
        var columnName = !boardTile && cardPositions.TryGetValue(card.Id, out var position) && position.Column != null
            ? boardLabels.GetValueOrDefault(position.Column) : null;
        var tags = boardTile && Features.Enabled(Feature.Tags)
            ? string.Join(" ", store.TagNames(store.Get(card.Id)?.Content ?? "").Select(tag => "#" + tag)) : null;
        if (!string.IsNullOrEmpty(tags)) panel.Children.Add(new TextBlock { Text = tags, TextTrimming = TextTrimming.CharacterEllipsis });
        var details = string.Join(" · ", new[] { SelectedGroup == null ? card.Group : null, card.Images > 0 ? $"📎 {card.Images}" : null, Features.Enabled(Feature.Checklist) ? card.Progress : null, columnName }.OfType<string>());
        if (details.Length > 0)
            panel.Children.Add(new TextBlock { Text = details, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock
        {
            Text = DateTimeOffset.Parse(card.Updated).ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            FontSize = 11, Foreground = Theme.Brush("Muted")
        });
        var border = new Border
        {
            Child = panel, Width = listView ? 332 : 166,
            Height = boardTile ? 208 : listView ? 104 : 184, Margin = new Thickness(4), Padding = new Thickness(9),
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = Theme.Brush("Border"),
            Background = NoteBrush(card.Color)
        };
        if (singleCard && !listView) border.MaxWidth = 640;
        if (listView || singleCard) border.Loaded += (_, _) =>
        {
            if (System.Windows.Media.VisualTreeHelper.GetParent(border) is not WrapPanel parent) return;
            var width = new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = parent };
            width.Converter = new ListRowWidth();
            border.SetBinding(WidthProperty, width);
        };
        if (card.Color != null)
            foreach (var label in panel.Children.OfType<TextBlock>()) label.Foreground = Theme.Brush("Ink");
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (!boardTile && e.ClickCount == 2 && scope.SelectedIndex != 2) OpenCard(card, search.Text);
        };
        ApplyDeadlineAppearance(card.Id, border, panel);
        border.ContextMenu = CardMenu(card);
        if (!boardTile) AttachCardDrag(border, card);
        return border;
    }

    internal static void Highlight(TextBlock text, string snippet)
    {
        var marked = false;
        foreach (var part in snippet.Split(['\u0001', '\u0002']))
        {
            var run = new Run(part);
            if (marked)
            {
                run.SetResourceReference(TextElement.BackgroundProperty, "MatchBrush");
                run.SetResourceReference(TextElement.ForegroundProperty, "InkBrush");
                run.FontWeight = FontWeights.Bold;
            }
            text.Inlines.Add(run);
            marked = !marked;
        }
    }

    private ContextMenu CardMenu(Card card)
    {
        var menu = new ContextMenu();
        if (scope.SelectedIndex == 2)
        {
            Menu(menu, "Geri yükle", () => Change(card, n => n.Deleted = null, "Not geri yüklendi."));
            return menu;
        }
        Menu(menu, "Aç", () => OpenCard(card));
        if (Features.Enabled(Feature.BoardDeadlines)) Menu(menu, "Son tarih ve hatırlatma…", () => EditDeadline(card));
        Menu(menu, "Dışa aktar…", () => ExportNote(card.Id));
        if (Features.Enabled(Feature.DesktopPins)) Menu(menu, "Masaüstüne iğnele", () => PinCard(card.Id));
        if (Features.Enabled(Feature.Checklist)) Menu(menu, card.Progress == null ? "Listeye çevir" : "Düz metne çevir", () =>
        {
            OpenCard(card);
            if (editors.TryGetValue(card.Id, out var editor)) editor.ToggleChecklist();
        });
        var move = new MenuItem { Header = "Gruba taşı" };
        move.Items.Add(new MenuItem());
        menu.Items.Add(move);
        move.SubmenuOpened += (_, _) => PopulateMoveMenu(move, card);
        var colors = new MenuItem { Header = "Renk" };
        menu.Items.Add(colors);
        for (var i = 0; i < Colors.Length; i++)
        {
            var chosen = Colors[i];
            Menu(colors, ColorNames[i], () => SetNoteColor(card.Id, chosen));
        }
        Menu(menu, card.Pinned ? "Sabitlemeyi kaldır" : "Sabitle", () => Change(card, n => n.Pinned = !n.Pinned, "Sabitleme değiştirildi."));
        Menu(menu, scope.SelectedIndex == 1 ? "Arşivden çıkar" : "Arşivle", () => Change(card, n => n.Archived = !n.Archived, "Arşiv durumu değiştirildi."));
        Menu(menu, "Sil", () => Change(card, n => n.Deleted = NoteStore.Now(), "Not çöp kutusuna taşındı."));
        return menu;
    }

    private static void Menu(ItemsControl menu, string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    internal void SetNoteColor(long id, string? color) => Change(id, note => note.Color = color, "Renk değiştirildi.");

    private static Brush NoteBrush(string? color) => Theme.Brush(color switch
    {
        "#FFF475" => "Yellow", "#A7FFEB" => "Green", "#AECBFA" => "Blue", "#FDCFE8" => "Pink", _ => "Surface"
    });

    private sealed class ListRowWidth : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is double width && width > 8 ? width - 8 : 332d;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            System.Windows.Data.Binding.DoNothing;
    }

    private void Change(Card card, Action<Note> change, string label) => Change(card.Id, change, label);

    private void Change(long id, Action<Note> change, string label)
    {
        if (!Flush(false)) return;
        var note = editors.TryGetValue(id, out var editor) ? editor.Note : store.Get(id);
        if (note == null) return;
        var before = (note.Color, note.Pinned, note.Archived, note.Deleted);
        change(note);
        try { store.Save(note); }
        catch (SqliteException error)
        {
            (note.Color, note.Pinned, note.Archived, note.Deleted) = before;
            Notify($"İşlem tamamlanamadı · {error.Message}");
            return;
        }
        if (note.Deleted != null && editor != null) { RemoveTab(editor); SaveSession(); }
        editor?.RefreshColorDots();
        if (desktopWindows.TryGetValue(id, out var desktop)) desktop.ApplyPalette();
        RefreshCards();
        Notify(label, () =>
        {
            if (!Flush(false)) return;
            var current = editors.TryGetValue(id, out var open) ? open.Note : store.Get(id);
            if (current == null) return;
            (current.Color, current.Pinned, current.Archived, current.Deleted) = before;
            try { store.Save(current); }
            catch (SqliteException error) { Notify($"Geri alınamadı · {error.Message}"); return; }
            if (current.Deleted != null && open != null) { RemoveTab(open); SaveSession(); }
            open?.RefreshColorDots();
            if (desktopWindows.TryGetValue(id, out var desktop)) desktop.ApplyPalette();
            RefreshCards();
        });
    }

    internal void Notify(string text, Action? reverse = null)
    {
        undoTimer.Stop();
        message.Text = text;
        undo = reverse;
        undoButton.Visibility = reverse == null ? Visibility.Collapsed : Visibility.Visible;
        if (reverse != null) undoTimer.Start();
    }

    private void OpenSearchResult(string query, int selectedScope, string? selectedColor)
    {
        Flush(false);
        if (selectedScope == 2) return;
        if (store.Search(query, selectedScope, selectedColor, 1, SelectedGroup, Features.Enabled(Feature.Tags), Features.Enabled(Feature.BoardDeadlines)).FirstOrDefault() is { } card)
            OpenCard(card, store.ParseGroup(query).Text);
    }

    private Window? palette;
    public void OpenPalette()
    {
        Reveal();
        if (palette != null) { palette.Activate(); return; }
        Flush(false);
        var window = Owned("Komut paleti", 620, 460);
        palette = window;
        var layout = new DockPanel { Margin = new Thickness(12) };
        var input = new TextBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6) };
        SetupGroupSuggestions(input);
        input.SetValue(AutomationProperties.NameProperty, "Not veya komut ara");
        var results = new ListBox();
        Add(layout, input, Dock.Top);
        layout.Children.Add(results);
        window.Content = layout;
        void Update()
        {
            results.Items.Clear();
            if (Features.Enabled(Feature.Authenticator) && input.Text.TrimStart().StartsWith("kod:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var account in FindAuthenticator(input.Text.TrimStart()[4..]).Take(60))
                    results.Items.Add(new ListBoxItem { Content = $"Kod kopyala · {account.Issuer} · {account.Name}",
                        Tag = new PaletteCommand(account.Name, "", async () => { if (await CopyAuthenticator(account, this)) Notify("Kopyalandı"); }) });
                results.SelectedIndex = results.Items.Count == 0 ? -1 : 0;
                return;
            }
            var commandsOnly = input.Text.TrimStart().StartsWith('>');
            var commandQuery = input.Text.TrimStart().TrimStart('>').Trim();
            foreach (var command in PaletteCommands().Where(command => command.Matches(commandQuery)))
                results.Items.Add(new ListBoxItem { Content = command.Title, Tag = command });
            if (!commandsOnly && !string.IsNullOrWhiteSpace(input.Text))
                foreach (var group in store.Groups().Where(group => NoteStore.Fold(group.Name).Contains(NoteStore.Fold(input.Text.TrimStart('@')))))
                    results.Items.Add(new ListBoxItem { Content = $"Grup · {group.Name}", Tag = group });
            foreach (var card in commandsOnly ? [] : store.Search(input.Text, 3, null, 60, tagFiltersEnabled: Features.Enabled(Feature.Tags), deadlineFiltersEnabled: Features.Enabled(Feature.BoardDeadlines)))
            {
                var preview = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .9 };
                if (card.Match.Length == 0) preview.Text = card.Preview.Split('\n')[0];
                else Highlight(preview, card.Match);
                var row = new StackPanel { Margin = new Thickness(4) };
                row.Children.Add(new TextBlock { Text = card.Group == null ? card.Title : $"{card.Title} · {card.Group}", FontWeight = FontWeights.SemiBold });
                row.Children.Add(preview);
                results.Items.Add(new ListBoxItem { Content = row, Tag = card });
            }
            var firstNote = !commandsOnly && !string.IsNullOrWhiteSpace(input.Text)
                ? results.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Tag is Card or NoteGroup) : null;
            if (firstNote != null) results.SelectedItem = firstNote;
            else results.SelectedIndex = results.Items.Count == 0 ? -1 : 0;
        }
        var delay = Timer(150, Update);
        void Select()
        {
            if (delay.IsEnabled) { delay.Stop(); Update(); }
            if ((results.SelectedItem as ListBoxItem)?.Tag is PaletteCommand command)
            {
                window.Close();
                command.Run();
                return;
            }
            if ((results.SelectedItem as ListBoxItem)?.Tag is NoteGroup group)
            {
                window.Close();
                SelectGroup(group.Id, 0);
                return;
            }
            if ((results.SelectedItem as ListBoxItem)?.Tag is not Card card) return;
            window.Close();
            OpenCard(card, store.ParseGroup(input.Text).Text);
        }
        input.TextChanged += (_, _) => Restart(delay);
        window.PreviewKeyDown += (_, e) =>
        {
            if (input.IsKeyboardFocused && GroupSuggestionsOpen(input)) return;
            switch (e.Key)
            {
                case Key.Escape: window.Close(); break;
                case Key.Enter: Select(); break;
                case Key.Down:
                case Key.Up:
                    if (results.Items.Count > 0)
                    {
                        results.SelectedIndex = Math.Clamp(results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, results.Items.Count - 1);
                        results.ScrollIntoView(results.SelectedItem);
                    }
                    break;
                default: return;
            }
            e.Handled = true;
        };
        results.MouseDoubleClick += (_, _) => Select();
        window.Closed += (_, _) => { delay.Stop(); palette = null; };
        Update();
        window.Show();
        input.Focus();
    }

    private Window Owned(string title, double width, double height)
    {
        var window = new Window
        {
            Owner = this, Title = title, Width = width, Height = height, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontSize = FontSize, FontFamily = FontFamily
        };
        Theme.Configure(window);
        return window;
    }

    private Window? settings;
    private ListBox? settingsNavigation;
    public void OpenSettings()
    {
        Reveal();
        if (settings != null) { settings.Activate(); return; }
        var window = Owned("Ayarlar", 710, 580);
        settings = window;
        window.Closed += (_, _) => { settings = null; settingsNavigation = null; };
        var layout = new StackPanel();
        var pages = new Func<UIElement>[] { () => layout, () => ShortcutGuide.Create(Features), () => TransferSettings(window),
            () => SecuritySettings(window), () => SyncSettings(window), UpdateSettings, AboutSettings, FeatureSettings };
        var cachedPages = new UIElement?[pages.Length];
        var navigation = settingsNavigation = new ListBox { Width = 166, Margin = new Thickness(0, 0, 10, 0),
            ItemsSource = new[] { "Genel", "Kısayollar", "Yedek ve aktarım", "Güvenlik", "Senkron", "Güncellemeler", "Hakkında", "Özellikler" } };
        AutomationProperties.SetName(navigation, "Ayarlar bölümleri");
        var page = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var pageTitle = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,16) };
        navigation.SelectionChanged += (_, _) =>
        {
            if (navigation.SelectedIndex is var index && index >= 0)
            {
                pageTitle.Text = (string)navigation.SelectedItem;
                page.Content = index is 1 or 4 or 7 ? pages[index]() : cachedPages[index] ??= pages[index]();
            }
        };
        var settingsLayout = new DockPanel { Margin = new Thickness(24) };
        Add(settingsLayout, navigation, Dock.Left);
        var content = new DockPanel { Margin = new Thickness(14,0,0,0) };
        Add(content, pageTitle, Dock.Top);
        content.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        settingsLayout.Children.Add(content);
        navigation.SelectedIndex = 0;
        window.Content = settingsLayout;
        var themes = new ComboBox { ItemsSource = new[] { "Sistem", "Açık", "Koyu" }, SelectedItem = theme };
        var fonts = new ComboBox { ItemsSource = Fonts.SystemFontFamilies.Select(font => font.Source).OrderBy(name => name), SelectedItem = EditorFont };
        var sizes = new ComboBox { ItemsSource = Enumerable.Range(9, 32), SelectedItem = (int)EditorSize };
        var appearance = SettingsCard(layout, "Görünüm");
        var editing = new StackPanel();
        SettingsField(appearance, "Tema", themes);
        SettingsField(editing, "Yazı tipi", fonts);
        SettingsField(editing, "Yazı boyutu", sizes, 80);
        var wrap = new CheckBox { Content = "Varsayılan olarak kelime kaydır", IsChecked = EditorWrap, Margin = new Thickness(0, 10, 0, 0) };
        var startup = new CheckBox { Content = "Windows ile başlat", IsChecked = store.Setting("startup") == "1", Margin = new Thickness(0, 7, 0, 0) };
        wrap.SetResourceReference(StyleProperty, "SettingsSwitch");
        editing.Children.Add(wrap);
        var app = (App)Application.Current;
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        layout.Children.Add(warning);
        var storage = new StackPanel();
        SettingsAction(storage, Button("Veri klasörünü aç", () => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(AppPaths.Data) { UseShellExecute = true })));
        AddPictureSettings(storage);
        var view = new ComboBox { ItemsSource = new[] { "Kart", "Liste" }, SelectedIndex = store.Setting("cardView") == "list" ? 1 : 0 };
        SettingsField(appearance, "Not görünümü", view);
        SettingsCard(layout, "Editör").Children.Add(editing);
        startup.SetResourceReference(StyleProperty, "SettingsSwitch");
        SettingsCard(layout, "Başlangıç").Children.Add(startup);
        SettingsCard(layout, "Depolama").Children.Add(storage);
        view.SelectionChanged += (_, _) => { store.Set("cardView", view.SelectedIndex == 1 ? "list" : "card"); RefreshCards(); };
        var days = new TextBox { Text = store.Setting("autoArchiveDays") ?? "0" };
        var archive = SettingsCard(layout, "Arşiv");
        SettingsField(archive, "Otomatik arşiv süresi (gün, 0 kapalı)", days, 80);
        void SaveArchive()
        {
            if (!int.TryParse(days.Text, out var value) || value is < 0 or > 3650)
            {
                warning.Text = "Otomatik arşiv süresi 0 ile 3650 gün arasında olmalı.";
                return;
            }
            if (!Flush(false)) return;
            warning.Text = "";
            store.Set("autoArchiveDays", value.ToString());
            if (value > 0) store.ArchiveInactive(value);
            RefreshCards();
        }
        days.LostKeyboardFocus += (_, _) => SaveArchive();
        days.KeyDown += (_, e) => { if (e.Key == Key.Enter) SaveArchive(); };
        layout.Children.Add(Brand("Tekin Teknoloji — tekinteknoloji.com"));
        themes.SelectionChanged += (_, _) =>
        {
            theme = (string)themes.SelectedItem; store.Set("theme", theme); ApplyTheme();
        };
        fonts.SelectionChanged += (_, _) =>
        {
            EditorFont = (string)fonts.SelectedItem; store.Set("font", EditorFont);
            foreach (var editor in editors.Values) editor.ApplyAppearance();
        };
        sizes.SelectionChanged += (_, _) =>
        {
            EditorSize = (int)sizes.SelectedItem; store.Set("size", ((int)EditorSize).ToString());
            foreach (var editor in editors.Values) editor.ApplyAppearance();
        };
        wrap.Click += (_, _) =>
        {
            EditorWrap = wrap.IsChecked == true;
            store.Set("wrap", EditorWrap ? "1" : "0");
            foreach (var editor in editors.Values) editor.SetWrap(EditorWrap);
        };
        startup.Click += (_, _) =>
        {
            var error = app.SetStartup(startup.IsChecked == true);
            if (error.Length == 0) store.Set("startup", startup.IsChecked == true ? "1" : "0");
            else { warning.Text = error; startup.IsChecked = store.Setting("startup") == "1"; }
        };
        window.Show();

    }

    private void SystemThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (theme == "Sistem") Dispatcher.BeginInvoke(new Action(ApplyTheme));
    }

    internal static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private void ApplyTheme()
    {
        Theme.Apply(Theme.Resolve(theme));
        foreach (var editor in editors.Values) editor.ApplyAppearance();
        foreach (var window in desktopWindows.Values) window.ApplyPalette();
        RefreshCards();
    }

    private void Shortcuts(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.K) OpenPalette();
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) NewNote();
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) Flush();
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L) search.Focus();
        else if (tabs.SelectedContent is EditorPane pane && pane.HandleShortcut(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) { }
        else return;
        e.Handled = true;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (Flush()) Hide();
    }
    public bool PrepareExit()
    {
        if (exiting) return true;
        if (cloudWriting || dailyBackupRunning) { Notify("Arka plan işlemi sürüyor. Tamamlandığında tekrar çıkabilirsiniz."); return false; }
        if (!Flush(false)) return false;
        foreach (var editor in editors.Values.ToArray())
            if (!desktopWindows.ContainsKey(editor.Note.Id) && store.RemoveBlank(editor.Note)) RemoveTab(editor);
        SaveSession();
        SystemParameters.StaticPropertyChanged -= SystemThemeChanged;
        exiting = true;
        return true;
    }
}
