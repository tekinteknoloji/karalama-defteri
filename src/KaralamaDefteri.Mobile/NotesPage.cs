using Microsoft.Maui.Controls.Shapes;

namespace KaralamaDefteri.Mobile;

internal sealed partial class NotesPage : ContentPage
{
    private readonly NoteStore store;
    private readonly MobileDriveSync drive;
    private readonly FeatureFlags features;
    private readonly Entry search = MobileStyle.Entry("Notlarda ara");
    private readonly Button clearTag = MobileStyle.Button("×");
    private readonly Label heading = MobileStyle.Label("Tümü", 17, true);
    private readonly VerticalStackLayout groupList = new() { Spacing = 6, Padding = 12 };
    private readonly Grid drawer = new() { IsVisible = false };
    private readonly HashSet<string> expandedGroups = new(StringComparer.Ordinal);
    private readonly GridItemsLayout columns = new(2, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 8, VerticalItemSpacing = 8 };
    private readonly LinearItemsLayout rows = new(ItemsLayoutOrientation.Vertical) { ItemSpacing = 8 };
    private readonly CollectionView cards;
    private readonly Button addNote;
    private readonly Button emptyTrash;
    private readonly View emptyNote;
    private readonly Label noResults;
    private readonly IDispatcherTimer searchTimer;
    private long? group;
    private int scope;
    private bool listView;
    private string? colorFilter;

    internal NotesPage(NoteStore store, MobileDriveSync drive)
    {
        this.store = store;
        this.drive = drive;
        listView = store.Setting("mobileListView") == "1";
        features = new FeatureFlags(store);
        drive.LocalChanged += Refresh;
        Title = "Karalama Defteri";
        MobileStyle.Page(this);
        searchTimer = Dispatcher.CreateTimer();
        searchTimer.Interval = TimeSpan.FromMilliseconds(150);
        searchTimer.IsRepeating = false;
        searchTimer.Tick += (_, _) => Refresh();
        search.TextChanged += (_, _) =>
        {
            clearTag.IsVisible = search.Text is { } value && value.StartsWith('#') && !value.Any(char.IsWhiteSpace);
            searchTimer.Stop();
            searchTimer.Start();
        };
        cards = new CollectionView
        {
            ItemsLayout = listView ? rows : columns,
            ItemTemplate = new DataTemplate(CreateCard),
            Footer = new BoxView { HeightRequest = 72, InputTransparent = true },
            ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems
        };
        var first = MobileStyle.Button("Yeni not", async () => await OpenNote(null), true);
        first.HorizontalOptions = LayoutOptions.Center;
        first.HeightRequest = 56;
        first.Padding = new Thickness(30, 8);
        emptyNote = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center,
            Children = { first }
        };
        noResults = MobileStyle.Label("Not bulunamadı.");
        noResults.HorizontalOptions = LayoutOptions.Center;
        noResults.VerticalOptions = LayoutOptions.Center;

