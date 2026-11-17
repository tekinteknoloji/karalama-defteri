using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private const string NotesDrag = "KaralamaDefteri.Notes";
    private const string GroupDrag = "KaralamaDefteri.Group";
    private readonly StackPanel groupRows = new();
    private readonly HashSet<long> selectedCards = [];
    private readonly List<long> cardOrder = [];
    private long? selectionAnchor;
    private TextBox? groupEdit;
    private readonly HashSet<string> expandedGroups = new(StringComparer.Ordinal);
    private bool splitGroups;
    private long? SelectedGroup { get; set; }
    private long? NewNoteGroup => scope.SelectedIndex == 0 && SelectedGroup > 0 ? SelectedGroup : null;
    internal string[] NoteTags(string content) => store.TagNames(content);

    private readonly StackPanel sidebarBottom = new();
    private readonly List<Button> sidebarRows = [];
    private readonly Dictionary<long, Button> groupButtons = [];
    private ScrollViewer? sidebarScroll;
    private Button? sidebarToggle;
    private int newGroupIndex;

    private UIElement BuildSidebar()
    {
        if (long.TryParse(store.Setting("group"), out var selected)) SelectedGroup = selected;
        if (SelectedGroup > 0)
        {
            var groups = store.Groups().ToDictionary(group => group.Uuid);
            var group = groups.Values.FirstOrDefault(group => group.Id == SelectedGroup);
            while (group?.ParentUuid is { } parent && expandedGroups.Add(parent))
                group = groups.GetValueOrDefault(parent);
        }
        if (int.TryParse(store.Setting("scope"), out var current)) scope.SelectedIndex = Math.Clamp(current, 0, 2);
        splitGroups = store.Setting("splitGroups") == "1";
        var panel = new DockPanel { Width = 206, Margin = new Thickness(0, 0, 10, 0) };
        panel.SetResourceReference(Panel.BackgroundProperty, "BarBrush");
        sidebarToggle = Button("‹");
        sidebarToggle.ClearValue(PaddingProperty);
        sidebarToggle.SetResourceReference(StyleProperty, "SidebarIcon");
        sidebarToggle.HorizontalAlignment = HorizontalAlignment.Right;
        sidebarToggle.Margin = new Thickness(0, 8, 8, 0);
        sidebarScroll = new ScrollViewer { Content = groupRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(10, 0, 10, 0) };
        sidebarBottom.Margin = new Thickness(10, 6, 10, 10);
        sidebarToggle.Click += (_, _) => SetCollapsed(sidebarScroll.Visibility == Visibility.Visible);
        Add(panel, sidebarToggle, Dock.Top);
        Add(panel, sidebarBottom, Dock.Bottom);
        panel.Children.Add(sidebarScroll);
        SetCollapsed(false);
        return panel;

        void SetCollapsed(bool collapsed)
        {
            sidebarScroll.Visibility = sidebarBottom.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            panel.Width = collapsed ? 42 : 206;
            sidebarToggle.Content = collapsed ? "›" : "‹";
            sidebarToggle.ToolTip = collapsed ? "Kenar çubuğunu genişlet" : "Kenar çubuğunu daralt";
            System.Windows.Automation.AutomationProperties.SetName(sidebarToggle, (string)sidebarToggle.ToolTip);
        }
    }

    private void SetSplitGroups(bool enabled)
    {
        splitGroups = enabled;
        store.Set("splitGroups", enabled ? "1" : "0");
        RefreshCards();
    }

    private void RefreshSidebar()
    {
        if (groupEdit != null) return;
        var offset = sidebarScroll?.VerticalOffset ?? 0;
        var focused = sidebarRows.FirstOrDefault(button => button.IsKeyboardFocused)?.Tag;
        groupRows.Children.Clear();
        sidebarBottom.Children.Clear();
        sidebarRows.Clear();
        groupButtons.Clear();
        var groups = store.Groups();
        if (SelectedGroup > 0 && !groups.Any(group => group.Id == SelectedGroup)) SelectedGroup = null;
        groupRows.Children.Add(Section("GÖRÜNÜMLER"));
        Row(groupRows, "Tümü", "▦", null, 0);
        Row(groupRows, "Grupsuz", "○", 0, 0);
        var create = Button("+", () => EditGroup(null));
        create.ClearValue(PaddingProperty);
        create.ClearValue(MarginProperty);
        create.SetResourceReference(StyleProperty, "SidebarIcon");
        create.ToolTip = "Yeni grup";
        System.Windows.Automation.AutomationProperties.SetName(create, "Yeni grup");
        groupRows.Children.Add(Section("GRUPLAR", create));
        newGroupIndex = groupRows.Children.Count;
        GroupSection(false);
        if (groups.Any(g => g.Archived))
        {
            groupRows.Children.Add(Section("ARŞİVLENMİŞ GRUPLAR"));
            GroupSection(true);
        }
        if (Features.Enabled(Feature.Authenticator))
            AddSidebarRow(sidebarBottom, "Doğrulayıcı", "\uE72E", OpenAuthenticator, glyphFont: "Segoe MDL2 Assets");
        if (Features.Enabled(Feature.BoardDeadlines))
            AddSidebarRow(sidebarBottom, "Yaklaşanlar", "◷", () => ShowUpcoming("hafta"));
        Row(sidebarBottom, "Arşiv", "▤", null, 1);
        Row(sidebarBottom, "Çöp kutusu", "\uE74D", null, 2, "Segoe MDL2 Assets");
        sidebarScroll?.ScrollToVerticalOffset(offset);
        if (focused != null) sidebarRows.FirstOrDefault(button => Equals(button.Tag, focused))?.Focus();

        Button Row(Panel target, string caption, string glyph, long? id, int category, string? font = null,
            int? count = null, string? color = null)
        {
            var selected = SelectedGroup == id && scope.SelectedIndex == category;
            var row = AddSidebarRow(target, caption, glyph, () => SelectGroup(id, category), selected, count, color, font);
            row.Tag = (id, category);
            if (category == 0 && id != null)
            {
                row.AllowDrop = true;
                row.DragOver += (_, e) =>
                {
                    e.Effects = e.Data.GetDataPresent(NotesDrag) || id > 0 && e.Data.GetDataPresent(GroupDrag)
                        ? DragDropEffects.Move : DragDropEffects.None;
                    e.Handled = true;
                };
                row.Drop += (_, e) =>
                {
                    if (e.Data.GetData(NotesDrag) is long[] ids) MoveNotes(ids, id == 0 ? null : id);
                    if (id > 0 && e.Data.GetData(GroupDrag) is long source) Reorder(source, id.Value);
                    e.Handled = true;
                };
            }
            return row;
        }

        void GroupSection(bool archived)
        {
            var section = groups.Where(group => group.Archived == archived).ToArray();
            var parents = section.Select(group => group.ParentUuid).ToHashSet();
            foreach (var (group, depth) in NoteStore.VisibleGroupTree(section, expandedGroups))
                GroupRow(group, depth, parents.Contains(group.Uuid));
        }

        void GroupRow(NoteGroup group, int depth, bool hasChildren)
        {
            var row = Row(groupRows, group.Name, "●", group.Id, 0, count: group.Count, color: group.Color ?? "#7C8BA4");
            if (row.Parent is Border marker)
            {
                marker.Margin = new Thickness(Math.Min(depth, 8) * 10, 0, 0, 2);
                if (hasChildren)
                {
                    marker.Child = null;
                    var panel = new DockPanel();
                    var expanded = expandedGroups.Contains(group.Uuid);
                    var toggle = Button(expanded ? "▾" : "▸", () =>
                    {
                        if (groupEdit != null) return;
                        if (!expandedGroups.Remove(group.Uuid)) expandedGroups.Add(group.Uuid);
                        RefreshSidebar();
                        if (groupButtons.TryGetValue(group.Id, out var refreshed)) refreshed.Focus();
                    });
                    toggle.ToolTip = expanded ? "Alt grupları kapat" : "Alt grupları aç";
                    System.Windows.Automation.AutomationProperties.SetName(toggle, $"{group.Name} · {toggle.ToolTip}");
                    toggle.ClearValue(MarginProperty);
                    toggle.ClearValue(PaddingProperty);
                    toggle.SetResourceReference(StyleProperty, "SidebarIcon");
                    Add(panel, toggle, Dock.Left);
                    panel.Children.Add(row);
                    marker.Child = panel;
                }
            }
            groupButtons.Add(group.Id, row);
            row.ToolTip = group.Latest == null ? group.Name : $"{group.Name} · {group.Count} not · {DateTimeOffset.Parse(group.Latest):dd.MM.yyyy HH:mm}";
            var menu = new ContextMenu();
            Menu(menu, "Grubu Markdown olarak dışa aktar…", () => ExportGroup(group));
            Menu(menu, "Alt grup oluştur…", () => EditGroup(null, parent: group));
            Menu(menu, "Yeniden adlandır", () => EditGroup(group));
            var colors = new MenuItem { Header = "Renk" };
            menu.Items.Add(colors);
            foreach (var (name, value) in new[] { ("Varsayılan", (string?)null), ("Sarı", "#D2A900"), ("Yeşil", "#159B77"), ("Mavi", "#5685DB"), ("Pembe", "#DA65A3") })
                Menu(colors, name, () => UpdateGroup(group with { Color = value }, group));
            Menu(menu, group.Archived ? "Arşivden çıkar" : "Arşivle", () => UpdateGroup(group with { Archived = !group.Archived }, group));
            Menu(menu, "Sil", () => DeleteGroup(group));
            row.ContextMenu = menu;
            SetupDrag(row, () => new DataObject(GroupDrag, group.Id));
        }
    }

    private DockPanel Section(string title, Button? action = null)
    {
        var header = new DockPanel { Margin = new Thickness(2, 16, 2, 6) };
        if (action != null) Add(header, action, Dock.Right);
        var label = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        header.Children.Add(label);
        return header;
    }

    private Button AddSidebarRow(Panel target, string caption, string glyph, Action action, bool selected = false,
        int? count = null, string? color = null, string? glyphFont = null)
    {
        var row = Button("", action);
        row.ClearValue(PaddingProperty);
        row.ClearValue(MarginProperty);
        row.SetResourceReference(StyleProperty, selected ? "SidebarSelectedRow" : "SidebarRow");
        System.Windows.Automation.AutomationProperties.SetName(row, caption);
        var content = new DockPanel();
        var icon = new TextBlock { Text = glyph, Width = 20, VerticalAlignment = VerticalAlignment.Center };
        if (glyphFont != null) icon.FontFamily = new FontFamily(glyphFont);
        if (color != null) icon.Foreground = Brush(color);
        else icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Add(content, icon, Dock.Left);
        if (count is int number)
        {
            var badge = new TextBlock { Text = number.ToString(), Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
            badge.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Add(content, badge, Dock.Right);
        }
        content.Children.Add(new TextBlock { Text = caption, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center });
        row.Content = content;
        var marker = new Border { Child = row, BorderThickness = new Thickness(3, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, 2) };
        if (selected) marker.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        target.Children.Add(marker);
        sidebarRows.Add(row);
        row.GotKeyboardFocus += (_, _) => row.BringIntoView();
        row.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down or Key.Enter)) return;
            var index = sidebarRows.IndexOf(row);
            if (e.Key == Key.Enter) row.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            else
            {
                var next = sidebarRows[Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, sidebarRows.Count - 1)];
                next.Focus();
            }
            e.Handled = true;
        };
        return row;
    }

    private void SelectGroup(long? id, int category)
    {
        if (!Flush(false)) return;
        SelectedGroup = id;
        selectedCards.Clear();
        selectionAnchor = null;
        store.Set("group", id?.ToString() ?? "");
        store.Set("scope", category.ToString());
        scope.SelectedIndex = category;
        RefreshCards();
    }

    private void EditGroup(NoteGroup? group, Action<long>? created = null, NoteGroup? parent = null)
    {
        if (groupEdit != null) { groupEdit.Focus(); return; }
        if (sidebarScroll?.Visibility == Visibility.Collapsed)
            sidebarToggle?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var index = newGroupIndex;
        if ((group ?? parent) is { } anchor && groupButtons.TryGetValue(anchor.Id, out var row))
        {
            DependencyObject container = row;
            while (VisualTreeHelper.GetParent(container) is { } owner && owner != groupRows)
                container = owner;
            if (container is UIElement element && groupRows.Children.IndexOf(element) is var rowIndex && rowIndex >= 0)
                index = rowIndex + (parent is null ? 0 : 1);
        }
        var input = new TextBox { Text = group?.Name ?? "", Margin = new Thickness(3), ToolTip = "Grup adı · Enter" };
        groupEdit = input;
        groupRows.Children.Insert(Math.Max(0, index), input);
        input.BringIntoView();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (groupEdit != input) return;
            input.Focus();
            input.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { groupEdit = null; RefreshCards(); e.Handled = true; return; }
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            e.Handled = true;
            try
            {
                if (group == null)
                {
                    var id = parent == null ? store.GetOrCreateGroup(input.Text) : store.CreateSubgroup(input.Text, parent);
                    if (parent != null) expandedGroups.Add(parent.Uuid);
                    ScheduleCloudAfterEdit();
                    groupEdit = null;
                    if (created != null) created(id);
                    else SelectGroup(id, 0);
                }
                else
                {
                    store.UpdateGroup(group with { Name = input.Text.Trim() });
                    groupEdit = null;
                    RefreshCards();
                    Notify("Grup adı değiştirildi.", () => UpdateGroup(group, null));
                }
            }
            catch (SqliteException) { Notify("Grup adı zaten kullanılıyor veya kaydedilemedi."); }
        };
    }

    private void UpdateGroup(NoteGroup updated, NoteGroup? previous)
    {
        try
        {
            store.UpdateGroup(updated);
            RefreshCards();
            if (previous != null) Notify("Grup güncellendi.", () => UpdateGroup(previous, null));
        }
        catch (SqliteException error) { Notify($"Grup güncellenemedi · {error.Message}"); }
    }

    private void DeleteGroup(NoteGroup group)
    {
        if (!Flush(false)) return;
        var ids = store.DeleteGroup(group.Id);
        UpdateEditorGroups(ids, null);
        if (SelectedGroup == group.Id) SelectGroup(0, 0);
        RefreshCards();
        Notify("Grup silindi. Notlar Grupsuz’a taşındı.", () =>
        {
            try
            {
                store.RestoreGroup(group, ids);
                foreach (var id in ids)
                    if (editors.TryGetValue(id, out var editor))
                        editor.Note.GroupId = store.Get(id)?.GroupId;
                RefreshCards();
            }
            catch (SqliteException error) { Notify($"Grup geri alınamadı · {error.Message}"); }
        });
    }

    private void Reorder(long source, long target)
    {
        if (source == target) return;
        var all = store.Groups();
        var targetGroup = all.FirstOrDefault(group => group.Id == target);
        if (targetGroup == null) return;
        var groups = all.Where(group => group.ParentUuid == targetGroup.ParentUuid && group.Archived == targetGroup.Archived).ToList();
        var moved = groups.FirstOrDefault(group => group.Id == source);
        if (moved == null) return;
        var index = groups.FindIndex(group => group.Id == target && group.Archived == moved.Archived);
        if (index < 0) return;
        groups.Remove(moved);
        groups.Insert(index, moved);
        store.Atomic(() =>
        {
            for (var i = 0; i < groups.Count; i++) store.UpdateGroup(groups[i] with { Order = i + 1 });
        });
        RefreshCards();
    }

    private void PopulateMoveMenu(MenuItem move, Card card)
    {
        move.Items.Clear();
        Menu(move, "Grupsuz", () => MoveNotes(SelectedIds(card.Id), null));
        foreach (var group in store.Groups()) Menu(move, group.Name, () => MoveNotes(SelectedIds(card.Id), group.Id));
        Menu(move, "Yeni grup…", () => EditGroup(null, group => MoveNotes(SelectedIds(card.Id), group)));
    }

    private long[] SelectedIds(long id) => selectedCards.Contains(id) ? selectedCards.ToArray() : [id];

    internal void MoveNotes(long[] ids, long? group)
    {
        if (!Flush(false)) return;
        var previous = new Dictionary<long, long?>();
        try
        {
            store.Atomic(() =>
            {
                foreach (var id in ids) previous.Add(id, store.MoveNote(id, group));
            });
            UpdateEditorGroups(ids, group);
            selectedCards.Clear();
            RefreshCards();
            Notify("Grup ataması değiştirildi.", () =>
            {
                store.Atomic(() =>
                {
                    foreach (var (id, oldGroup) in previous) store.MoveNote(id, oldGroup);
                });
                foreach (var (id, oldGroup) in previous) UpdateEditorGroups([id], oldGroup);
                RefreshCards();
            });
        }
        catch (SqliteException error) { Notify($"Not taşınamadı · {error.Message}"); }
    }

    private void UpdateEditorGroups(IEnumerable<long> ids, long? group)
    {
        foreach (var id in ids)
            if (editors.TryGetValue(id, out var editor))
            {
                editor.Note.GroupId = group;
                editor.RefreshGroupBox();
            }
    }

    internal IReadOnlyList<NoteGroup> GetGroups() => store.Groups();
    internal void AssignGroup(Note note, string name)
    {
        try
        {
            var group = string.IsNullOrWhiteSpace(name) || name == "Grupsuz" ? (long?)null : store.GetOrCreateGroup(name);
            if (group != note.GroupId) MoveNotes([note.Id], group);
        }
        catch (SqliteException error) { Notify($"Grup atanamadı · {error.Message}"); }
    }

    private void AttachCardDrag(Border border, Card card)
    {
        if (scope.SelectedIndex == 2) return;
        cardOrder.Add(card.Id);
        if (selectedCards.Contains(card.Id)) border.BorderBrush = Theme.Brush("Accent");
        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (!selectedCards.Add(card.Id)) selectedCards.Remove(card.Id);
                selectionAnchor = card.Id;
                RefreshCards();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && selectionAnchor is long anchor)
            {
                var start = cardOrder.IndexOf(anchor);
                var end = cardOrder.IndexOf(card.Id);
                if (start >= 0 && end >= 0)
                    selectedCards.UnionWith(cardOrder.Skip(Math.Min(start, end)).Take(Math.Abs(start - end) + 1));
                RefreshCards();
                e.Handled = true;
            }
            else selectionAnchor = card.Id;
        };
        SetupDrag(border, () => new DataObject(NotesDrag, SelectedIds(card.Id)));
    }

    private static void SetupDrag(FrameworkElement element, Func<DataObject> data)
    {
        Point? start = null;
        element.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(element);
        element.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || start is not Point origin) return;
            var delta = e.GetPosition(element) - origin;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            DragDrop.DoDragDrop(element, data(), DragDropEffects.Move);
        };
    }

    private void SetupGroupSuggestions(TextBox input)
    {
        var list = new ListBox { MaxHeight = 190, MinWidth = 220 };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = input, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false, Child = list
        };
        void Choose()
        {
            if (list.SelectedItem is not NoteGroup group) return;
            var index = input.Text.LastIndexOf('@');
            if (index < 0) return;
            input.Text = input.Text[..index] + $"@\"{group.Name}\" ";
            input.CaretIndex = input.Text.Length;
            popup.IsOpen = false;
            input.BringIntoView();
        input.Focus();
        }
        list.DisplayMemberPath = nameof(NoteGroup.Name);
        list.PreviewMouseLeftButtonUp += (_, _) => Choose();
        input.TextChanged += (_, _) =>
        {
            var index = input.Text.LastIndexOf('@');
            var prefix = index < 0 ? "" : input.Text[(index + 1)..];
            if (index < 0 || prefix.Contains(' ') || prefix.Contains('"') || !input.IsKeyboardFocused)
            {
                popup.IsOpen = false;
                return;
            }
            var choices = store.Groups().Where(group => NoteStore.Fold(group.Name).StartsWith(NoteStore.Fold(prefix), StringComparison.Ordinal)).ToArray();
            list.ItemsSource = choices;
            list.SelectedIndex = choices.Length > 0 ? 0 : -1;
            popup.IsOpen = choices.Length > 0;
        };
        input.PreviewKeyDown += (_, e) =>
        {
            if (!popup.IsOpen) return;
            if (e.Key == Key.Escape) popup.IsOpen = false;
            else if (e.Key == Key.Tab || e.Key == Key.Enter) Choose();
            else if (e.Key == Key.Down) list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
            else if (e.Key == Key.Up) list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1);
            else return;
            e.Handled = true;
        };
        input.Tag = popup;
    }

    private static bool GroupSuggestionsOpen(TextBox input) =>
        input.Tag is System.Windows.Controls.Primitives.Popup { IsOpen: true };
}
