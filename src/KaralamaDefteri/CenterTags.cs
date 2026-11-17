using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private readonly Button tagsToggle = Button("Etiketler ▾");
    private Grid? tagPanel;
    private readonly Button clearTag = new() { Content = "×", Visibility = Visibility.Collapsed };

    private void UpdateTagsToggle()
    {
        var enabled = Features.Enabled(Feature.Tags);
        if (!enabled && tagPanel != null)
        {
            cardBody.Children.Remove(tagPanel);
            tagPanel = null;
        }
        tagsToggle.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled) return;
        var count = store.Tags().Length;
        var label = $"Etiketler ({count})";
        tagsToggle.Content = label + (tagPanel == null ? " ▾" : " ▴");
        tagsToggle.IsEnabled = count > 0;
        tagsToggle.ToolTip = tagPanel == null ? "Etiketleri aç" : "Etiketleri kapat";
        AutomationProperties.SetName(tagsToggle, label);
    }

    private void CloseTags(bool restoreFocus = true)
    {
        if (tagPanel == null) return;
        cardBody.Children.Remove(tagPanel);
        tagPanel = null;
        UpdateTagsToggle();
        if (restoreFocus) tagsToggle.Focus();
    }

    private void OpenTags()
    {
        var tags = store.Tags();
        if (tags.Length == 0) return;
        var overlay = tagPanel = new Grid();
        Grid.SetColumnSpan(overlay, 3);
        Panel.SetZIndex(overlay, 1);
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)) };
        backdrop.MouseLeftButtonDown += (_, _) => CloseTags();
        overlay.Children.Add(backdrop);
        var layout = new DockPanel { LastChildFill = true };
        var panel = new Border
        {
            Child = layout, MaxWidth = 660, MaxHeight = 360, Margin = new Thickness(10),
            Padding = new Thickness(12), VerticalAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8)
        };
        panel.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        overlay.Children.Add(panel);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var close = Button("×", () => CloseTags());
        AutomationProperties.SetName(close, "Etiket panelini kapat");
        Add(header, close, Dock.Right);
        var input = new TextBox { ToolTip = "Etiketlerde ara" };
        AutomationProperties.SetName(input, "Etiketlerde ara");
        Add(header, new TextBlock { Text = "Etiketlerde ara", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0) }, Dock.Left);
        header.Children.Add(input);
        Add(layout, header, Dock.Top);
        var results = new WrapPanel();
        layout.Children.Add(new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        string[] matches = [];

        void Choose(string tag)
        {
            CloseTags(restoreFocus: false);
            var filter = "#" + tag;
            clearTag.Tag = filter;
            search.Text = filter;
            clearTag.Visibility = Visibility.Visible;
            search.Focus();
            search.CaretIndex = search.Text.Length;
        }
        void Filter()
        {
            var query = NoteStore.Fold(input.Text.Trim().TrimStart('#'));
            matches = tags.Where(tag => NoteStore.Fold(tag).Contains(query, StringComparison.Ordinal)).ToArray();
            results.Children.Clear();
            foreach (var tag in matches)
            {
                var button = Button("#" + tag, () => Choose(tag));
                button.Margin = new Thickness(2, 0, 5, 6);
                results.Children.Add(button);
            }
            if (matches.Length == 0) results.Children.Add(new TextBlock { Text = "Etiket bulunamadı.", Margin = new Thickness(3) });
        }
        input.TextChanged += (_, _) => Filter();
        overlay.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CloseTags(); }
            else if (e.Key == Key.Enter && input.IsKeyboardFocused && matches.Length > 0)
            { e.Handled = true; Choose(matches[0]); }
        };
        Filter();
        cardBody.Children.Add(overlay);
        UpdateTagsToggle();
        input.Focus();
    }
}
