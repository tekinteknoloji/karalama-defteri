using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private const string BoardDragFormat = "KaralamaDefteri.BoardCard";
    private readonly ScrollViewer boardView = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Visibility = Visibility.Collapsed
    };
    private readonly Dictionary<long, Border> boardCards = [];
    private Dictionary<string, string> boardLabels = [];
    private Dictionary<long, BoardPosition> cardPositions = [];
    private ScrollViewer cardScroll = null!;
    private GridSplitter cardSplitter = null!;
    private Grid cardBody = null!;
    private GridLength cardWidth = new(380);
    private string? BoardScope => store.BoardGroup(SelectedGroup);
    private bool ShowingBoard => Features.Enabled(Feature.Board) && store.Setting("cardView") == "board" && scope.SelectedIndex == 0;

    private void OpenBoard()
    {
        if (!Features.Enabled(Feature.Board)) return;
        store.Set("cardView", "board");
        if (scope.SelectedIndex != 0) SelectGroup(null, 0);
        RefreshCards();
    }

    private void UpdateWorkspaceLayout(bool board)
    {
        var showEditor = tabs.Items.Count != 0;
        var showSplitter = showEditor && !board;
        var main = cardBody.ColumnDefinitions[0];
        if (main.Width.IsAbsolute && main.Width.Value > 0) cardWidth = main.Width;
        main.MinWidth = !showEditor ? 0 : board ? 220 : 240;
        main.Width = !showEditor ? new GridLength(1, GridUnitType.Star)
            : board ? new GridLength(2, GridUnitType.Star) : cardWidth;
        Grid.SetColumnSpan(boardView, showEditor ? 2 : 3);
        boardView.Visibility = board ? Visibility.Visible : Visibility.Collapsed;
        cardScroll.Visibility = board ? Visibility.Collapsed : Visibility.Visible;
        cardSplitter.Visibility = showSplitter ? Visibility.Visible : Visibility.Collapsed;
        cardBody.ColumnDefinitions[1].Width = new GridLength(showSplitter ? 9 : 0);
        cardBody.ColumnDefinitions[2].MinWidth = showEditor ? 300 : 0;
        cardBody.ColumnDefinitions[2].Width = showEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        tabs.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshBoard(IReadOnlyList<Card> cards)
    {
        var visible = ShowingBoard;
        var focused = (Keyboard.FocusedElement as Border)?.Tag as long?;
        var offset = boardView.HorizontalOffset;
        UpdateWorkspaceLayout(visible);
        boardCards.Clear();
        if (!visible) { boardView.Content = null; return; }

        var columns = store.BoardColumns(BoardScope);
        var positions = store.BoardPositions();
        var byId = cards.ToDictionary(card => card.Id);
        var active = store.ActiveBoardCards(SelectedGroup);
        var layout = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            var displayed = BoardOrder(column.Id, columns, positions, active)
                .Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
            var stack = new StackPanel();
            var header = new DockPanel { Margin = new Thickness(4) };
            var more = Button("…");
            AutomationProperties.SetName(more, column.Name + " sütun işlemleri");
            var menu = new ContextMenu { PlacementTarget = more };
            more.Click += (_, _) => menu.IsOpen = true;
            Menu(menu, "Yeniden adlandır…", () => EditBoardColumn(column));
            Menu(menu, column.Done ? "Bitti işaretini kaldır" : "Bitti sütunu olarak işaretle", () =>
                ChangeBoard(() => store.SaveBoardColumn(BoardScope, column with { Done = !column.Done })));
            if (i > 0)
            {
                var left = columns[i - 1];
                Menu(menu, "Sola taşı", () => SwapBoardColumns(left, column));
            }
            if (i + 1 < columns.Length)
            {
                var right = columns[i + 1];
                Menu(menu, "Sağa taşı", () => SwapBoardColumns(right, column));
            }
            if (columns.Length > 1) Menu(menu, "Sütunu sil", () => DeleteBoardColumn(column));
            Add(header, more, Dock.Right);
            header.Children.Add(new TextBlock { Text = $"{column.Name} ({displayed.Length})", FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var lane = new DockPanel { Width = 222, Margin = new Thickness(0, 0, 8, 0), Background = Theme.Brush("Bar") };
            Add(lane, header, Dock.Top);
            lane.Children.Add(scroll);
            AddDrop(lane, column.Id, null);
            foreach (var card in displayed)
            {
                var tile = CardView(card, boardTile: true);
                tile.Width = 194;
                tile.Focusable = true;
                tile.Tag = card.Id;
                AutomationProperties.SetName(tile, card.Title);
                AddDrop(tile, column.Id, card.Id);
                Point start = default;
                var pressed = false;
                tile.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    pressed = true;
                    start = e.GetPosition(this);
                    tile.Focus();
                };
                tile.PreviewMouseMove += (_, e) =>
                {
                    if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                    var distance = e.GetPosition(this) - start;
                    if (Math.Abs(distance.X) < SystemParameters.MinimumHorizontalDragDistance &&
                        Math.Abs(distance.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    pressed = false;
                    DragDrop.DoDragDrop(tile, new DataObject(BoardDragFormat, card.Id), DragDropEffects.Move);
                };
                tile.MouseLeftButtonUp += (_, _) => { if (pressed) OpenBoardCard(card); pressed = false; };
                tile.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { OpenBoardCard(card); e.Handled = true; }
                    else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                        e.Handled = MoveBoardWithKey(card.Id, e.Key);
                };
                boardCards.Add(card.Id, tile);
                stack.Children.Add(tile);
            }
            layout.Children.Add(lane);
        }
        var addColumn = Button("Sütun ekle…", () => EditBoardColumn(null));
        addColumn.VerticalAlignment = VerticalAlignment.Top;
        layout.Children.Add(addColumn);
        boardView.Content = layout;
        boardView.ScrollToHorizontalOffset(offset);
        if (focused is long id && boardCards.TryGetValue(id, out var replacement)) replacement.Focus();
    }

    private void OpenBoardCard(Card card)
    {
        OpenCard(card);
        RefreshCards();
    }

    private void AddDrop(UIElement target, string column, long? before)
    {
        target.AllowDrop = true;
        target.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(BoardDragFormat)) { e.Effects = DragDropEffects.Move; e.Handled = true; }
        };
        target.Drop += (_, e) =>
        {
            if (e.Data.GetData(BoardDragFormat) is not long id) return;
            e.Handled = true;
            var after = before != null && target is FrameworkElement tile && e.GetPosition(tile).Y >= tile.ActualHeight / 2;
            MoveBoardCard(id, column, before, after);
        };
    }

    private static string EffectiveColumn(BoardPosition position, BoardColumn[] columns) =>
        columns.Any(column => column.Id == position.Column) ? position.Column! : columns[0].Id;

    private long[] BoardOrder(string column, BoardColumn[] columns, Dictionary<long, BoardPosition> positions, long[]? active = null) =>
        (active ?? store.ActiveBoardCards(SelectedGroup)).Where(id => EffectiveColumn(positions[id], columns) == column)
            .OrderBy(id => positions[id].Order).ToArray();

    private bool MoveBoardWithKey(long id, Key key)
    {
        var columns = store.BoardColumns(BoardScope);
        var positions = store.BoardPositions();
        var column = EffectiveColumn(positions[id], columns);
        var index = Array.FindIndex(columns, item => item.Id == column);
        if (key is Key.Left or Key.Right)
        {
            var target = index + (key == Key.Left ? -1 : 1);
            if (target >= 0 && target < columns.Length) MoveBoardCard(id, columns[target].Id, null, false);
        }
        else if (key is Key.Up or Key.Down)
        {
            var ids = BoardOrder(column, columns, positions);
            var target = Array.IndexOf(ids, id) + (key == Key.Up ? -1 : 1);
            if (target >= 0 && target < ids.Length) MoveBoardCard(id, column, ids[target], key == Key.Down);
        }
        else return false;
        return true;
    }

    private void MoveBoardCard(long id, string column, long? anchor, bool after)
    {
        if (!ShowingBoard || id == anchor) return;
        var active = store.ActiveBoardCards(SelectedGroup);
        if (!active.Contains(id)) return;
        var columns = store.BoardColumns(BoardScope);
        if (!columns.Any(item => item.Id == column)) return;
        var positions = store.BoardPositions();
        var ids = BoardOrder(column, columns, positions, active).Where(item => item != id).ToList();
        var index = anchor is long target ? ids.IndexOf(target) : -1;
        ids.Insert(index < 0 ? ids.Count : index + (after ? 1 : 0), id);
        ChangeBoard(() => store.OrderBoardCards(column, ids));
        if (boardCards.TryGetValue(id, out var card)) { card.Focus(); card.BringIntoView(); }
    }

    private void ChangeBoard(Action write)
    {
        if (!Flush(false)) return;
        try
        {
            write();
            if (syncPassword != null && syncAfterSave != null) Restart(syncAfterSave);
            ScheduleCloudAfterEdit();
            RefreshCards();
        }
        catch (SqliteException error) { Notify("Pano kaydedilemedi · " + error.Message); }
    }

    internal void StartBoardArchive()
    {
        ArchiveBoardCards();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        timer.Tick += (_, _) => ArchiveBoardCards();
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private void ArchiveBoardCards()
    {
        if (exiting || RestoreInProgress || !Features.Enabled(Feature.Board) ||
            !int.TryParse(store.Setting("boardArchiveDays"), out var days) || days <= 0) return;
        ChangeBoard(() => store.ArchiveFinishedBoardCards(days));
    }

    private void SwapBoardColumns(BoardColumn left, BoardColumn right) => ChangeBoard(() =>
    {
        var group = BoardScope;
        store.Atomic(() =>
        {
            var ordered = store.BoardColumns(group);
            var i = Array.FindIndex(ordered, item => item.Id == left.Id);
            var j = Array.FindIndex(ordered, item => item.Id == right.Id);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
            for (var index = 0; index < ordered.Length; index++)
                if (ordered[index].Order != index) store.SaveBoardColumn(group, ordered[index] with { Order = index });
        });
    });

    private void EditBoardColumn(BoardColumn? column)
    {
        var group = BoardScope;
        var window = Owned(column == null ? "Sütun ekle" : "Sütunu yeniden adlandır", 370, 160);
        var layout = new StackPanel { Margin = new Thickness(14) };
        var name = new TextBox { Text = column?.Name ?? "", MaxLength = 80 };
        AutomationProperties.SetName(name, "Sütun adı");
        layout.Children.Add(name);
        var save = Button("Kaydet", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            var updated = column ?? new BoardColumn(Guid.NewGuid().ToString(), "", store.BoardColumns(group).Max(item => item.Order) + 1,
                "", "", false, false);
            ChangeBoard(() => store.SaveBoardColumn(group, updated with { Name = name.Text.Trim() }));
            window.Close();
        });
        save.IsDefault = true;
        layout.Children.Add(save);
        window.Content = layout;
        window.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        window.ShowDialog();
    }

    private void DeleteBoardColumn(BoardColumn column)
    {
        var group = BoardScope;
        var columns = store.BoardColumns(group);
        var index = Array.FindIndex(columns, item => item.Id == column.Id);
        if (index < 0 || columns.Length < 2) return;
        var target = columns[index == 0 ? 1 : index - 1];
        var positions = store.BoardPositions();
        var moved = BoardOrder(column.Id, columns, positions);
        var destination = BoardOrder(target.Id, columns, positions);
        ChangeBoard(() =>
        {
            var affected = destination.Concat(moved).ToArray();
            store.Atomic(() =>
            {
                store.OrderBoardCards(target.Id, affected);
                store.SaveBoardColumn(group, column with { Deleted = true });
            });
            Notify("Sütun silindi.", () => ChangeBoard(() => store.Atomic(() =>
            {
                store.SaveBoardColumn(group, column);
                store.RestoreBoardPositions(affected, positions);
            })));
        });
    }
}
