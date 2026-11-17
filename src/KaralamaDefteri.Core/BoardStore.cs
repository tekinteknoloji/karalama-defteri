namespace KaralamaDefteri;

internal sealed record BoardColumn(string Id, string Name, long Order, string Created, string Updated, bool Done, bool Deleted);
internal sealed record BoardPosition(string? Column, long Order, string? Updated);

public sealed partial class NoteStore
{
    private static readonly Guid BoardSyncId = new("cafc31eb-b7cf-4093-8e82-44e720fcac29");
    private static readonly string[] DefaultColumns = ["Fikir", "Yapılacak", "Yapılıyor", "Bitti"];
    private sealed record SyncedColumn(string? Group, BoardColumn Column);

    internal string? BoardGroup(long? group) => Groups().FirstOrDefault(item => item.Id == group)?.Uuid;

    private BoardColumn[] ReadBoardColumns(string? group)
    {
        using var command = Command("""
            SELECT uuid,name,sort_order,created_at,updated_at,is_done,is_deleted
            FROM board_columns WHERE group_uuid IS $group
            """, ("$group", group));
        using var rows = command.ExecuteReader();
        var columns = new List<BoardColumn>();
        while (rows.Read())
            columns.Add(new(rows.GetString(0), rows.GetString(1), rows.GetInt64(2), rows.GetString(3),
                rows.GetString(4), rows.GetBoolean(5), rows.GetBoolean(6)));
        return columns.ToArray();
    }

    internal BoardColumn[] BoardColumns(string? group)
    {
        var saved = ReadBoardColumns(group);
        var defaults = DefaultColumns.Select((name, index) =>
            new BoardColumn($"00000000-0000-4000-8000-{index + 1:D12}", name, index, "", "", index == 3, false));
        return saved.Concat(defaults.Where(column => saved.All(item => item.Id != column.Id)))
            .Where(column => !column.Deleted).OrderBy(column => column.Order)
            .ThenBy(column => column.Id, StringComparer.Ordinal).ToArray();
    }

    internal void WriteBoardColumn(string? group, BoardColumn column) => Execute("""
        INSERT INTO board_columns(group_uuid,uuid,name,sort_order,created_at,updated_at,is_done,is_deleted)
        VALUES($group,$uuid,$name,$order,$created,$updated,$done,$deleted)
        ON CONFLICT DO UPDATE SET name=excluded.name,sort_order=excluded.sort_order,
        updated_at=excluded.updated_at,is_done=excluded.is_done,is_deleted=excluded.is_deleted
        """, ("$group", group), ("$uuid", column.Id), ("$name", column.Name),
        ("$order", column.Order), ("$created", column.Created), ("$updated", column.Updated),
        ("$done", column.Done), ("$deleted", column.Deleted));

    internal void SaveBoardColumn(string? group, BoardColumn column)
    {
        var now = BoardStamp();
        WriteBoardColumn(group, column with { Created = column.Created.Length == 0 ? now : column.Created, Updated = now });
    }

    private static string BoardStamp() => DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    internal Dictionary<long, BoardPosition> BoardPositions()
    {
        using var command = Command("SELECT id,column_uuid,board_order,board_updated_at FROM notes");
        using var rows = command.ExecuteReader();
        var positions = new Dictionary<long, BoardPosition>();
        while (rows.Read()) positions[rows.GetInt64(0)] = new(Optional(rows, 1), rows.GetInt64(2), Optional(rows, 3));
        return positions;
    }

    private BoardPosition? ReadBoardPosition(long id)
    {
        using var command = Command("SELECT column_uuid,board_order,board_updated_at FROM notes WHERE id=$id", ("$id", id));
        using var rows = command.ExecuteReader();
        return rows.Read() && !rows.IsDBNull(2) ? new(Optional(rows, 0), rows.GetInt64(1), rows.GetString(2)) : null;
    }

