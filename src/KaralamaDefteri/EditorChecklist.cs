using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KaralamaDefteri;

public sealed partial class EditorPane
{
    private readonly StackPanel checklistRows = new();
    private readonly TextBox newItem = new() { ToolTip = "Yeni madde yazın ve Enter’a basın" };
    private ScrollViewer? checklistView;
    private Button? checklistSwitch;
    private bool textView;

    private void InitializeChecklist()
    {
        Children.Remove(editor);
        checklistSwitch = Center.Button("Metni düzenle", () => { textView = !textView; RefreshChecklist(); });
        Add(checklistSwitch, Dock.Top);
        SetDock(newItem, Dock.Top);
        var panel = new DockPanel { Margin = new Thickness(6) };
        panel.Children.Add(newItem);
        panel.Children.Add(checklistRows);
        checklistView = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new Grid();
        content.Children.Add(editor);
        content.Children.Add(checklistView);
        Children.Add(content);
        newItem.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(newItem.Text)) return;
            var prefix = editor.Document.TextLength == 0 ? "" : "\n";
            editor.Document.Insert(editor.Document.TextLength, prefix + "[ ] " + newItem.Text.Trim());
            newItem.Clear();
            e.Handled = true;
        };
        RefreshChecklist();
    }

    internal void ToggleChecklist()
    {
        if (!center.Features.Enabled(Feature.Checklist)) return;
        Note.Checklist = !Note.Checklist;
        textView = false;
        editor.Document.Replace(0, editor.Document.TextLength, ChecklistText.Convert(editor.Text, Note.Checklist));
        MarkDirty();
        RefreshChecklist();
        center.Flush();
    }

    private void RefreshChecklist()
    {
        if (checklistView == null || checklistSwitch == null) return;
        var showChecklist = Note.Checklist && center.Features.Enabled(Feature.Checklist);
        checklistSwitch.Visibility = showChecklist ? Visibility.Visible : Visibility.Collapsed;
        checklistSwitch.Content = textView ? "Listeyi göster" : "Metni düzenle";
        checklistView.Visibility = showChecklist && !textView ? Visibility.Visible : Visibility.Collapsed;
        editor.Visibility = checklistView.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (checklistView.Visibility != Visibility.Visible) return;
        checklistRows.Children.Clear();
        var document = editor.Document;
        var lines = ChecklistText.Lines(editor.Text);
        foreach (var index in Enumerable.Range(0, lines.Length).OrderBy(i => ChecklistText.Complete(lines[i])))
        {
            var line = lines[index];
            if (!ChecklistText.Item(line))
            {
                if (!string.IsNullOrWhiteSpace(line)) checklistRows.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
                continue;
            }
            var complete = ChecklistText.Complete(line);
            var label = new TextBlock { Text = ChecklistText.Label(line), TextWrapping = TextWrapping.Wrap };
            if (complete) label.TextDecorations = TextDecorations.Strikethrough;
            var box = new CheckBox { Content = label, IsChecked = complete, Margin = new Thickness(5) };
            box.Click += (_, _) => document.Replace(document.GetLineByNumber(index + 1).Offset, 4, complete ? "[ ] " : "[x] ");
            checklistRows.Children.Add(box);
        }
    }
}
