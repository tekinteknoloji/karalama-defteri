namespace KaralamaDefteri.Mobile;

internal sealed partial class NotePage : ContentPage
{
    internal sealed record NoteColor(string Name, string Value, Color Light, Color Dark);
    internal static readonly NoteColor[] NoteColors =
    [
        new("Sarı", "#FFF475", Color.FromArgb("#9A7500"), Color.FromArgb("#DDC05D")),
        new("Yeşil", "#A7FFEB", Color.FromArgb("#287762"), Color.FromArgb("#6AB8A7")),
        new("Mavi", "#AECBFA", Color.FromArgb("#426FAC"), Color.FromArgb("#81ADF0")),
        new("Pembe", "#FDCFE8", Color.FromArgb("#AC5079"), Color.FromArgb("#E48BB4"))
    ];

    private readonly NoteStore store;
    private readonly MobileDriveSync drive;
    private Note? note;
    private long? initialGroup;
    private readonly Entry title = MobileStyle.Entry("Otomatik başlık");
    private readonly Editor body = new() { Placeholder = "Notunu yaz", MinimumHeightRequest = 150 };
    private readonly Label feedback = MobileStyle.Label("", 12);
    private readonly Button pictures;
    private readonly Button groupButton;
    private readonly IDispatcherTimer timer;
    private bool dirty;
    private bool picking;

