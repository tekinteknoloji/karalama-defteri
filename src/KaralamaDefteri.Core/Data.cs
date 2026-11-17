using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class Note : ObservableObject
{
    public long Id { get; init; }
    public string Uuid { get; init; } = "";
    public string DeviceId { get; internal set; } = "";
    public string Content { get; set; } = "";
    [ObservableProperty] private string title = "";
    public bool Manual { get; set; }
    public string? Syntax { get; set; }
    public string? Color { get; set; }
    public bool Pinned { get; set; }
    public bool Checklist { get; set; }
    public bool Archived { get; set; }
    public string? Deleted { get; set; }
    public string Created { get; init; } = "";
    public string Updated { get; set; } = "";
    public int Caret { get; set; }
    public bool Dirty { get; set; }
    public long? GroupId { get; set; }
}

public sealed record Card(long Id, string Title, string Preview, string? Color, bool Pinned, string Updated, string Match,
    string? Group, int Images, byte[]? Thumbnail, string? Progress = null);

public sealed partial class NoteStore : IDisposable
{
    static NoteStore() => SQLitePCL.Batteries_V2.Init();

    private readonly SqliteConnection db;
    private readonly string? databasePassword;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Regex Words = new(@"[\p{L}\p{N}\p{M}]+", RegexOptions.Compiled);

    public NoteStore(string path) : this(path, null) { }

