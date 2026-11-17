namespace KaralamaDefteri.Mobile;

internal sealed partial class NotesPage
{
    private readonly HorizontalStackLayout groupUndoBar = new()
    {
        IsVisible = false, Spacing = 8, Padding = 8, VerticalOptions = LayoutOptions.End,
        HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(0, 0, 58, 0)
    };

    private Button GroupRow(NoteGroup item)
    {
        var row = MobileStyle.Button($"{(item.Color is null ? "" : "● ")}{item.Name} ({item.Count})",
            () => Select(item.Name, item.Id, 0), group == item.Id && scope == 0);
        if (item.Color is { } color && NotePage.NoteColors.FirstOrDefault(tone => tone.Value == color) is { } tone)
            row.SetAppThemeColor(Button.BorderColorProperty, tone.Light, tone.Dark);
        async void Hold(object? sender, Android.Views.View.LongClickEventArgs args)
        {
            args.Handled = true;
            await GroupOptions(item.Id);
        }
        row.HandlerChanged += (_, _) =>
        {
            if (row.Handler?.PlatformView is Android.Views.View view) view.LongClick += Hold;
        };
        row.HandlerChanging += (_, args) =>
        {
            if (args.OldHandler?.PlatformView is Android.Views.View view) view.LongClick -= Hold;
        };
        return row;
    }

    private void AddNewGroup()
    {
        var input = MobileStyle.Entry("Grup adı");
        input.MaxLength = 80;
        var editor = new HorizontalStackLayout { IsVisible = false, Spacing = 4 };
        input.WidthRequest = 140;
        editor.Children.Add(input);
        editor.Children.Add(MobileStyle.Button("Ekle", Create));
        input.Completed += (_, _) => Create();
        groupList.Children.Add(MobileStyle.Button("+ Yeni grup", () => { editor.IsVisible = true; input.Focus(); }));
        groupList.Children.Add(editor);

        void Create()
        {
            var name = input.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            Select(name, store.GetOrCreateGroup(name), 0);
            drive.Changed();
        }
    }

    private async Task GroupOptions(long id)
    {
        var item = store.Groups().FirstOrDefault(group => group.Id == id);
        if (item is null) return;
        var archive = item.Archived ? "Arşivden çıkar" : "Arşivle";
        var choice = await DisplayActionSheet(item.Name, "Kapat", "Sil", "Yeniden adlandır", "Renk", archive,
            "Alt grup oluştur", "Yukarı taşı", "Aşağı taşı");
        try
        {
            switch (choice)
            {
                case "Alt grup oluştur":
                    var childName = (await DisplayPromptAsync("Alt grup oluştur", "Alt grup adı", "Ekle", "Vazgeç", maxLength: 80))?.Trim();
                    if (string.IsNullOrEmpty(childName)) return;
                    expandedGroups.Add(item.Uuid);
                    Select(childName, store.CreateSubgroup(childName, item), 0);
                    break;
                case "Yeniden adlandır":
                    var name = (await DisplayPromptAsync("Grubu yeniden adlandır", "Grup adı", "Kaydet", "Kapat",
                        maxLength: 80, initialValue: item.Name))?.Trim();
                    if (string.IsNullOrEmpty(name) || name == item.Name) return;
                    store.UpdateGroup(item with { Name = name });
                    if (group == id) heading.Text = name;
                    break;
                case "Renk":
                    var color = await DisplayActionSheet("Grup rengi", "Kapat", null, ["Renksiz", .. NotePage.NoteColors.Select(tone => tone.Name)]);
                    if (color is null or "Kapat") return;
                    store.UpdateGroup(item with { Color = NotePage.NoteColors.FirstOrDefault(tone => tone.Name == color)?.Value });
                    break;
                case var action when action == archive:
                    store.UpdateGroup(item with { Archived = !item.Archived });
                    break;
                case "Yukarı taşı":
                case "Aşağı taşı":
                    if (!MoveGroup(item, choice == "Yukarı taşı" ? -1 : 1)) return;
                    break;
                case "Sil":
                    if (!await DisplayAlert("Grubu sil", "Notlar Grupsuz’a taşınacak.", "Sil", "Vazgeç")) return;
                    var affected = store.DeleteGroup(id);
                    if (group == id) Select("Grupsuz", 0, 0);
                    drawer.IsVisible = false;
                    OfferGroupUndo(item, affected);
                    Refresh();
                    return;
                default:
                    return;
            }
            drive.Changed();
            Refresh();
            OpenDrawer();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            await DisplayAlert("Grup işlemi", "Grup güncellenemedi. Aynı adlı bir grup olup olmadığını kontrol et.", "Kapat");
        }
    }

    private bool MoveGroup(NoteGroup item, int offset)
    {
        var groups = store.Groups().Where(group => group.Archived == item.Archived && group.ParentUuid == item.ParentUuid).ToList();
        var index = groups.FindIndex(group => group.Id == item.Id);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= groups.Count) return false;
        (groups[index], groups[target]) = (groups[target], groups[index]);
        store.Atomic(() =>
        {
            for (var i = 0; i < groups.Count; i++)
                if (groups[i].Order != i + 1) store.UpdateGroup(groups[i] with { Order = i + 1 });
        });
        return true;
    }

    private void OfferGroupUndo(NoteGroup item, long[] affected)
    {
        groupUndoBar.Children.Clear();
        groupUndoBar.SetAppThemeColor(VisualElement.BackgroundColorProperty, MobileStyle.LightBar, MobileStyle.DarkBar);
        var undo = MobileStyle.Button("Geri al", async () =>
        {
            try
            {
                store.RestoreGroup(item, affected);
                Refresh();
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                await DisplayAlert("Grup işlemi", "Grup geri alınamadı.", "Kapat");
            }
            finally
            {
                groupUndoBar.IsVisible = false;
                drive.Changed();
            }
        });
        groupUndoBar.Children.Add(MobileStyle.Label("Grup silindi."));
        groupUndoBar.Children.Add(undo);
        groupUndoBar.IsVisible = true;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(5), () =>
        {
            if (groupUndoBar.Children.Contains(undo)) groupUndoBar.IsVisible = false;
            drive.Changed();
            return false;
        });
    }
}
