using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class NoteStore
{
    public string Backup(string directory, string prefix = "notes")
    {
        Directory.CreateDirectory(directory);
        var name = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmm}";
        var target = Path.Combine(directory, name + ".db");
        for (var suffix = 1; File.Exists(target); suffix++)
            target = Path.Combine(directory, $"{name}_{suffix}.db");
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (databasePassword == null)
                Execute("VACUUM INTO $destination", ("$destination", temporary));
            else
                ExportDatabase(db.DataSource, databasePassword, temporary, databasePassword);
            File.Move(temporary, target);
            return target;
        }
        finally { File.Delete(temporary); }
    }

    public void DailyBackup(string directory)
    {
        var pattern = $"daily_{DateTime.Now:yyyyMMdd}_*.db";
        if (!Directory.Exists(directory) || !Directory.EnumerateFiles(directory, pattern).Any())
            Backup(directory, "daily");
        foreach (var old in Directory.EnumerateFiles(directory, "daily_????????_????.db")
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(7))
            File.Delete(old);
    }

    public void RestoreBackup(string path, string safetyDirectory, string? backupPassword = null)
    {
        var inputPassword = backupPassword ?? databasePassword;
        ValidateBackup(path, inputPassword);
        if (databasePassword != null)
        {
            using var source = OpenDatabase(path, inputPassword, SqliteOpenMode.ReadOnly);
            using var version = source.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            if ((long)version.ExecuteScalar()! == SchemaVersion)
            {
                RestoreEncryptedContents(path, safetyDirectory, inputPassword!);
                return;
            }
            var temporary = Path.Combine(safetyDirectory, Guid.NewGuid().ToString("N") + ".tmp");
            Directory.CreateDirectory(safetyDirectory);
            try
            {
                ExportDatabase(path, inputPassword, temporary, databasePassword);
                using (var migrated = new NoteStore(temporary, databasePassword, syncConnection: true))
                {
                    migrated.Migrate();
                    migrated.Checkpoint();
                }
                RestoreEncryptedContents(temporary, safetyDirectory, databasePassword);
            }
            finally { File.Delete(temporary); }
            return;
        }
        using var plainSource = OpenDatabase(path, databasePassword, SqliteOpenMode.ReadOnly);
        Backup(safetyDirectory, "geri_yukleme_oncesi");
        plainSource.BackupDatabase(db);
        Checkpoint();
    }

    private void RestoreEncryptedContents(string path, string safetyDirectory, string sourcePassword)
    {
        Backup(safetyDirectory, "geri_yukleme_oncesi");
        Execute("ATTACH DATABASE $file AS snapshot KEY $key", ("$file", path), ("$key", sourcePassword));
        try
        {
            Atomic(() =>
            {
                using var version = Command("PRAGMA snapshot.user_version");
                if ((long)version.ExecuteScalar()! != SchemaVersion)
                    throw new InvalidDataException("Şifreli yedek için veritabanı şeması eşleşmiyor.");
                using var triggerQuery = Command("SELECT name,sql FROM main.sqlite_schema WHERE type='trigger'");
                using var triggerRows = triggerQuery.ExecuteReader();
                var triggers = new List<(string Name, string Sql)>();
                while (triggerRows.Read()) triggers.Add((triggerRows.GetString(0), triggerRows.GetString(1)));
                triggerRows.Close();
                foreach (var trigger in triggers) Execute("DROP TRIGGER \"" + trigger.Name.Replace("\"", "\"\"") + "\"");
                var tables = new[] { "groups", "notes", "attachments", "attachment_originals", "tags", "note_tags", "settings", "deleted_records" };
                foreach (var table in tables.Reverse()) Execute($"DELETE FROM main.{table}");
                foreach (var table in tables)
                {
                    using var select = Command($"SELECT name FROM pragma_table_info('{table}')");
                    using var rows = select.ExecuteReader();
                    var columns = new List<string>();
                    while (rows.Read()) columns.Add("\"" + rows.GetString(0).Replace("\"", "\"\"") + "\"");
                    rows.Close();
                    var list = string.Join(",", columns);
                    Execute($"INSERT INTO main.{table}({list}) SELECT {list} FROM snapshot.{table}");
                }
                Execute("DELETE FROM main.sqlite_sequence; INSERT INTO main.sqlite_sequence SELECT * FROM snapshot.sqlite_sequence;");
                Execute("INSERT INTO notes_fts(notes_fts) VALUES('rebuild')");
                foreach (var trigger in triggers) Execute(trigger.Sql);
            });
        }
        finally { Execute("DETACH DATABASE snapshot"); }
        Checkpoint();
    }

    internal void ValidateBackup(string path, string? password)
    {
        if (FileTypePathEquals(db.DataSource, path))
            throw new InvalidDataException("Açık veritabanı yedek olarak seçilemez.");
        using var source = OpenDatabase(path, password, SqliteOpenMode.ReadOnly);
        source.CreateFunction<string, string>("search_fold", Fold, isDeterministic: true);
        using var command = source.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_schema WHERE type='table' AND name IN ('notes','settings','notes_fts','tags','note_tags')";
        if (!Equals(command.ExecuteScalar(), 5L))
            throw new InvalidDataException("Dosya bir Karalama Defteri yedeği olarak tanınmadı.");
        command.CommandText = "PRAGMA user_version";
        using var version = Command("PRAGMA user_version");
        if (Convert.ToInt64(command.ExecuteScalar()) > Convert.ToInt64(version.ExecuteScalar()))
            throw new InvalidDataException("Yedek daha yeni bir Karalama Defteri sürümü gerektiriyor.");
        command.CommandText = "PRAGMA integrity_check";
        using (var result = command.ExecuteReader())
            if (!result.Read() || result.GetString(0) != "ok" || result.Read())
                throw new InvalidDataException("Yedekte veritabanı bütünlük hatası var.");
    }

    private static bool FileTypePathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public string ExportMarkdown(Note note, string directory)
    {
        var images = Path.Combine(directory, "resimler");
        var content = ImageMarkers.Pattern().Replace(note.Content, marker =>
        {
            var picture = GetPicture(note.Id, ImageMarkers.Id(marker));
            if (picture == null) return marker.Value;
            var extension = picture.Mime switch
            {
                "image/jpeg" => ".jpg", "image/bmp" => ".bmp", "image/webp" => ".webp",
                "image/gif" => ".gif", _ => ".png"
            };
            var name = picture.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + extension;
            Directory.CreateDirectory(images);
            File.WriteAllBytes(Path.Combine(images, name), picture.Bytes);
            return $"![Resim](resimler/{name})";
        });
        return content;
    }

    public IReadOnlyList<Note> GroupNotes(long group)
    {
        using var command = Command("SELECT id FROM notes WHERE group_id=$id AND deleted_at IS NULL ORDER BY id", ("$id", group));
        using var reader = command.ExecuteReader();
        var ids = new List<long>();
        while (reader.Read()) ids.Add(reader.GetInt64(0));
        reader.Close();
        return ids.Select(id => Get(id)!).ToArray();
    }
}
