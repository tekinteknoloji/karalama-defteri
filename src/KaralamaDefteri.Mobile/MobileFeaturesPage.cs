namespace KaralamaDefteri.Mobile;

internal sealed class MobileFeaturesPage : ContentPage
{
    internal MobileFeaturesPage(FeatureFlags features, MobileDriveSync drive)
    {
        Title = "Özellikler";
        MobileStyle.Page(this);
        var layout = new VerticalStackLayout { Padding = 16, Spacing = 8 };
        Add(Feature.Board, "Pano");
        Add(Feature.Authenticator, "Doğrulayıcı");
        MobileStyle.SetContent(this, layout);

        void Add(Feature feature, string name)
        {
            var label = MobileStyle.Label(name, 16, true);
            label.VerticalOptions = LayoutOptions.Center;
            var toggle = new Switch { IsToggled = features.Selected(feature) };
            SemanticProperties.SetDescription(toggle, name);
            toggle.Toggled += (_, e) => { features.Set(feature, e.Value); drive.Changed(); };
            var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
            row.Add(label);
            row.Add(toggle, 1);
            layout.Children.Add(row);
        }
    }
}
