using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace KaralamaDefteri;

public sealed partial class EditorPane : DockPanel
{
    private readonly Center center;
    private readonly TextEditor editor;
    private readonly TextBox title;
    private readonly Button otpBadge;
    private readonly DockPanel tagRow = new();
    private readonly TextBlock position = new();
    private readonly TextBlock saved = new();
    private readonly ComboBox language;
    private readonly DockPanel find = new() { Visibility = Visibility.Collapsed };
    private readonly TextBox query = new() { Width = 155 };
    private readonly TextBox replacement = new() { Width = 155 };
    private readonly CheckBox regex = new() { Content = "Regex" };
    private readonly CheckBox whole = new() { Content = "Tam kelime" };
    private readonly CheckBox matchCase = new() { Content = "Harf duyarlı" };
    private readonly DispatcherTimer detection = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? pendingDetection;
    private string detected = "text";
    private bool updating;
    private bool closed;
    private static readonly string[] Syntaxes = ["json", "xml", "powershell", "sql", "ini", "log", "text"];
    public Note Note { get; }
    public TabItem Tab { get; }
    internal void RefreshFeatureVisibility()
    {
        tagRow.Visibility = center.Features.Enabled(Feature.Tags) ? Visibility.Visible : Visibility.Collapsed;
        center.RefreshAuthenticatorBadge(otpBadge, Note);
        RefreshChecklist();
    }

    public int Offset => editor.CaretOffset;
    internal ICSharpCode.AvalonEdit.Document.TextDocument SharedDocument => editor.Document;