    internal void SetBoardPosition(long id, BoardPosition position) => Execute("""
        UPDATE notes SET column_uuid=$column, board_order=$order, board_updated_at=$updated WHERE id=$id
        """, ("$column", position.Column), ("$order", position.Order), ("$updated", position.Updated), ("$id", id));

    internal void OrderBoardCards(string column, IReadOnlyList<long> ids) => Atomic(() =>
    {
        var previous = BoardPositions();
        var stamp = BoardStamp();
        for (var i = 0; i < ids.Count; i++)
            if (previous[ids[i]].Column != column || previous[ids[i]].Order != i)
                SetBoardPosition(ids[i], new(column, i, stamp));
    });

    internal long[] ActiveBoardCards(long? group)
    {
        using var command = Command("""
            SELECT id FROM notes WHERE deleted_at IS NULL AND is_archived=0
              AND ($group IS NULL OR coalesce(group_id,0)=$group)
            ORDER BY uuid
            """, ("$group", group));
        using var rows = command.ExecuteReader();
        var ids = new List<long>();
        while (rows.Read()) ids.Add(rows.GetInt64(0));
        return ids.ToArray();
    }

    internal void RestoreBoardPositions(IEnumerable<long> ids, Dictionary<long, BoardPosition> positions)
    {
        var now = BoardStamp();
        foreach (var id in ids) SetBoardPosition(id, positions[id] with { Updated = now });
    }

    internal int ArchiveFinishedBoardCards(int days)
    {
        if (days <= 0) return 0;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var groups = Groups().ToDictionary(group => group.Id, group => group.Uuid);
        var columns = groups.Values.Append("").ToDictionary(group => group,
            group => BoardColumns(group.Length == 0 ? null : group).ToDictionary(column => column.Id));
        var count = 0;
        Atomic(() =>
        {
            foreach (var (id, position) in BoardPositions())
            {
                if (position.Column == null || position.Updated == null || string.CompareOrdinal(position.Updated, cutoff) >= 0 ||
                    Get(id) is not { Archived: false, Deleted: null } note) continue;
                var group = note.GroupId is long groupId ? groups.GetValueOrDefault(groupId) ?? "" : "";
                var column = columns[group].GetValueOrDefault(position.Column) ??
                    columns[""].GetValueOrDefault(position.Column);
                if (column?.Done != true) continue;
                note.Archived = true;
                Save(note);
                count++;
            }
        });
        return count;
    }

    private void SyncBoardColumns(ISyncProvider provider, SyncEncryption crypto)
    {
        var remote = provider.GetBlob(BoardSyncId) is { } bytes
            ? crypto.Open<Dictionary<string, SyncedColumn>>(BoardSyncId, "board-columns", bytes) : [];
        var publish = false;
        var scopes = Groups().Select(group => (Uuid: (string?)group.Uuid, Name: (string?)group.Name)).Prepend((null, null));
        foreach (var (uuid, name) in scopes)
        foreach (var column in ReadBoardColumns(uuid))
        {
            var key = (name == null ? "" : Fold(name)) + "/" + column.Id;
            if (!remote.TryGetValue(key, out var other) || CompareColumns(column, other.Column) > 0)
            {
                remote[key] = new(name, column);
                publish = true;
            }
        }
        Atomic(() =>
        {
            foreach (var entry in remote.Values)
            {
                var group = entry.Group == null ? null : BoardGroup(GetOrCreateGroup(entry.Group));
                var local = ReadBoardColumns(group).FirstOrDefault(column => column.Id == entry.Column.Id);
                if (local == null || CompareColumns(entry.Column, local) > 0) WriteBoardColumn(group, entry.Column);
            }
        });
        if (publish) provider.PutBlob(BoardSyncId, crypto.Seal(BoardSyncId, "board-columns", remote));
    }

    private static int CompareColumns(BoardColumn left, BoardColumn right)
    {
        var time = string.CompareOrdinal(left.Updated, right.Updated);
        return time != 0 ? time : string.CompareOrdinal(System.Text.Json.JsonSerializer.Serialize(left),
            System.Text.Json.JsonSerializer.Serialize(right));
    }
}
