using System.Security.Cryptography;
using System.Text;

namespace KaralamaDefteri;

public sealed partial class NoteStore
{
    private const int SchemaVersion = 7;
    private static readonly string DeviceId = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes($"KaralamaDefteri:{Environment.MachineName.ToUpperInvariant()}")))
        .ToLowerInvariant();

    internal static string NewUuid()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var index = 5; index >= 0; index--)
        {
            bytes[index] = (byte)timestamp;
            timestamp >>= 8;
        }
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true).ToString("D");
    }

    private void MigrateRecordMetadata()
    {
        Execute("""
            CREATE TABLE deleted_records (
                uuid TEXT NOT NULL, kind TEXT NOT NULL, deleted_at TEXT NOT NULL, device_id TEXT NOT NULL,
                PRIMARY KEY (uuid, kind)
            );
            CREATE INDEX deleted_records_age ON deleted_records(deleted_at);
            """);
        AddRecordMetadata("notes", "note", "created_at", "title,title_manual,content,syntax,color,is_pinned,is_checklist,is_archived,deleted_at,desktop_pin,group_id");
        AddRecordMetadata("groups", "group", "created_at", "name,color,sort_order,is_archived");
        AddRecordMetadata("attachments", "attachment", "created_at", "note_id,mime,data,thumb,width,height,file_name,orphaned_at,position");
        AddRecordMetadata("tags", "tag", "utc_ms()", "name");
    }

    private void AddRecordMetadata(string table, string kind, string initialTime, string editableColumns)
    {
        if (table != "notes") Execute($"ALTER TABLE {table} ADD COLUMN updated_at TEXT NOT NULL DEFAULT ''");
        Execute($"""
            ALTER TABLE {table} ADD COLUMN uuid TEXT NOT NULL DEFAULT '';
            ALTER TABLE {table} ADD COLUMN device_id TEXT NOT NULL DEFAULT '';
            """);
        var time = table == "notes" ? "updated_at" : initialTime;
        Execute($"""
            UPDATE {table}
            SET uuid=uuid_v7(), device_id=local_device(),
                updated_at=coalesce(strftime('%Y-%m-%dT%H:%M:%fZ',{time}),utc_ms());
            CREATE UNIQUE INDEX {table}_uuid ON {table}(uuid);
            CREATE TRIGGER {table}_metadata_insert AFTER INSERT ON {table} BEGIN
                UPDATE {table} SET uuid=CASE WHEN new.uuid='' THEN uuid_v7() ELSE new.uuid END,
                    updated_at=utc_ms(), device_id=local_device() WHERE id=new.id;
                DELETE FROM deleted_records WHERE uuid=new.uuid AND kind='{kind}';
            END;
            CREATE TRIGGER {table}_metadata_update AFTER UPDATE OF {editableColumns} ON {table} BEGIN
                UPDATE {table} SET updated_at=utc_ms(), device_id=local_device() WHERE id=new.id;
            END;
            CREATE TRIGGER {table}_uuid_immutable BEFORE UPDATE OF uuid ON {table}
            WHEN old.uuid<>'' AND new.uuid<>old.uuid BEGIN
                SELECT RAISE(ABORT,'Kayıt kimliği değiştirilemez');
            END;
            CREATE TRIGGER {table}_tombstone AFTER DELETE ON {table} BEGIN
                INSERT OR REPLACE INTO deleted_records(uuid,kind,deleted_at,device_id)
                VALUES(old.uuid,'{kind}',utc_ms(),local_device());
            END;
            """);
    }

    private void PruneDeletedRecords() => Execute("DELETE FROM deleted_records WHERE deleted_at < $cutoff",
        ("$cutoff", Timestamp(DateTimeOffset.UtcNow.AddDays(-90))));
}
