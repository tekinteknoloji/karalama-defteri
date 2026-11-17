using System.Text.RegularExpressions;

namespace KaralamaDefteri;

public sealed record NoteGroup(long Id, string Name, string? Color, bool Archived, int Order, int Count, string? Latest,
    string Uuid, string UpdatedAt, string DeviceId, string? ParentUuid = null);
public sealed record Picture(string Mime, byte[] Bytes, byte[] Thumbnail, int Width, int Height, string? FileName);
public sealed record StoredPicture(long Id, string Mime, byte[] Bytes, string? FileName, string Uuid, string UpdatedAt, string DeviceId);

internal static partial class ImageMarkers
{
    [GeneratedRegex(@"\[\[resim:(\d+)\]\]", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();
    public static string Strip(string text) => Pattern().Replace(text, "");
    public static long Id(Match match) => long.TryParse(match.Groups[1].Value, out var id) ? id : 0;
    public static string Marker(long id) => $"[[resim:{id}]]";
}

public sealed partial class NoteStore
{
    private void Migrate()
    {
        using var versionQuery = Command("PRAGMA user_version");
        var version = (long)versionQuery.ExecuteScalar()!;
        if (version > SchemaVersion) throw new InvalidOperationException("Bu veritabanı daha yeni bir Karalama Defteri sürümüne ait.");
        if (version == SchemaVersion) return;
        Atomic(() =>
        {
            if (version == 0) Execute("""
            CREATE TABLE groups (
              id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE COLLATE NOCASE,
              color TEXT, sort_order INTEGER NOT NULL DEFAULT 0, is_archived INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX groups_turkish_name ON groups(search_fold(name));
            ALTER TABLE notes ADD COLUMN group_id INTEGER REFERENCES groups(id) ON DELETE SET NULL;
            CREATE INDEX notes_group ON notes(group_id);
            CREATE TABLE attachments (
              id INTEGER PRIMARY KEY AUTOINCREMENT, note_id INTEGER NOT NULL REFERENCES notes(id) ON DELETE CASCADE,
              mime TEXT NOT NULL, data BLOB NOT NULL, thumb BLOB, width INTEGER, height INTEGER,
              file_name TEXT, created_at TEXT NOT NULL, orphaned_at TEXT, position INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX attachments_note ON attachments(note_id);
            """);
            if (version < 2) MigrateRecordMetadata();
            if (version < 3) Execute("""
                CREATE TABLE IF NOT EXISTS attachment_originals (
                  id INTEGER PRIMARY KEY REFERENCES attachments(id) ON DELETE CASCADE,
                  mime TEXT NOT NULL, data BLOB NOT NULL, thumb BLOB, width INTEGER, height INTEGER,
                  expires TEXT NOT NULL
                );
                """);
            if (version < 4)
            {
                Execute("CREATE INDEX IF NOT EXISTS note_tags_tag ON note_tags(tag_id)");
                using var query = Command("SELECT id,content FROM notes");
                using var rows = query.ExecuteReader();
                var notes = new List<(long Id, string Content)>();
                while (rows.Read()) notes.Add((rows.GetInt64(0), rows.GetString(1)));
                rows.Close();
                foreach (var note in notes) SyncTags(note.Id, note.Content);
            }
            if (version < 5) Execute("""
                CREATE TABLE board_columns (
                  group_uuid TEXT, uuid TEXT NOT NULL, name TEXT NOT NULL, sort_order INTEGER NOT NULL,
                  created_at TEXT NOT NULL, updated_at TEXT NOT NULL, is_done INTEGER NOT NULL, is_deleted INTEGER NOT NULL,
                  CHECK(group_uuid IS NULL OR length(group_uuid) > 0)
                );
                CREATE UNIQUE INDEX board_columns_scope_uuid ON board_columns(coalesce(group_uuid,''),uuid);
                ALTER TABLE notes ADD COLUMN column_uuid TEXT;
                ALTER TABLE notes ADD COLUMN board_order INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE notes ADD COLUMN board_updated_at TEXT;
                """);
            if (version < 6) Execute("""
                ALTER TABLE notes ADD COLUMN deadline_utc INTEGER;
                ALTER TABLE notes ADD COLUMN deadline_reminder INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE notes ADD COLUMN deadline_updated_at TEXT;
                ALTER TABLE notes ADD COLUMN deadline_notified_for TEXT;
                CREATE INDEX notes_deadline ON notes(deadline_utc) WHERE deadline_utc IS NOT NULL;
                """);
            if (version < 7) Execute("ALTER TABLE groups ADD COLUMN parent_uuid TEXT");
            Execute($"PRAGMA user_version={SchemaVersion}");
        });
    }

    public void Atomic(Action action)
    {
        if (inAtomic) { action(); return; }
        Execute("BEGIN IMMEDIATE");
        inAtomic = true;
        try { action(); Execute("COMMIT"); }
        catch { Execute("ROLLBACK"); throw; }
        finally { inAtomic = false; }
    }

    private bool inAtomic;

    public IReadOnlyList<NoteGroup> Groups()
    {
        using var command = Command("""
            SELECT g.id,g.name,g.color,g.is_archived,g.sort_order,count(n.id),max(n.updated_at),g.uuid,g.updated_at,g.device_id,g.parent_uuid
            FROM groups g LEFT JOIN notes n ON n.group_id=g.id AND n.deleted_at IS NULL AND n.is_archived=0
            GROUP BY g.id
            """);
        using var rows = command.ExecuteReader();
        var groups = new List<NoteGroup>();
        while (rows.Read()) groups.Add(new(rows.GetInt64(0), rows.GetString(1), Optional(rows, 2), rows.GetBoolean(3),
            rows.GetInt32(4), rows.GetInt32(5), Optional(rows, 6), rows.GetString(7), rows.GetString(8), rows.GetString(9), Optional(rows, 10)));
        return groups.OrderBy(g => g.Order).ThenBy(g => g.Name, StringComparer.Create(Turkish, true)).ToArray();
    }

    public static IEnumerable<(NoteGroup Group, int Depth)> GroupTree(IEnumerable<NoteGroup> groups)
    {
        var ordered = groups.ToArray();
        var ids = ordered.Select(group => group.Uuid).ToHashSet(StringComparer.Ordinal);
        var children = ordered.ToLookup(group => group.ParentUuid ?? "", StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var roots = ordered.Where(group => group.ParentUuid == null || !ids.Contains(group.ParentUuid));
        foreach (var root in roots.Concat(ordered))
        {
            var pending = new Stack<(NoteGroup Group, int Depth)>();
            pending.Push((root, 0));
            while (pending.TryPop(out var item))
            {
                if (!visited.Add(item.Group.Uuid)) continue;
                yield return item;
                foreach (var child in children[item.Group.Uuid].Reverse())
                    pending.Push((child, item.Depth + 1));
            }
        }
    }

    public static IEnumerable<(NoteGroup Group, int Depth)> VisibleGroupTree(
        IEnumerable<NoteGroup> groups, IReadOnlySet<string> expanded)
    {
        int? collapsedDepth = null;
        foreach (var item in GroupTree(groups))
        {
            if (collapsedDepth is int depth && item.Depth > depth) continue;
            yield return item;
            collapsedDepth = expanded.Contains(item.Group.Uuid) ? null : item.Depth;
        }
    }

    public long CreateSubgroup(string name, NoteGroup parent)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Grup adı boş olamaz.");
        using var insert = Command("""
            INSERT INTO groups(name,created_at,parent_uuid) VALUES($name,$now,$parent) RETURNING id
            """, ("$name", name), ("$now", Now()), ("$parent", parent.Uuid));
        return (long)insert.ExecuteScalar()!;
    }

    public long GetOrCreateGroup(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Grup adı boş olamaz.");
        using var insert = Command("""
            INSERT INTO groups(name,created_at) VALUES($name,$now) ON CONFLICT DO NOTHING
            """, ("$name", name), ("$now", Now()));
        insert.ExecuteNonQuery();
        using var get = Command("SELECT id FROM groups WHERE search_fold(name)=search_fold($name) OR name=$name COLLATE NOCASE",
            ("$name", name));
        return (long)get.ExecuteScalar()!;
    }

    public void UpdateGroup(NoteGroup group) => Execute("""
        UPDATE groups SET name=$name,color=$color,is_archived=$archived,sort_order=$order WHERE id=$id
        """, ("$name", group.Name.Trim()), ("$color", group.Color), ("$archived", group.Archived), ("$order", group.Order), ("$id", group.Id));

    public long[] DeleteGroup(long group)
    {
        var ids = new List<long>();
        Atomic(() =>
        {
            using (var command = Command("SELECT id FROM notes WHERE group_id=$group", ("$group", group)))
            using (var rows = command.ExecuteReader())
                while (rows.Read()) ids.Add(rows.GetInt64(0));
            Execute("DELETE FROM groups WHERE id=$group", ("$group", group));
        });
        return ids.ToArray();
    }

    public void RestoreGroup(NoteGroup group, long[] ids) => Atomic(() =>
    {
        Execute("""
            INSERT INTO groups(id,name,color,is_archived,sort_order,created_at,uuid,parent_uuid)
            VALUES($id,$name,$color,$archived,$order,$now,$uuid,$parent)
            """, ("$id", group.Id), ("$name", group.Name), ("$color", group.Color),
            ("$archived", group.Archived), ("$order", group.Order), ("$now", Now()), ("$uuid", group.Uuid), ("$parent", group.ParentUuid));
        foreach (var id in ids)
            Execute("UPDATE notes SET group_id=$group WHERE id=$id AND group_id IS NULL", ("$group", group.Id), ("$id", id));
    });

    public long? MoveNote(long id, long? group)
    {
        using var command = Command("SELECT group_id FROM notes WHERE id=$id", ("$id", id));
        var previous = command.ExecuteScalar() as long?;
        Execute("UPDATE notes SET group_id=$group,updated_at=$now WHERE id=$id", ("$group", group), ("$id", id), ("$now", Now()));
        return previous;
    }

    public (string Text, long? Group, bool Missing) ParseGroup(string query)
    {
        var match = Regex.Match(query, @"(?:^|\s)@(?:""([^""]+)""|([^\s""]+))");
        if (!match.Success) return (query, null, false);
        var name = Fold(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        var groups = Groups();
        var selected = groups.FirstOrDefault(group => Fold(group.Name) == name)
            ?? groups.FirstOrDefault(group => Fold(group.Name).StartsWith(name, StringComparison.Ordinal));
        return (query.Remove(match.Index, match.Length).Insert(match.Index, " ").Trim(), selected?.Id, selected == null);
    }

    public long InsertPicture(long note, Picture picture)
    {
        using var command = Command("""
            INSERT INTO attachments(note_id,mime,data,thumb,width,height,file_name,created_at)
            VALUES($note,$mime,$data,$thumb,$width,$height,$name,$now) RETURNING id
            """, ("$note", note), ("$mime", picture.Mime), ("$data", picture.Bytes), ("$thumb", picture.Thumbnail),
            ("$width", picture.Width), ("$height", picture.Height), ("$name", picture.FileName), ("$now", Now()));
        return (long)command.ExecuteScalar()!;
    }

    public StoredPicture? GetPicture(long note, long id)
    {
        using var command = Command("SELECT mime,data,file_name,uuid,updated_at,device_id FROM attachments WHERE id=$id AND note_id=$note",
            ("$id", id), ("$note", note));
        using var row = command.ExecuteReader();
        return row.Read() ? new(id, row.GetString(0), (byte[])row.GetValue(1), Optional(row, 2),
            row.GetString(3), row.GetString(4), row.GetString(5)) : null;
    }

    public StoredPicture? GetFirstPicture(long note)
    {
        using var command = Command("""
            SELECT id FROM attachments WHERE note_id=$note AND orphaned_at IS NULL ORDER BY position,id LIMIT 1
            """, ("$note", note));
        return command.ExecuteScalar() is long id ? GetPicture(note, id) : null;
    }

    public void SyncPictures(Note note) => Execute("""
        UPDATE attachments SET
            position=instr($content,'[[resim:'||id||']]'),
            orphaned_at=CASE WHEN instr($content,'[[resim:'||id||']]')>0 THEN NULL ELSE coalesce(orphaned_at,$now) END
        WHERE note_id=$id AND (position<>instr($content,'[[resim:'||id||']]') OR
            (orphaned_at IS NULL) <> (instr($content,'[[resim:'||id||']]')>0))
        """, ("$content", note.Content), ("$id", note.Id), ("$now", Now()));

    private bool HasImages(long id)
    {
        using var command = Command("SELECT 1 FROM attachments WHERE note_id=$id AND orphaned_at IS NULL LIMIT 1", ("$id", id));
        return command.ExecuteScalar() != null;
    }

    private void CleanAttachments() => Execute("""
        DELETE FROM attachments WHERE orphaned_at < $cutoff
        """, ("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToString("O")));

    public void Vacuum() => Execute("VACUUM");
}
