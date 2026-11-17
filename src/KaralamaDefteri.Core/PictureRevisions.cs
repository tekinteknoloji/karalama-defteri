namespace KaralamaDefteri;

public sealed partial class NoteStore
{
    public void ReplacePicture(long note, long id, Picture picture) => Atomic(() =>
    {
        Execute("""
            INSERT INTO attachment_originals(id,mime,data,thumb,width,height,expires)
            SELECT id,mime,data,thumb,width,height,$expires FROM attachments WHERE note_id=$note AND id=$id
            ON CONFLICT(id) DO UPDATE SET mime=excluded.mime,data=excluded.data,thumb=excluded.thumb,
              width=excluded.width,height=excluded.height,expires=excluded.expires
            WHERE attachment_originals.expires <= $now
            """, ("$expires", Timestamp(DateTimeOffset.UtcNow.AddDays(30))), ("$now", Now()), ("$note", note), ("$id", id));
        Execute("""
            UPDATE attachments SET mime=$mime,data=$data,thumb=$thumb,width=$width,height=$height,updated_at=$now,device_id=$device
            WHERE note_id=$note AND id=$id
            """, ("$mime", picture.Mime), ("$data", picture.Bytes), ("$thumb", picture.Thumbnail),
            ("$width", picture.Width), ("$height", picture.Height), ("$now", Now()), ("$device", DeviceId),
            ("$note", note), ("$id", id));
    });

    public bool HasOriginal(long note, long id)
    {
        using var command = Command("""
            SELECT 1 FROM attachment_originals o JOIN attachments a ON a.id=o.id
            WHERE o.id=$id AND a.note_id=$note AND expires>$now
            """, ("$id", id), ("$note", note), ("$now", Now()));
        return command.ExecuteScalar() != null;
    }

    public void RestoreOriginal(long note, long id) => Atomic(() =>
    {
        if (!HasOriginal(note, id)) return;
        Execute("""
            UPDATE attachments SET (mime,data,thumb,width,height) =
              (SELECT mime,data,thumb,width,height FROM attachment_originals WHERE id=$id),
              updated_at=$now,device_id=$device WHERE note_id=$note AND id=$id
            """, ("$id", id), ("$note", note), ("$now", Now()), ("$device", DeviceId));
        Execute("DELETE FROM attachment_originals WHERE id=$id", ("$id", id));
    });
}
