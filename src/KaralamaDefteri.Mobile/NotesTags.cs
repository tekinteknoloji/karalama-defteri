namespace KaralamaDefteri.Mobile;

internal sealed partial class NotesPage
{
    private Func<bool>? closeTagsPanel;

    private View CreateTagsPanel(Button toggle)
    {
        var input = MobileStyle.Entry("Etiketlerde ara");
        var close = MobileStyle.Button("×");
        SemanticProperties.SetDescription(close, "Etiket panelini kapat");
        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6 };
        header.Add(input);
        header.Add(close, 1);
        var results = new HorizontalStackLayout { Spacing = 5 };
        var scroller = new ScrollView
        {
            Content = results, HeightRequest = 38, Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never
        };
        scroller.SetAppThemeColor(VisualElement.BackgroundColorProperty, MobileStyle.LightBar, MobileStyle.DarkBar);
        var panel = new Border
        {
            IsVisible = false, HorizontalOptions = LayoutOptions.Fill, MaximumWidthRequest = 660,
            Padding = 8, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = new VerticalStackLayout
            {
                Spacing = 6, Children = { header, scroller }
            }
        };
        panel.SetAppThemeColor(VisualElement.BackgroundColorProperty, MobileStyle.LightBar, MobileStyle.DarkBar);
        panel.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush(MobileStyle.LightLine),
            new SolidColorBrush(MobileStyle.DarkLine));
        string[] tags = [];

        bool Close()
        {
            if (!panel.IsVisible) return false;
            input.Unfocus();
            panel.IsVisible = false;
            toggle.Text = "Etiketler ▾";
            return true;
        }
        void Filter()
        {
            var query = NoteStore.Fold(input.Text?.Trim().TrimStart('#'));
            results.Children.Clear();
            foreach (var tag in tags.Where(tag => NoteStore.Fold(tag).Contains(query, StringComparison.Ordinal)))
            {
                var button = MobileStyle.Button("#" + tag, () =>
                {
                    Close();
                    search.Text = "#" + tag;
                });
                button.FontSize = 12;
                button.MinimumHeightRequest = 34;
                button.Padding = new Thickness(8, 2);
                button.VerticalOptions = LayoutOptions.Center;
                results.Children.Add(button);
            }
            if (results.Children.Count == 0)
            {
                var empty = MobileStyle.Label("Etiket bulunamadı.");
                empty.VerticalOptions = LayoutOptions.Center;
                results.Children.Add(empty);
            }
        }
        closeTagsPanel = Close;
        close.Clicked += (_, _) => Close();
        input.TextChanged += (_, _) => Filter();
        toggle.Clicked += (_, _) =>
        {
            if (Close() || !features.Enabled(Feature.Tags)) return;
            tags = store.Tags();
            input.Text = "";
            Filter();
            toggle.Text = "Etiketler ▴";
            panel.IsVisible = true;
        };
        toggle.IsVisible = features.Enabled(Feature.Tags);
        Appearing += (_, _) =>
        {
            toggle.IsVisible = features.Enabled(Feature.Tags);
            if (!toggle.IsVisible) Close();
        };
        return panel;
    }
}
