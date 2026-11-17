using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace KaralamaDefteri;

public sealed partial class EditorPane
{
    private readonly List<(Button Button, Ellipse Dot)> colorDots = [];

    private StackPanel CreateColorDots()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < Center.Colors.Length; i++)
        {
            var color = Center.Colors[i];
            var label = $"Not rengi · {Center.ColorNames[i]}";
            var button = Center.Button(label, () => center.SetNoteColor(Note.Id, color));
            var dot = new Ellipse { Width = 12, Height = 12, StrokeThickness = 1 };
            dot.SetResourceReference(Shape.FillProperty, Center.ColorBrushes[i]);
            button.Content = dot;
            button.Padding = new Thickness(3);
            button.Margin = new Thickness(1);
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
            row.Children.Add(button);
            colorDots.Add((button, dot));
        }
        RefreshColorDots();
        return row;
    }

    internal void RefreshColorDots()
    {
        for (var i = 0; i < colorDots.Count; i++)
        {
            var selected = Center.Colors[i] == Note.Color;
            colorDots[i].Dot.SetResourceReference(Shape.StrokeProperty, selected ? "InkBrush" : "MutedBrush");
            colorDots[i].Dot.StrokeThickness = selected ? 2 : 1;
            AutomationProperties.SetHelpText(colorDots[i].Button, selected ? "Seçili" : "");
        }
    }
}