        var bar = new Grid
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            Padding = new Thickness(10, 5), ColumnSpacing = 10
        };
        bar.SetAppThemeColor(VisualElement.BackgroundColorProperty, MobileStyle.LightBar, MobileStyle.DarkBar);
        bar.Add(HeaderButton("☰", "Gruplar", OpenDrawer), 0);
        var title = MobileStyle.Label(Title, 20, true);
        title.VerticalOptions = LayoutOptions.Center;
        title.LineBreakMode = LineBreakMode.TailTruncation;
        bar.Add(title, 1);
        bar.Add(HeaderButton("⋯", "Ayarlar ve görünümler", async () => await Settings()), 2);

        addNote = MobileStyle.Button("+", async () => await OpenNote(null), true);
        SemanticProperties.SetDescription(addNote, "Yeni not");
        addNote.FontSize = 30;
        addNote.CornerRadius = 28;
        addNote.WidthRequest = addNote.HeightRequest = 56;
        addNote.Padding = 0;
        addNote.HorizontalOptions = LayoutOptions.End;
        addNote.VerticalOptions = LayoutOptions.End;
        addNote.Margin = new Thickness(0, 0, 10, 10);
        var surface = new Grid();
        surface.Add(cards);
        surface.Add(addNote);
        surface.Add(groupUndoBar);
        var layout = new Grid
        {
            Padding = 10, RowSpacing = 9,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }
        };
        var searchRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 5 };
        searchRow.Add(search);
        clearTag.IsVisible = false;
        clearTag.Clicked += (_, _) => search.Text = "";
        SemanticProperties.SetDescription(clearTag, "Etiket filtresini kaldır");
        searchRow.Add(clearTag, 1);
        var filterButtons = new HorizontalStackLayout { Spacing = 5 };
        var filters = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = filterButtons };
        var tags = MobileStyle.Button("Etiketler ▾");
        var tagPanel = CreateTagsPanel(tags);
        filterButtons.Children.Add(tags);
        var controls = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
            ColumnSpacing = 8, RowSpacing = 5
        };
        controls.Add(searchRow);
        controls.Add(filters, 0, 1);
        Grid.SetColumnSpan(searchRow, 2);
        Grid.SetColumnSpan(filters, 2);
        layout.Add(new VerticalStackLayout { Spacing = 6, Children = { controls, tagPanel } });
        heading.VerticalOptions = LayoutOptions.Center;
        heading.LineBreakMode = LineBreakMode.TailTruncation;
        var colors = MobileStyle.Button("Tüm renkler");
        colors.Clicked += async (_, _) =>
        {
            var choice = await DisplayActionSheet("Renge göre göster", "Kapat", null, ["Tüm renkler", .. NotePage.NoteColors.Select(tone => tone.Name)]);
            if (choice is null or "Kapat") return;
            colorFilter = NotePage.NoteColors.FirstOrDefault(tone => tone.Name == choice)?.Value;
            colors.Text = choice;
            Refresh();
        };
        filterButtons.Children.Add(colors);
        var view = MobileStyle.Button(listView ? "Kart" : "Liste");
        view.Clicked += (_, _) =>
        {
            listView = !listView;
            store.Set("mobileListView", listView ? "1" : "0");
            cards.ItemsLayout = listView ? rows : columns;
            cards.ItemTemplate = new DataTemplate(CreateCard);
            view.Text = listView ? "Kart" : "Liste";
            Refresh();
        };
        filterButtons.Children.Add(view);
        layout.Add(heading, 0, 1);
        emptyTrash = MobileStyle.Button("Çöp kutusunu boşalt", async () =>
        {
            if (!await DisplayAlert("Çöp kutusunu boşalt", "Çöp kutusundaki notlar ve ekleri kalıcı olarak silinecek.", "Boşalt", "Vazgeç")) return;
            store.EmptyTrash();
            drive.Changed();
            Refresh();
        });
        layout.Add(emptyTrash, 0, 2);
        layout.Add(surface, 0, 3);
        var root = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        root.Add(bar);
        root.Add(layout, 0, 1);
        Grid.SetRowSpan(drawer, 2);
        root.Add(drawer);
        var shade = new BoxView { Color = Color.FromArgb("#66000000") };
        var close = new TapGestureRecognizer();
        close.Tapped += (_, _) => drawer.IsVisible = false;
        shade.GestureRecognizers.Add(close);
        drawer.Add(shade);
        var panel = new ScrollView { WidthRequest = 272, HorizontalOptions = LayoutOptions.Start, Content = groupList };
        panel.SetAppThemeColor(VisualElement.BackgroundColorProperty, MobileStyle.LightBar, MobileStyle.DarkBar);
        drawer.Add(panel);
        NavigationPage.SetHasNavigationBar(this, false);
        Content = root;
        SizeChanged += (_, _) =>
        {
            if (Width > 0) panel.WidthRequest = Math.Min(286, Width * 0.84);
            var wide = Width >= 680;
            Grid.SetColumnSpan(searchRow, wide ? 1 : 2);
            controls.ColumnDefinitions[0].Width = wide
                ? new GridLength(Math.Min(480, Width - 350)) : GridLength.Star;
            filters.HorizontalOptions = wide ? LayoutOptions.Start : LayoutOptions.Fill;
            Grid.SetColumn(filters, wide ? 1 : 0);
            Grid.SetRow(filters, wide ? 0 : 1);
            Grid.SetColumnSpan(filters, wide ? 1 : 2);
            var span = Width >= 850 ? 4 : Width >= 550 ? 3 : 2;
            if (columns.Span != span) columns.Span = span;
        };
    }

    protected override void OnAppearing() { base.OnAppearing(); drive.Editing(null); Refresh(); }

    protected override bool OnBackButtonPressed()
    {
        if (closeTagsPanel?.Invoke() == true) return true;
        if (drawer.IsVisible)
        {
            drawer.IsVisible = false;
            return true;
        }
        if (group is null && scope == 0) return base.OnBackButtonPressed();
        Select("Tümü", null, 0);
        return true;
    }

    private static Button HeaderButton(string glyph, string label, Action action)
    {
        var button = MobileStyle.Button(glyph, action);
        SemanticProperties.SetDescription(button, label);
        button.WidthRequest = 44;
        button.FontSize = 21;
        button.Padding = 0;
        return button;
    }

    private void Refresh()
    {
        var query = search.Text ?? "";
        var results = store.Search(query, scope, colorFilter, group: group);
        emptyTrash.IsVisible = scope == 2 && store.Search("", 2, null, limit: 1).Count > 0;
        var canCreate = scope == 0;
        var emptyCanCreate = canCreate && results.Count == 0 && string.IsNullOrWhiteSpace(query) && colorFilter is null;
        addNote.IsVisible = canCreate && !emptyCanCreate;
        cards.EmptyView = emptyCanCreate ? emptyNote : noResults;
        cards.ItemsSource = results;
    }

    private async Task OpenNote(Card? card)
    {
        search.Unfocus();
        var note = card is null ? null : store.Get(card.Id);
        if (card is not null && note is null) return;
        await OpenEditor(note);
    }

    private async Task OpenEditor(Note? note)
    {
        drive.Editing(note is null ? null : Guid.Parse(note.Uuid));
        var page = new NotePage(store, note, group is > 0 ? group : null, drive);
        await Navigation.PushAsync(page);
        if (note is null) await page.FocusBody();
    }

    internal async Task OpenEntry(MobileEntry entry)
    {
        if (Window?.Page is NavigationPage navigation && navigation.CurrentPage is NotePage editor && !editor.Flush())
            throw new IOException();
        await Navigation.PopToRootAsync(false);
        Note? note = null;
        if (!string.IsNullOrWhiteSpace(entry.Text) || entry.Picture is not null)
        {
            store.Atomic(() =>
            {
                note = store.Create(entry.Text, group is > 0 ? group : null);
                if (entry.Picture is not { } picture) return;
                var id = store.InsertPicture(note.Id, picture);
                note.Content = string.IsNullOrWhiteSpace(note.Content) ? ImageMarkers.Marker(id) :
                    note.Content.TrimEnd() + "\n" + ImageMarkers.Marker(id);
                note.Title = NoteStore.AutoTitle(note.Content, note.Created);
                store.Save(note);
            });
            drive.Changed();
        }
        await OpenEditor(note);
    }

    private View CreateCard()
    {
        var title = MobileStyle.Label("", 16, true);
        title.MaxLines = 2;
        title.LineBreakMode = LineBreakMode.TailTruncation;
        var pin = MobileStyle.Label("📌", 13);
        SemanticProperties.SetDescription(pin, "Sabitlenmiş not");
        var color = new Ellipse { WidthRequest = 10, HeightRequest = 10, VerticalOptions = LayoutOptions.Center };
        var badges = new HorizontalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Start, Children = { pin, color } };
        var cardHeader = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 5
        };
        cardHeader.Add(title, 0);
        cardHeader.Add(badges, 1);
        var preview = MobileStyle.Label("");
        preview.MaxLines = 3;
        preview.LineBreakMode = LineBreakMode.TailTruncation;
        var metadata = MobileStyle.Label("", 12);
        var image = new Image { HeightRequest = listView ? 66 : 110, Aspect = Aspect.AspectFit };
        var card = new Border
        {
            Padding = 10, StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new VerticalStackLayout { Spacing = 6, Children = { cardHeader, image, preview, metadata } }
        };
        card.SetAppThemeColor(VisualElement.BackgroundColorProperty, MobileStyle.LightCard, MobileStyle.DarkCard);
        card.BindingContextChanged += (_, _) =>
        {
            if (card.BindingContext is not Card item) return;
            title.Text = item.Title;
            pin.IsVisible = item.Pinned;
            preview.Text = item.Preview;
            preview.IsVisible = !string.IsNullOrWhiteSpace(item.Preview);
            metadata.Text = item.Group ?? "";
            metadata.IsVisible = item.Group is not null;
            image.Source = item.Thumbnail is { } bytes ? MobileStyle.Picture(bytes).Source : null;
            image.IsVisible = image.Source is not null;
            var tone = NotePage.NoteColors.FirstOrDefault(option => option.Value == item.Color);
            color.IsVisible = tone is not null;
            if (tone is not null)
            {
                SemanticProperties.SetDescription(color, tone.Name);
                color.SetAppTheme<Brush>(Shape.FillProperty, new SolidColorBrush(tone.Light), new SolidColorBrush(tone.Dark));
            }
            badges.IsVisible = pin.IsVisible || color.IsVisible;
            card.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush(tone?.Light ?? MobileStyle.LightLine),
                new SolidColorBrush(tone?.Dark ?? MobileStyle.DarkLine));
            card.StrokeThickness = tone is null ? 1 : 3;
        };
        async void OpenCard(object? sender, EventArgs args)
        {
            if (card.BindingContext is Card item) await OpenNote(item);
        }
        async void CardOptions(object? sender, Android.Views.View.LongClickEventArgs args)
        {
            args.Handled = true;
            if (card.BindingContext is not Card item || store.Get(item.Id) is not { } note) return;
            search.Unfocus();
            if (!await NotePage.ApplyOptions(this, store, note)) return;
            drive.Changed();
            Refresh();
        }
        card.HandlerChanged += (_, _) =>
        {
            if (card.Handler?.PlatformView is not Android.Views.View view) return;
            view.Click += OpenCard;
            view.LongClick += CardOptions;
        };
        card.HandlerChanging += (_, args) =>
        {
            if (args.OldHandler?.PlatformView is not Android.Views.View view) return;
            view.Click -= OpenCard;
            view.LongClick -= CardOptions;
        };
        return card;
    }

    private void OpenDrawer()
    {
        search.Unfocus();
        groupList.Children.Clear();
        var drawerTitle = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        var title = MobileStyle.Label("Gruplar", 19, true);
        title.VerticalOptions = LayoutOptions.Center;
        drawerTitle.Add(title);
        drawerTitle.Add(HeaderButton("×", "Grupları kapat", () => drawer.IsVisible = false), 1);
        groupList.Children.Add(drawerTitle);
        Section("Görünümler");
        Add("Tümü", null, 0);
        Add("Grupsuz", 0, 0);
        Section("Kayıtlı gruplar");
        var groups = store.Groups();
        AddGroupRows(false);
        AddNewGroup();
        if (groups.Any(item => item.Archived))
        {
            Section("Arşivlenmiş gruplar");
            AddGroupRows(true);
        }
        Section("Diğer");
        Add("Arşiv", null, 1);
        Add("Çöp kutusu", null, 2);
        if (features.Enabled(Feature.Board))
            groupList.Children.Add(MobileStyle.Button("Pano", async () =>
            {
                drawer.IsVisible = false;
                await Navigation.PushAsync(new MobileBoardPage(store, drive, group, OpenNote));
            }));
        if (features.Selected(Feature.Authenticator))
            groupList.Children.Add(MobileStyle.Button("Doğrulayıcı", async () =>
            {
                drawer.IsVisible = false;
                await Navigation.PushAsync(new MobileAuthenticatorPage(store, drive, group));
            }));
        drawer.IsVisible = true;

        void AddGroupRows(bool archived)
        {
            var section = groups.Where(item => item.Archived == archived).ToArray();
            var parents = section.Select(item => item.ParentUuid).ToHashSet();
            foreach (var (item, depth) in NoteStore.VisibleGroupTree(section, expandedGroups))
            {
                var row = GroupRow(item);
                var container = new Grid
                {
                    ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) },
                    Margin = new Thickness(Math.Min(depth, 8) * 8, 0, 0, 0)
                };
                if (parents.Contains(item.Uuid))
                {
                    var expanded = expandedGroups.Contains(item.Uuid);
                    container.Add(HeaderButton(expanded ? "▾" : "▸",
                        item.Name + (expanded ? " · Alt grupları kapat" : " · Alt grupları aç"), () =>
                        {
                            if (!expandedGroups.Remove(item.Uuid)) expandedGroups.Add(item.Uuid);
                            OpenDrawer();
                        }));
                }
                container.Add(row, 1);
                groupList.Children.Add(container);
            }
        }

        void Section(string label)
        {
            var section = MobileStyle.Label(label, 12, true);
            section.Margin = new Thickness(4, 9, 4, 2);
            section.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#6B6A64"), Color.FromArgb("#A8A69C"));
            groupList.Children.Add(section);
        }

        void Add(string label, long? selectedGroup, int selectedScope, string? name = null) =>
            groupList.Children.Add(MobileStyle.Button(label, () => Select(name ?? label, selectedGroup, selectedScope),
                group == selectedGroup && scope == selectedScope));
    }

    private void Select(string title, long? selectedGroup, int selectedScope)
    {
        group = selectedGroup;
        scope = selectedScope;
        heading.Text = title;
        search.Text = "";
        drawer.IsVisible = false;
        Refresh();
    }

    private async Task Settings()
    {
        var choice = await DisplayActionSheet("Ayarlar", "Kapat", null, "Arşiv", "Çöp kutusu", "Özellikler", "Güvenlik", "Hızlı not widget’ı",
            "Google Drive", "Güncellemeler", "Sistem teması", "Açık tema", "Koyu tema", "Hakkında");
        if (choice is "Arşiv" or "Çöp kutusu")
        {
            Select(choice, null, choice == "Arşiv" ? 1 : 2);
            return;
        }
        if (choice == "Güncellemeler")
        {
            await Navigation.PushAsync(new AndroidUpdatePage());
            return;
        }
        if (choice == "Hızlı not widget’ı")
        {
            if (!QuickNoteWidget.RequestPin())
                await DisplayAlert("Hızlı not", "Widget’ı ana ekranın Widget’lar menüsünden ekleyebilirsin.", "Tamam");
            return;
        }
        if (choice == "Özellikler")
        {
            await Navigation.PushAsync(new MobileFeaturesPage(features, drive));
            return;
        }
        if (choice == "Güvenlik")
        {
            await Navigation.PushAsync(new MobileSecurityPage(store));
            return;
        }
        if (choice == "Google Drive")
        {
            await Navigation.PushAsync(new DriveSettingsPage(drive));
            return;
        }
        var theme = choice switch { "Sistem teması" => AppTheme.Unspecified, "Açık tema" => AppTheme.Light, "Koyu tema" => AppTheme.Dark, _ => (AppTheme?)null };
        if (theme is { } selected)
        {
            store.Set("mobileTheme", selected.ToString());
            Application.Current!.UserAppTheme = selected;
            (Platform.CurrentActivity as MainActivity)?.ApplySystemBarTheme();
        }
        else if (choice == "Hakkında")
        {
            var about = new ContentPage { Title = "Hakkında" };
            MobileStyle.Page(about);
            MobileStyle.SetContent(about, new VerticalStackLayout
            {
                Padding = 24, Spacing = 12,
                Children =
                {
                    MobileStyle.Label($"Karalama Defteri v{AppInfo.Current.VersionString}", 22, true),
                    MobileStyle.Label("Geliştiren: Tekin Teknoloji"),
                    MobileStyle.Button("tekinteknoloji.com", async () => await Launcher.Default.OpenAsync(new Uri("https://tekinteknoloji.com/karalama-defteri/")))
                }
            });
            await Navigation.PushAsync(about);
        }
    }
}