    public EditorPane(Center center, Note note)
    {
        this.center = center;
        Note = note;
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var caption = new TextBlock { MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        caption.SetBinding(TextBlock.TextProperty, new Binding(nameof(Note.Title)) { Source = note });
        header.Children.Add(caption);
        var noteMenu = new ContextMenu();
        var pin = new MenuItem { Header = "Masaüstüne iğnele" };
        pin.Click += (_, _) => center.PinCard(note.Id);
        noteMenu.Items.Add(pin);
        var checklistMenu = new MenuItem();
        checklistMenu.Click += (_, _) => ToggleChecklist();
        noteMenu.Opened += (_, _) =>
        {
            pin.Visibility = center.Features.Enabled(Feature.DesktopPins) ? Visibility.Visible : Visibility.Collapsed;
            checklistMenu.Visibility = center.Features.Enabled(Feature.Checklist) ? Visibility.Visible : Visibility.Collapsed;
            checklistMenu.Header = Note.Checklist ? "Düz metne çevir" : "Listeye çevir";
        };
        noteMenu.Items.Add(checklistMenu);
        header.ContextMenu = noteMenu;
        var close = Center.Button("×", () => center.CloseTab(this));
        close.ToolTip = "Sekmeyi kapat";
        header.Children.Add(close);
        Tab = new TabItem { Header = header, Content = this, Tag = note.Id };

        title = new TextBox { Text = note.Title, Margin = new Thickness(5), Padding = new Thickness(4) };
        AutomationProperties.SetName(title, "Not başlığı");
        var heading = new DockPanel();
        otpBadge = Center.Button("Kod", async () => await center.CopyNoteAuthenticator(note));
        DockPanel.SetDock(otpBadge, Dock.Right);
        heading.Children.Add(otpBadge);
        heading.IsVisibleChanged += (_, _) => center.RefreshAuthenticatorBadge(otpBadge, note);
        center.Activated += RefreshOnActivation;
        Unloaded += (_, _) => center.Activated -= RefreshOnActivation;
        void RefreshOnActivation(object? sender, EventArgs args) => center.RefreshAuthenticatorBadge(otpBadge, note);
        var colors = CreateColorDots();
        DockPanel.SetDock(colors, Dock.Right);
        heading.Children.Add(colors);
        DockPanel.SetDock(groupBox, Dock.Right);
        heading.Children.Add(groupBox);
        heading.Children.Add(title);
        Add(heading, Dock.Top);
        ConfigureGroupBox();
        var tags = new TextBox { Margin = new Thickness(5), ToolTip = "Etiket eklemek için #etiket yazıp Enter’a basın." };
        AutomationProperties.SetName(tags, "Not etiketleri");

        var tagsLabel = new TextBlock { Text = "Etiketler", Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
        SetDock(tagsLabel, Dock.Left);
        tagRow.Children.Add(tagsLabel);
        tagRow.Children.Add(tags);
        Add(tagRow, Dock.Top);
        void RefreshTags(string content) => tags.Text = string.Join(" ", center.NoteTags(content).Select(tag => "#" + tag));
        RefreshTags(note.Content);
        tags.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var names = tags.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => "#" + name.TrimStart('#'));
            var existing = center.NoteTags(editor!.Text);
            var additions = center.NoteTags(string.Join(" ", names)).Except(existing).ToArray();
            if (additions.Length != 0)
                editor.Document.Insert(editor.Document.TextLength, Environment.NewLine + string.Join(" ", additions.Select(name => "#" + name)));
            RefreshTags(editor.Text);
            e.Handled = true;
        };
        var status = new DockPanel { Margin = new Thickness(6) };
        language = new ComboBox { MinWidth = 130, ItemsSource = new[] { "Otomatik", "JSON", "XML", "PowerShell", "SQL", "INI", "Log", "Düz metin" } };
        language.SelectedIndex = Array.IndexOf(Syntaxes, note.Syntax) + 1;
        AutomationProperties.SetName(language, "Sözdizimi");
        DockPanel.SetDock(language, Dock.Right);
        status.Children.Add(language);
        DockPanel.SetDock(saved, Dock.Right);
        saved.Margin = new Thickness(10, 0, 10, 0);
        status.Children.Add(saved);
        status.Children.Add(position);
        Add(status, Dock.Bottom);

        var findRows = new StackPanel { Margin = new Thickness(5) };
        var fields = new WrapPanel();
        fields.Children.Add(new TextBlock { Text = "Bul", Margin = new Thickness(3) });
        fields.Children.Add(query);
        fields.Children.Add(Center.Button("Sonrakini bul", FindNext));
        fields.Children.Add(Center.Button("Kapat", HideFind));
        findRows.Children.Add(fields);
        var replaceRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        replaceRow.Children.Add(new TextBlock { Text = "Yeni metin", Margin = new Thickness(3) });
        replaceRow.Children.Add(replacement);
        replaceRow.Children.Add(Center.Button("Değiştir", ReplaceNext));
        replaceRow.Children.Add(Center.Button("Tümünü değiştir", ReplaceAll));
        findRows.Children.Add(replaceRow);
        var options = new WrapPanel();
        foreach (var check in new[] { regex, whole, matchCase }) { check.Margin = new Thickness(3, 4, 8, 0); options.Children.Add(check); }
        findRows.Children.Add(options);
        find.Children.Add(findRows);
        Add(find, Dock.Top);
        query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FindNext(); e.Handled = true; } };

        editor = new TextEditor { ShowLineNumbers = true, WordWrap = center.EditorWrap, Text = note.Content };
        editor.Options.EnableRectangularSelection = true;
        editor.CaretOffset = Math.Clamp(note.Caret, 0, editor.Document.TextLength);
        editor.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            editor.FontSize = Math.Clamp(editor.FontSize + Math.Sign(e.Delta), 9, 40);
            e.Handled = true;
        };
        Children.Add(editor);
        InitializeChecklist();
        RefreshFeatureVisibility();
        ConfigurePictures();
        ApplyAppearance();
        detection.Tick += (_, _) => { detection.Stop(); Detect(); };
        editor.TextChanged += (_, _) =>
        {
            RefreshChecklist();
            if (!tags.IsKeyboardFocusWithin) RefreshTags(editor.Text);
            if (!Note.Manual) SetAutoTitle();
            MarkDirty();
            detection.Stop();
            detection.Start();
        };
        editor.TextArea.Caret.PositionChanged += (_, _) => { UpdateStatus(); center.ScheduleSave(); };
        title.TextChanged += (_, _) =>
        {
            if (updating) return;
            Note.Manual = !string.IsNullOrWhiteSpace(title.Text);
            if (Note.Manual) Note.Title = title.Text;
            MarkDirty();
        };
        title.LostKeyboardFocus += (_, _) => { if (!Note.Manual) SetAutoTitle(); };
        language.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            Note.Syntax = language.SelectedIndex > 0 ? Syntaxes[language.SelectedIndex - 1] : null;
            MarkDirty();
            ApplySyntax();
            if (Note.Syntax == null) Detect();
        };
        UpdateStatus();
        Detect();
    }

    private void Add(UIElement item, Dock dock) { SetDock(item, dock); Children.Add(item); }

    private void SetAutoTitle()
    {
        var document = editor.Document;
        var derived = NoteStore.AutoTitle("", Note.Created);
        foreach (var line in document.Lines)
        {
            var text = ImageMarkers.Strip(document.GetText(line.Offset, Math.Min(line.Length, 512))).Trim();
            if (text.Length == 0 && line.Length > 512)
                text = ImageMarkers.Strip(document.GetText(line.Offset, line.Length)).Trim();
            if (text.Length == 0) continue;
            derived = NoteStore.AutoTitle(text, Note.Created);
            break;
        }
        if (Note.Title == derived && title.Text == derived) return;
        Note.Title = derived;
        if (title.IsKeyboardFocusWithin) return;
        updating = true;
        title.Text = derived;
        updating = false;
    }

    private void MarkDirty()
    {
        Note.Dirty = true;
        UpdateStatus();
        center.ScheduleSave();
    }

    public void ReadContent()
    {
        Note.Content = editor.Text;
        if (!Note.Manual) Note.Title = NoteStore.AutoTitle(Note.Content, Note.Created);
    }

    public void UpdateStatus()
    {
        var caret = editor.TextArea.Caret;
        position.Text = $"Satır {caret.Line} · Sütun {caret.Column} · {editor.Document.TextLength:N0} karakter · {groupBox.Text}";
        saved.Text = Note.Dirty ? "Kaydediliyor…" : "Kaydedildi ✓";
    }

    public void ApplyAppearance()
    {
        editor.FontFamily = new FontFamily(center.EditorFont);
        editor.FontSize = center.EditorSize;
        editor.Background = Theme.Brush("Surface");
        editor.Foreground = Theme.Brush("Ink");
        editor.LineNumbersForeground = Theme.Brush("Muted");
        editor.TextArea.Background = editor.Background;
        editor.TextArea.Foreground = editor.Foreground;
        editor.TextArea.SelectionBrush = Theme.Brush("Selection");
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.SelectionForeground = editor.Foreground;
        editor.TextArea.Caret.CaretBrush = Theme.Brush("Accent");
        ApplySyntax();
    }

    public void SetWrap(bool enabled) => editor.WordWrap = enabled;
    public void Stop()
    {
        closed = true;
        detection.Stop();
        pendingDetection?.Cancel();
    }

    private async void Detect()
    {
        pendingDetection?.Cancel();
        if (Note.Syntax != null || closed) return;
        var snapshot = editor.Document.CreateSnapshot();
        var request = new CancellationTokenSource();
        pendingDetection = request;
        try
        {
            var syntax = await Task.Run(() => Editing.Detect(snapshot.Text), request.Token);
            if (closed || request.IsCancellationRequested || Note.Syntax != null) return;
            detected = syntax;
            ApplySyntax();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (pendingDetection == request) pendingDetection = null;
            request.Dispose();
        }
    }

    private void ApplySyntax()
    {
        var syntax = Note.Syntax ?? detected;
        editor.SyntaxHighlighting = SyntaxColors.Get(syntax, Theme.IsDark);
        language.ToolTip = $"Sözdizimi · {language.Items[Array.IndexOf(Syntaxes, syntax) + 1]}";
        var auto = $"Otomatik · {language.Items[Array.IndexOf(Syntaxes, detected) + 1]}";
        if (!Equals(language.Items[0], auto))
        {
            var selected = language.SelectedIndex;
            updating = true;
            language.ItemsSource = new[] { auto, "JSON", "XML", "PowerShell", "SQL", "INI", "Log", "Düz metin" };
            language.SelectedIndex = selected;
            updating = false;
        }
    }

    public void FocusText(string search)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (closed) return;
            editor.Focus();
            if (string.IsNullOrWhiteSpace(search)) return;
            var match = NoteStore.FirstMatch(editor.Text, search);
            if (!match.Success) return;
            Select(match.Index, match.Length);
            editor.CaretOffset = match.Index;
        }));
    }

    public bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control && key is (Key.F or Key.H))
        {
            find.Visibility = Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(editor.SelectedText)) query.Text = editor.SelectedText;
            (key == Key.F ? query : replacement).Focus();
            return true;
        }
        if (modifiers == ModifierKeys.Control && key == Key.W) { center.CloseTab(this); return true; }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.F)
        {
            try
            {
                var original = editor.Text;
                var formatted = Editing.Format(original, Note.Syntax ?? Editing.Detect(original));
                editor.Document.Replace(0, editor.Document.TextLength, formatted);
            }
            catch (Exception error) when (error is System.Text.Json.JsonException or System.Xml.XmlException or InvalidOperationException)
            {
                center.Notify($"Biçimlendirilemedi · {error.Message}");
            }
            return true;
        }
        if (modifiers == ModifierKeys.Alt && key == Key.Z) { editor.WordWrap = !editor.WordWrap; return true; }
        if (key == Key.Escape && find.Visibility == Visibility.Visible) { HideFind(); return true; }
        return false;
    }

    private void HideFind() { find.Visibility = Visibility.Collapsed; editor.Focus(); }
    private void Select(int index, int length)
    {
        editor.Select(index, length);
        editor.ScrollToLine(editor.Document.GetLineByOffset(index).LineNumber);
    }

    private void WithPattern(Action<Regex, string> action)
    {
        if (query.Text.Length == 0) return;
        try { action(Editing.Pattern(query.Text, regex.IsChecked == true, whole.IsChecked == true, matchCase.IsChecked == true), editor.Text); }
        catch (ArgumentException error) { center.Notify($"Arama deseni geçersiz · {error.Message}"); }
        catch (RegexMatchTimeoutException) { center.Notify("Arama zaman sınırını aştı."); }
    }

    private int lastZeroMatch = -1;
    private void FindNext() => WithPattern((pattern, text) =>
    {
        var offset = editor.SelectionStart + editor.SelectionLength;
        if (offset == lastZeroMatch) offset++;
        var match = offset <= text.Length ? pattern.Match(text, offset) : Match.Empty;
        if (!match.Success) match = pattern.Match(text);
        if (!match.Success) { center.Notify("Eşleşme bulunamadı."); return; }
        lastZeroMatch = match.Length == 0 ? match.Index : -1;
        Select(match.Index, match.Length);
    });
    private void ReplaceNext() => WithPattern((pattern, text) =>
    {
        var match = pattern.Match(text, editor.SelectionStart);
        if (!match.Success || match.Index != editor.SelectionStart || match.Length != editor.SelectionLength) { FindNext(); return; }
        var value = regex.IsChecked == true ? match.Result(replacement.Text) : replacement.Text;
        editor.Document.Replace(match.Index, match.Length, value);
        Select(match.Index, value.Length);
        FindNext();
    });
    private void ReplaceAll() => WithPattern((pattern, text) =>
    {
        var count = 0;
        var output = pattern.Replace(text, match =>
        {
            count++;
            return regex.IsChecked == true ? match.Result(replacement.Text) : replacement.Text;
        });
        if (count > 0) editor.Document.Replace(0, text.Length, output);
        center.Notify($"{count} eşleşme değiştirildi.");
    });
}
