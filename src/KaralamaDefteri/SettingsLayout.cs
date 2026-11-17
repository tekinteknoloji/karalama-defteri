using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private static StackPanel SettingsCard(Panel parent, string title)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,4) });
        var card = new Border { Child = body, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Padding = new Thickness(16), Margin = new Thickness(0,0,0,24) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        parent.Children.Add(card);
        return body;
    }

    private static void SettingsField(Panel card, string label, Control field, double width = 210)
    {
        var row = new DockPanel { Margin = new Thickness(0,8,0,4) };
        field.Width = width;
        AutomationProperties.SetName(field, label);
        Add(row, field, Dock.Right);
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0,0,12,0) });
        card.Children.Add(row);
    }

    private static void SettingsAction(Panel card, Button button)
    {
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.MinWidth = 96;
        button.Margin = new Thickness(0,8,0,0);
        card.Children.Add(button);
    }
}