    internal NotePage(NoteStore store, Note? note, long? group, MobileDriveSync drive)
    {
        this.store = store;
        this.drive = drive;
        this.note = note;
        initialGroup = group;
        Title = "Not";
        MobileStyle.Page(this);
        title.Text = note?.Manual == true ? note.Title : "";
        body.Text = note?.Content ?? "";
        if (note?.Manual == false) title.Placeholder = note.Title;
        title.IsReadOnly = body.IsReadOnly = note?.Deleted is not null;
        groupButton = MobileStyle.Button("Grupsuz", async () =>
        {
            if (!Flush()) return;
            var selection = await ChooseGroup(this, store);
            if (!selection.Selected) return;
            if (note is { } current)
            {
                current.GroupId = selection.Id;
                store.Save(current);
                drive.Changed();
            }
            else initialGroup = selection.Id;
            UpdateGroup();
        });
        groupButton.IsEnabled = note?.Deleted is null;
        groupButton.MaximumWidthRequest = 140;
        UpdateGroup();
        body.SetAppThemeColor(InputView.TextColorProperty, MobileStyle.LightText, MobileStyle.DarkText);
        body.SetAppThemeColor(InputView.PlaceholderColorProperty, Color.FromArgb("#6B6A64"), Color.FromArgb("#A8A69C"));
        body.SetAppThemeColor(VisualElement.BackgroundColorProperty, MobileStyle.LightCard, MobileStyle.DarkCard);
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Flush();
        title.TextChanged += Changed;
        body.TextChanged += Changed;
        var actions = new HorizontalStackLayout { Spacing = 6 };
        if (note?.Deleted is null)
        {
            actions.Children.Add(MobileStyle.Button("Resim ekle", async () => await AddPicture(camera: false)));
            actions.Children.Add(MobileStyle.Button("Kamera", async () => await AddPicture(camera: true)));
        }
        pictures = MobileStyle.Button("Resimler", async () => await ViewPictures());
        actions.Children.Add(pictures);
        actions.Children.Add(MobileStyle.Button("⋯", async () => await Options()));
        var layout = new Grid
        {
            Padding = 12, RowSpacing = 8,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }
        };
        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6 };
        header.Add(title);
        header.Add(groupButton, 1);
        layout.Add(header);
        InitializeEditorTools(layout);
        layout.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = actions }, 0, 3);
        layout.Add(feedback, 0, 4);
        MobileStyle.SetContent(this, layout);
        UpdatePictures();
        RefreshEditorTools();
    }

    internal async Task FocusBody()
    {
        if (!body.IsVisible) return;
        body.Focus();
        await body.ShowSoftInputAsync(CancellationToken.None);
    }

    protected override void OnDisappearing()
    {
        if (Flush() && note is { Deleted: null, Manual: false } current &&
            string.IsNullOrWhiteSpace(current.Content) && store.RemoveBlank(current))
        {
            note = null;
            drive.Changed();
        }
        base.OnDisappearing();
    }

    private void Changed(object? sender, TextChangedEventArgs args)
    {
        dirty = true;
        feedback.Text = "";
        timer.Stop();
        timer.Start();
        UpdatePictures();
        RefreshEditorTools();
    }

    internal bool Flush()
    {
        timer.Stop();
        if (!dirty) return true;
        var content = body.Text ?? "";
        var manual = !string.IsNullOrWhiteSpace(title.Text);
        if (note is null && !manual && string.IsNullOrWhiteSpace(content))
        {
            dirty = false;
            return true;
        }
        try
        {
            var draft = note;
            store.Atomic(() =>
            {
                draft ??= store.Create(content, initialGroup, manual ? title.Text!.Trim() : null);
                Save(draft, content, manual);
            });
            note = draft;
            if (note is not null) drive.Editing(Guid.Parse(note.Uuid));
            drive.Changed();
            title.Placeholder = note?.Title ?? "Otomatik başlık";
            feedback.Text = note is null ? "" : "Kaydedildi";
            dirty = false;
            return true;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(error.ToString());
            feedback.Text = "Not kaydedilemedi. Alanı kontrol edip tekrar düzenle.";
            return false;
        }
    }

    private void Save(Note draft, string content, bool manual)
    {
        draft.Content = content;
        draft.Checklist = checklistMode;
        draft.Manual = manual;
        draft.Title = manual ? title.Text!.Trim() : NoteStore.AutoTitle(content, draft.Created);
        store.Save(draft);
        store.SyncPictures(draft);
    }

    private IEnumerable<long> ImageIds() =>
        ImageMarkers.Pattern().Matches(body.Text ?? "").Select(ImageMarkers.Id).Where(id => id > 0).Distinct();

    private void UpdatePictures() => pictures.IsVisible = ImageIds().Any();

    private void UpdateGroup()
    {
        var id = note is null ? initialGroup : note.GroupId;
        groupButton.Text = store.Groups().FirstOrDefault(item => item.Id == id)?.Name ?? "Grupsuz";
        SemanticProperties.SetDescription(groupButton, $"Not grubu · {groupButton.Text}");
    }

    private async Task AddPicture(bool camera)
    {
        if (picking || !Flush()) return;
        picking = true;
        try
        {
            if (camera && !MediaPicker.Default.IsCaptureSupported)
            {
                feedback.Text = "Bu cihazda kullanılabilir kamera bulunamadı.";
                return;
            }
            var file = camera ? await MediaPicker.Default.CapturePhotoAsync() :
                await FilePicker.Default.PickAsync(new PickOptions { FileTypes = FilePickerFileType.Images, PickerTitle = "Resim seç" });
            if (file is null) return;
            await using var source = await file.OpenReadAsync();
            var picture = await MobileEntry.ReadPicture(source, file.FileName, camera);
            var draft = note;
            var content = body.Text ?? "";
            store.Atomic(() =>
            {
                draft ??= store.Create("", initialGroup);
                var id = store.InsertPicture(draft.Id, picture);
                content = content.Length == 0 ? ImageMarkers.Marker(id) : content.TrimEnd('\n', '\r') + "\n" + ImageMarkers.Marker(id);
                Save(draft, content, !string.IsNullOrWhiteSpace(title.Text));
            });
            note = draft;
            drive.Editing(Guid.Parse(note!.Uuid));
            drive.Changed();
            body.Text = content;
            timer.Stop();
            dirty = false;
            title.Placeholder = note!.Title;
            feedback.Text = "Kaydedildi";
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(error.ToString());
            feedback.Text = camera ? "Kamera fotoğrafı okunamadı. Tekrar çekmeyi dene." :
                "Resim eklenemedi. Bir PNG veya JPG dosyası seç.";
        }
        finally { picking = false; }
    }

    private async Task ViewPictures()
    {
        if (!Flush() || note is null) return;
        var noteId = note.Id;
        var ids = ImageIds().ToArray();
        if (ids.Length == 0) return;
        var page = new ContentPage { Title = "Resimler" };
        MobileStyle.Page(page);
        var image = new Image { Aspect = Aspect.AspectFit };
        var caption = MobileStyle.Label("");
        var index = 0;
        var fit = true;
        var render = 0;
        async Task Show()
        {
            var request = ++render;
            var picture = store.GetPicture(noteId, ids[index]);
            image.Source = null;
            caption.Text = picture is null ? "Resim bulunamadı." : $"{index + 1}/{ids.Length} · {picture.FileName ?? "Resim"}";
            if (picture is null) return;
            byte[] bytes;
            try { bytes = await Task.Run(() => CameraPicture.ForDisplay(picture)); }
            catch (Exception error) when (error is Java.Lang.Exception or InvalidDataException)
            {
                bytes = picture.Bytes;
            }
            if (request == render) image.Source = MobileStyle.Picture(bytes).Source;
        }
        var toolbar = new HorizontalStackLayout { Spacing = 6 };
        if (ids.Length > 1)
        {
            toolbar.Children.Add(MobileStyle.Button("Önceki", async () => { index = (index + ids.Length - 1) % ids.Length; await Show(); }));
            toolbar.Children.Add(MobileStyle.Button("Sonraki", async () => { index = (index + 1) % ids.Length; await Show(); }));
        }
        toolbar.Children.Add(MobileStyle.Button("Yakınlaştır", () => { fit = !fit; image.Scale = fit ? 1 : 2; }));
        var layout = new Grid { Padding = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        layout.Add(caption);
        layout.Add(new Grid { IsClippedToBounds = true, Children = { image } }, 0, 1);
        layout.Add(toolbar, 0, 2);
        MobileStyle.SetContent(page, layout);
        await Navigation.PushAsync(page);
        await Show();
    }

    private async Task Options()
    {
        if (!Flush() || note is not { } current) return;
        var previousArchive = current.Archived;
        var wasDeleted = current.Deleted is not null;
        if (!await ApplyOptions(this, store, current)) return;
        drive.Changed();
        UpdateGroup();
        if (wasDeleted || current.Deleted is not null || previousArchive != current.Archived)
            await Navigation.PopAsync();
        else feedback.Text = "Kaydedildi";
    }

    internal static async Task<bool> ApplyOptions(Page page, NoteStore store, Note current)
    {
        if (current.Deleted is not null)
        {
            if (await page.DisplayActionSheet("Çöp kutusu", "Kapat", null, "Geri yükle") != "Geri yükle") return false;
            current.Deleted = null;
            store.Save(current);
            return true;
        }
        var pin = current.Pinned ? "Sabitlemeyi kaldır" : "Sabitle";
        var archive = current.Archived ? "Arşivden çıkar" : "Arşivle";
        var action = await page.DisplayActionSheet("Not işlemleri", "Kapat", "Çöp kutusuna taşı", pin, archive, "Renk", "Gruba taşı");
        switch (action)
        {
            case "Çöp kutusuna taşı": current.Deleted = NoteStore.Now(); break;
            case var item when item == pin: current.Pinned = !current.Pinned; break;
            case var item when item == archive: current.Archived = !current.Archived; break;
            case "Renk":
                var color = await page.DisplayActionSheet("Not rengi", "Kapat", null, [.. NoteColors.Select(item => item.Name), "Renksiz"]);
                if (color is null or "Kapat") return false;
                current.Color = NoteColors.FirstOrDefault(item => item.Name == color)?.Value;
                break;
            case "Gruba taşı":
                var selection = await ChooseGroup(page, store);
                if (!selection.Selected) return false;
                current.GroupId = selection.Id;
                break;
            default: return false;
        }
        store.Save(current);
        return true;
    }

    private static async Task<(bool Selected, long? Id)> ChooseGroup(Page page, NoteStore store)
    {
        var groups = store.Groups().Where(group => !group.Archived).ToArray();
        var name = await page.DisplayActionSheet("Gruba taşı", "Kapat", null, ["Grupsuz", .. groups.Select(group => group.Name)]);
        if (name is null or "Kapat") return (false, null);
        return (true, name == "Grupsuz" ? null : groups.FirstOrDefault(group => group.Name == name)?.Id);
    }
}
