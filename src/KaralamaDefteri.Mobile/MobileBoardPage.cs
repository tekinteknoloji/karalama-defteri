namespace KaralamaDefteri.Mobile;

internal sealed class MobileBoardPage : ContentPage
{
    private readonly NoteStore store;
    private readonly MobileDriveSync drive;
    private readonly long? group;
    private readonly Func<Card, Task> open;
    private readonly HorizontalStackLayout columns = new() { Padding = 12, Spacing = 10 };

    internal MobileBoardPage(NoteStore store, MobileDriveSync drive, long? group, Func<Card, Task> open)
    {
        this.store = store;
        this.drive = drive;
        this.group = group;
        this.open = open;
        Title = "Pano";
        MobileStyle.Page(this);
        MobileStyle.SetContent(this, new ScrollView { Orientation = ScrollOrientation.Both, Content = columns });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        drive.LocalChanged += Refresh;
        Refresh();
    }

    protected override void OnDisappearing()
    {
        drive.LocalChanged -= Refresh;
        base.OnDisappearing();
    }

    private void Refresh()
    {
        columns.Children.Clear();
        var lanes = store.BoardColumns(store.BoardGroup(group));
        if (lanes.Length == 0) return;
        var positions = store.BoardPositions();
        var cards = store.Search("", 0, null, group: group).OrderBy(card => positions[card.Id].Order).ToArray();
        var valid = lanes.Select(lane => lane.Id).ToHashSet();
        foreach (var lane in lanes)
        {
            var box = new VerticalStackLayout { WidthRequest = 230, Spacing = 8, Padding = 10 };
            box.SetAppThemeColor(VisualElement.BackgroundColorProperty, MobileStyle.LightBar, MobileStyle.DarkBar);
            box.Children.Add(MobileStyle.Label(lane.Name, 17, true));
            foreach (var card in cards.Where(card => positions[card.Id].Column is { } id && valid.Contains(id) ? id == lane.Id : lane.Id == lanes[0].Id))
                box.Children.Add(MobileStyle.Button(card.Title, async () =>
                {
                    if (!new FeatureFlags(store).Enabled(Feature.Board)) return;
                    var options = lanes.Where(item => item.Id != lane.Id).ToArray();
                    var choice = await DisplayActionSheet(card.Title, "Kapat", null, ["Notu aç", .. options.Select(item => "Taşı: " + item.Name)]);
                    if (choice == "Notu aç") await open(card);
                    else if (options.FirstOrDefault(item => "Taşı: " + item.Name == choice) is { } target)
                    {
                        var current = store.BoardPositions();
                        var ids = store.ActiveBoardCards(group).Where(id => id != card.Id && current[id].Column == target.Id)
                            .OrderBy(id => current[id].Order).Append(card.Id).ToArray();
                        store.OrderBoardCards(target.Id, ids);
                        drive.Changed();
                        Refresh();
                    }
                }));
            columns.Children.Add(box);
        }
    }
}