    internal NoteStore(string path, string? password, bool syncConnection = false)
    {
        databasePassword = password;
        db = OpenDatabase(path, password, syncConnection ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate, foreignKeys: true);
        try
        {
        ConfigureDatabase();
        if (syncConnection) return;
        Execute("""
            CREATE TABLE IF NOT EXISTS notes (
              id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL,
              title_manual INTEGER NOT NULL DEFAULT 0, content TEXT NOT NULL DEFAULT '',
              syntax TEXT, color TEXT, is_pinned INTEGER NOT NULL DEFAULT 0,
              is_checklist INTEGER NOT NULL DEFAULT 0, is_archived INTEGER NOT NULL DEFAULT 0,
              deleted_at TEXT, desktop_pin TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
              search_title TEXT GENERATED ALWAYS AS (search_fold(title)) STORED,
              search_content TEXT GENERATED ALWAYS AS (search_fold(content)) STORED
            );
            CREATE TABLE IF NOT EXISTS tags (id INTEGER PRIMARY KEY, name TEXT UNIQUE NOT NULL);
            CREATE TABLE IF NOT EXISTS note_tags (note_id INTEGER, tag_id INTEGER, PRIMARY KEY(note_id, tag_id));
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT);
            CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(search_title, search_content,
              content='notes', content_rowid='id', tokenize='unicode61 remove_diacritics 0', prefix='2 3 4');
            CREATE TRIGGER IF NOT EXISTS notes_ai AFTER INSERT ON notes BEGIN
              INSERT INTO notes_fts(rowid,search_title,search_content) VALUES(new.id,new.search_title,new.search_content);
            END;
            CREATE TRIGGER IF NOT EXISTS notes_ad AFTER DELETE ON notes BEGIN
              INSERT INTO notes_fts(notes_fts,rowid,search_title,search_content)
              VALUES('delete',old.id,old.search_title,old.search_content);
            END;
            CREATE TRIGGER IF NOT EXISTS notes_au AFTER UPDATE ON notes
            WHEN old.title <> new.title OR old.content <> new.content BEGIN
              INSERT INTO notes_fts(notes_fts,rowid,search_title,search_content)
              VALUES('delete',old.id,old.search_title,old.search_content);
              INSERT INTO notes_fts(rowid,search_title,search_content) VALUES(new.id,new.search_title,new.search_content);
            END;
            """);
        Migrate();
        Execute("DELETE FROM notes WHERE deleted_at < $cutoff", ("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToString("O")));
        CleanAttachments();
        Execute("DELETE FROM note_tags WHERE NOT EXISTS (SELECT 1 FROM notes WHERE id=note_tags.note_id)");
        Execute("DELETE FROM attachment_originals WHERE expires <= $now", ("$now", Now()));
        PruneDeletedRecords();
        }
        catch { db.Dispose(); throw; }
    }

    private void ConfigureDatabase()
    {
        db.CreateFunction<string, string>("search_fold", Fold, isDeterministic: true);
        db.CreateFunction("uuid_v7", NewUuid);
        db.CreateFunction("utc_ms", Now);
        db.CreateFunction("local_device", () => DeviceId, isDeterministic: true);
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA temp_store=MEMORY;");
    }

    public static string Now() => Timestamp(DateTimeOffset.UtcNow);
    private static string Timestamp(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    public static string Fold(string? value) => (value ?? "").Normalize().ToLower(Turkish);
    public static string AutoTitle(string content, string created)
    {
        using var lines = new StringReader(content);
        while (lines.ReadLine() is { } line)
        {
            line = ImageMarkers.Strip(line).Trim();
            if (line.Length == 0) continue;
            var length = Math.Min(line.Length, 80);
            if (char.IsHighSurrogate(line[length - 1])) length--;
            return line[..length];
        }
        return $"Not – {DateTimeOffset.Parse(created).LocalDateTime.ToString("dd.MM.yyyy HH:mm", Turkish)}";
    }

    private SqliteCommand Command(string sql, params (string, object?)[] parameters)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private void Execute(string sql, params (string, object?)[] parameters)
    {
        using var command = Command(sql, parameters);
        command.ExecuteNonQuery();
    }
    public string? Setting(string key)
    {
        using var command = Command("SELECT value FROM settings WHERE key=$key", ("$key", key));
        return command.ExecuteScalar() as string;
    }
    public void Set(string key, string value) => Execute("""
        INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value
        """, ("$key", key), ("$value", value));

    public Note Create(string content, long? group = null, string? title = null)
    {
        Note? result = null;
        Atomic(() => result = CreateWithTags(content, group, title));
        return result!;
    }

    private Note CreateWithTags(string content, long? group, string? title)
    {
        var now = Now();
        using var command = Command("""
            INSERT INTO notes(title,title_manual,content,created_at,updated_at,group_id)
            VALUES($title,$manual,$content,$now,$now,$group) RETURNING id
            """, ("$title", title ?? AutoTitle(content, now)), ("$manual", title != null), ("$content", content), ("$now", now), ("$group", group));
        var id = (long)command.ExecuteScalar()!;
        SyncTags(id, content);
        return Get(id)!;
    }
    public Note? Get(long id)
    {
        using var command = Command("""
            SELECT title,title_manual,content,syntax,color,is_pinned,is_archived,deleted_at,created_at,updated_at,group_id,uuid,device_id,is_checklist
            FROM notes WHERE id=$id
            """, ("$id", id));
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        return new Note
        {
            Id = id, Title = row.GetString(0), Manual = row.GetBoolean(1), Content = row.GetString(2),
            Syntax = Optional(row, 3), Color = Optional(row, 4), Pinned = row.GetBoolean(5),
            Archived = row.GetBoolean(6), Deleted = Optional(row, 7), Created = row.GetString(8), Updated = row.GetString(9),
            GroupId = row.IsDBNull(10) ? null : row.GetInt64(10), Uuid = row.GetString(11), DeviceId = row.GetString(12),
            Checklist = row.GetBoolean(13)
        };
    }
    private static string? Optional(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetString(index);
    public void Save(Note note) => Atomic(() => SaveWithTags(note));

    public void SaveWithPictures(Note note)
    {
        var updated = note.Updated;
        var device = note.DeviceId;
        var dirty = note.Dirty;
        try
        {
            Atomic(() =>
            {
                SaveWithTags(note);
                SyncPictures(note);
            });
        }
        catch
        {
            note.Updated = updated;
            note.DeviceId = device;
            note.Dirty = dirty;
            throw;
        }
    }

    private void SaveWithTags(Note note)
    {
        var updated = Now();
        Execute("""
            UPDATE notes SET title=$title,title_manual=$manual,content=$content,syntax=$syntax,
            color=$color,is_pinned=$pinned,is_archived=$archived,deleted_at=$deleted,updated_at=$updated,group_id=$group,is_checklist=$checklist WHERE id=$id
            """, ("$title", note.Title), ("$manual", note.Manual), ("$content", note.Content),
            ("$syntax", note.Syntax), ("$color", note.Color), ("$pinned", note.Pinned), ("$archived", note.Archived),
            ("$deleted", note.Deleted), ("$updated", updated), ("$id", note.Id), ("$group", note.GroupId), ("$checklist", note.Checklist));
        SyncTags(note.Id, note.Content);
        note.Updated = updated;
        note.DeviceId = DeviceId;
        note.Dirty = false;
    }
    public int EmptyTrash()
    {
        using var command = Command("DELETE FROM notes WHERE deleted_at IS NOT NULL");
        return command.ExecuteNonQuery();
    }

    public bool RemoveBlank(Note note)
    {
        if (note.Manual || note.Deleted != null || !string.IsNullOrWhiteSpace(note.Content)) return false;
        if (HasImages(note.Id)) return false;
        Execute("DELETE FROM notes WHERE id=$id", ("$id", note.Id));
        return true;
    }

    public static string[] SearchTerms(string query) => Words.Matches(Fold(query)).Select(match => match.Value).ToArray();
    public static Match FirstMatch(string original, string query)
    {
        var terms = SearchTerms(query);
        return Words.Matches(original).FirstOrDefault(word =>
            terms.Any(term => Fold(word.Value).StartsWith(term, StringComparison.Ordinal))) ?? Match.Empty;
    }
    public IReadOnlyList<Card> Search(string query, int scope, string? color, int limit = 1500, long? group = null, bool tagFiltersEnabled = true, bool deadlineFiltersEnabled = false)
    {
        var (dueStart, dueEnd) = deadlineFiltersEnabled
            ? NoteDeadline.SearchRange(ref query, DateTimeOffset.Now, TimeZoneInfo.Local)
            : ((long?)null, (long?)null);
        var tags = new List<string>();
        bool? images = null;
        query = FilterPattern.Replace(query, filter =>
        {
            if (filter.Groups[1].Success)
            {
                if (!tagFiltersEnabled) return filter.Value;
                tags.Add(Fold(filter.Groups[1].Value));
            }
            else if (filter.Groups[2].Success)
            {
                var chosen = Fold(filter.Groups[2].Value) switch
                {
                    "sarı" => "#FFF475", "yeşil" => "#A7FFEB", "mavi" => "#AECBFA", "pembe" => "#FDCFE8", _ => null
                };
                if (chosen == null) return filter.Value;
                color = chosen;
            }
            else if (filter.Groups[3].Success) scope = Fold(filter.Groups[3].Value) is "hayır" or "0" ? 0 : 1;
            else images = Fold(filter.Groups[4].Value) is not ("hayır" or "0");
            return " ";
        });
        query = query.Trim();
        var parsed = ParseGroup(query);
        if (parsed.Missing) return [];
        group = parsed.Group ?? group;
        query = parsed.Text;
        var terms = SearchTerms(query);
        if (terms.Length == 0 && !string.IsNullOrWhiteSpace(query)) return [];
        var match = string.Join(" AND ", terms.Select(term => $"\"{term}\"*"));
        var source = terms.Length == 0 ? "notes n" : "notes_fts JOIN notes n ON n.id=notes_fts.rowid";
        var filter = scope switch
        {
            1 => "n.deleted_at IS NULL AND n.is_archived=1",
            2 => "n.deleted_at IS NOT NULL",
            3 => "n.deleted_at IS NULL",
            _ => "n.deleted_at IS NULL AND n.is_archived=0"
        };
        var snippet = terms.Length == 0 ? "''" : "snippet(notes_fts,-1,char(1),char(2),'…',18)";
        var tagFilters = string.Join(" ", tags.Select((_, i) => $"AND EXISTS (SELECT 1 FROM note_tags nt JOIN tags t ON t.id=nt.tag_id WHERE nt.note_id=n.id AND t.name=$tag{i})"));
        using var command = Command($"""
            SELECT n.id,n.title,substr(n.content,1,900),n.color,n.is_pinned,n.updated_at,{snippet},
              g.name,
              (SELECT count(*) FROM attachments a WHERE a.note_id=n.id AND a.orphaned_at IS NULL),
              (SELECT a.thumb FROM attachments a WHERE a.note_id=n.id AND a.orphaned_at IS NULL ORDER BY a.position,a.id LIMIT 1),
              CASE WHEN n.is_checklist=1 THEN n.content END
            FROM {source} LEFT JOIN groups g ON g.id=n.group_id
            WHERE {filter} AND ($color IS NULL OR n.color=$color)
              AND ($group IS NULL OR coalesce(n.group_id,0)=$group)
              AND ($dueStart IS NULL OR (n.deadline_utc >= $dueStart AND n.deadline_utc < $dueEnd))
              AND ($images IS NULL OR $images=EXISTS (SELECT 1 FROM attachments a WHERE a.note_id=n.id AND a.orphaned_at IS NULL))
            {tagFilters}
            {(terms.Length == 0 ? "" : "AND notes_fts MATCH $match")}
            ORDER BY n.is_pinned DESC,{(terms.Length == 0 ? "" : "rank,")}n.updated_at DESC LIMIT $limit
            """, ("$color", color), ("$match", match), ("$limit", limit), ("$group", group), ("$images", images), ("$dueStart", dueStart), ("$dueEnd", dueEnd));
        for (var i = 0; i < tags.Count; i++) command.Parameters.AddWithValue($"$tag{i}", tags[i]);
        using var reader = command.ExecuteReader();
        var cards = new List<Card>();
        while (reader.Read())
            cards.Add(new Card(reader.GetInt64(0), reader.GetString(1),
                string.Join("\n", ImageMarkers.Strip(reader.GetString(2)).Replace("\r\n", "\n").Split('\n').Take(6)),
                Optional(reader, 3), reader.GetBoolean(4), reader.GetString(5), reader.GetString(6),
                Optional(reader, 7), reader.GetInt32(8), reader.IsDBNull(9) ? null : (byte[])reader.GetValue(9),
                reader.IsDBNull(10) ? null : ChecklistText.Progress(reader.GetString(10))));
        return cards;
    }
    public void Dispose() => db.Dispose();

    public void ArchiveInactive(int days) => Execute("""
        UPDATE notes SET is_archived=1 WHERE is_archived=0 AND is_pinned=0 AND desktop_pin IS NULL
          AND deleted_at IS NULL AND updated_at < $cutoff
        """, ("$cutoff", Timestamp(DateTimeOffset.UtcNow.AddDays(-days))));

    public string? DesktopPin(long id)
    {
        using var command = Command("SELECT desktop_pin FROM notes WHERE id=$id", ("$id", id));
        return command.ExecuteScalar() as string;
    }

    public long[] DesktopPins()
    {
        using var command = Command("SELECT id FROM notes WHERE desktop_pin IS NOT NULL AND deleted_at IS NULL ORDER BY id");
        using var rows = command.ExecuteReader();
        var ids = new List<long>();
        while (rows.Read()) ids.Add(rows.GetInt64(0));
        return ids.ToArray();
    }

    public void SetDesktopPin(long id, string? layout) =>
        Execute("UPDATE notes SET desktop_pin=$layout WHERE id=$id", ("$id", id), ("$layout", layout));
    public void VerifyWritable() => Execute("BEGIN IMMEDIATE; ROLLBACK;");
    internal long SupportNoteCount()
    {
        using var command = Command("SELECT count(*) FROM notes WHERE deleted_at IS NULL");
        return (long)command.ExecuteScalar()!;
    }
    public void Checkpoint() => Execute("PRAGMA wal_checkpoint(TRUNCATE)");
}
