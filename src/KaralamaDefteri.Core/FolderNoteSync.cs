using System.Security.Cryptography;
using System.Text.Json;

namespace KaralamaDefteri;

public sealed partial class NoteStore
{
    private sealed record SyncNote(Guid Id, string Time, string Device, string Title, string Content,
        bool Manual, string? Color, string? Syntax, string? Group, bool Archived, bool Pinned, bool Checklist,
        string? Deleted, Dictionary<long, Guid> Pictures, Guid? ConflictOf = null, BoardPosition? Board = null,
        NoteDeadline? Deadline = null, string? GroupUuid = null, bool Permanent = false);
    private sealed record SyncPicture(string Mime, byte[] Bytes, byte[] Thumbnail, int Width, int Height, string? Name);

    internal int SyncFolder(string directory, string password, IReadOnlySet<Guid>? openNotes = null)
    {
        var provider = new FolderSyncProvider(directory);
        using var lease = provider.BeginSync();
        return SyncNotes(provider, password, provider.GetOrCreateSalt(), provider.SaveDailyBackup, openNotes);
    }

    internal int SyncNotes(ISyncProvider provider, string password, byte[] salt, Action<Func<byte[]>> saveBackup,
        IReadOnlySet<Guid>? openNotes = null)
    {
        using var crypto = new SyncEncryption(password, salt);
        return SyncNotes(provider, crypto, salt, saveBackup, openNotes);
    }

    internal int SyncNotes(ISyncProvider provider, SyncEncryption crypto, byte[] salt, Action<Func<byte[]>> saveBackup,
        IReadOnlySet<Guid>? openNotes = null, Action? localChanges = null, Func<Guid, bool>? isNoteEditing = null)
    {
        var cloud = provider as CloudSyncProvider;
        cloud?.VerifyKey(crypto);
        Dictionary<Guid, SyncNote> remote;
        try
        {
            remote = provider.ListRecords().ToDictionary(id => id, id =>
                crypto.Open<SyncNote>(id, "note", provider.GetRecord(id) ?? throw new IOException("Senkron kaydı taşındı.")));
        }
        catch (AuthenticationTagMismatchException error) when (cloud != null)
        {
            throw new CloudSyncKeyMismatchException(error);
        }
        var device = Setting("syncDevice");
        if (device == null)
        {
            device = Guid.NewGuid().ToString("N");
            Set("syncDevice", device);
        }
        var stateKey = "sync:" + Convert.ToHexString(SHA256.HashData(salt));
        var groupAliases = SyncGroups(provider, crypto, stateKey, localChanges);
        var seen = JsonSerializer.Deserialize<Dictionary<Guid, string>>(Setting(stateKey) ?? "{}")!;
        var localIds = ReadNoteIds();
        var local = ReadSyncNotes(device, provider, crypto, localIds.Values, groupAliases);
        var purged = new Dictionary<Guid, string>();
        using (var query = Command("SELECT uuid,deleted_at FROM deleted_records WHERE kind='note'"))
        using (var reader = query.ExecuteReader())
            while (reader.Read()) purged[Guid.Parse(reader.GetString(0))] = reader.GetString(1);

        SyncBoardColumns(provider, crypto);
        var conflicts = 0;
        var completed = 0;
        try
        {
            foreach (var id in local.Keys.Union(remote.Keys))
            {
                // Never perform provider I/O while holding the database writer lock.
                local.TryGetValue(id, out var ours);
                remote.TryGetValue(id, out var theirs);
                if (theirs?.Permanent == true || ours is null && purged.ContainsKey(id))
                {
                    if (openNotes?.Contains(id) == true || isNoteEditing?.Invoke(id) == true) continue;
                    if (theirs?.Permanent != true)
                    {
                        var marker = new SyncNote(id, purged[id], device, "", "", false, null, null, null,
                            false, false, false, NoteStore.Timestamp(DateTimeOffset.UnixEpoch), [], Permanent: true);
                        provider.PutRecord(id, crypto.Seal(id, "note", marker));
                    }
                    else if (ours is not null)
                    {
                        using var delete = Command("DELETE FROM notes WHERE uuid=$uuid", ("$uuid", id.ToString()));
                        if (delete.ExecuteNonQuery() != 0) localChanges?.Invoke();
                    }
                    if (seen.Remove(id)) completed++;
                    continue;
                }
                seen.TryGetValue(id, out var previous);
                var localHash = ours == null ? null : Fingerprint(ours);
                var remoteHash = theirs == null ? null : Fingerprint(theirs);
                if ((openNotes?.Contains(id) == true || isNoteEditing?.Invoke(id) == true) &&
                    theirs != null && remoteHash != previous && remoteHash != localHash)
                    continue;
                var board = LatestBoardPosition(ours?.Board, theirs?.Board);
                var deadline = NoteDeadline.Latest(ours?.Deadline, theirs?.Deadline);
                if (ours == null && previous != null && theirs is { Deleted: null })
                {
                    ours = theirs with { Deleted = Now(), Time = Now(), Device = device };
                    localHash = Fingerprint(ours);
                }
                SyncNote? copy = null;
                if (ours != null && theirs != null && localHash != remoteHash && previous != localHash && previous != remoteHash)
                {
                    var remoteWins = string.CompareOrdinal(theirs.Time, ours.Time) > 0 ||
                        theirs.Time == ours.Time && string.CompareOrdinal(theirs.Device, ours.Device) > 0;
                    var losing = remoteWins ? ours : theirs;
                    if (losing.Deleted == null)
                    {
                        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id + Fingerprint(losing)));
                        copy = losing with { Id = new Guid(digest.AsSpan(0, 16)), ConflictOf = id, Manual = true,
                            Title = $"Çakışma kopyası – {losing.Device[..8]} – {losing.Time} · {losing.Title}" };
                    }
                    if (remoteWins) ours = null;
                    else theirs = null;
                }
                var pull = theirs != null && (ours == null || localHash == previous && localHash != remoteHash);
                var merged = (pull ? theirs! : ours!) with { Board = board, Deadline = deadline };
                var apply = pull ? PrepareSyncNote(merged, provider, crypto, groupAliases) : null;
                var applyCopy = copy == null ? null : PrepareSyncNote(copy, provider, crypto, groupAliases);
                var existing = local.GetValueOrDefault(id);
                if (apply != null || applyCopy != null || existing != null && (existing.Board != board || existing.Deadline != deadline))
                {
                    var applied = false;
                    Atomic(() =>
                    {
                        if (existing == null && FindSyncNoteId(id) != null) return;
                        if (apply != null && isNoteEditing?.Invoke(id) == true) return;
                        if (existing != null && (Get(localIds[id])?.Updated != existing.Time ||
                            ReadDeadline(localIds[id]) != existing.Deadline ||
                            (existing.Board != null || board != null) && ReadBoardPosition(localIds[id]) != existing.Board))
                            return;
                        applyCopy?.Invoke();
                        if (apply != null) apply();
                        else if (existing != null)
                        {
                            if (board != null && board != existing.Board) SetBoardPosition(localIds[id], board);
                            if (deadline != null && deadline != existing.Deadline) WriteDeadline(localIds[id], deadline);
                        }
                        applied = true;
                    });
                    if (!applied) continue;
                    localChanges?.Invoke();
                }
                if (copy != null)
                {
                    provider.PutRecord(copy.Id, crypto.Seal(copy.Id, "note", copy));
                    seen[copy.Id] = Fingerprint(copy);
                    conflicts++;
                }
                var originalRemote = remote.GetValueOrDefault(id);
                if (originalRemote == null || (!pull && localHash != remoteHash) ||
                    originalRemote.Board != board || originalRemote.Deadline != deadline)
                    provider.PutRecord(id, crypto.Seal(id, "note", merged));
                var fingerprint = Fingerprint(merged);
                if (previous != fingerprint)
                {
                    seen[id] = fingerprint;
                    if (++completed % 50 == 0) SaveProgress();
                }
            }
        }
        finally { if (completed != 0 || conflicts != 0) SaveProgress(); }

        void SaveProgress() => Set(stateKey, JsonSerializer.Serialize(seen));
        SyncAuthenticator(provider, crypto);
        new FeatureFlags(this).Sync(provider, crypto);
        cloud?.SaveKeyVerifier(crypto);
        saveBackup(() =>
        {
            var temporary = db.DataSource + ".sync-backup";
            var backupKey = Convert.ToHexString(crypto.BackupKey());
            try
            {
                File.Delete(temporary);
                ExportDatabase(db.DataSource, databasePassword, temporary, backupKey);
                return File.ReadAllBytes(temporary);
            }
            finally { File.Delete(temporary); }
        });
        return conflicts;
    }

    private Dictionary<Guid, long> ReadNoteIds()
    {
        using var command = Command("SELECT uuid,id FROM notes");
        using var rows = command.ExecuteReader();
        var result = new Dictionary<Guid, long>();
        while (rows.Read()) result.Add(Guid.Parse(rows.GetString(0)), rows.GetInt64(1));
        return result;
    }

    private static BoardPosition? LatestBoardPosition(BoardPosition? local, BoardPosition? remote)
    {
        var compare = string.CompareOrdinal(local?.Updated, remote?.Updated);
        if (compare == 0) compare = string.CompareOrdinal(local?.Column, remote?.Column);
        if (compare == 0) compare = Nullable.Compare(local?.Order, remote?.Order);
        return compare >= 0 ? local : remote;
    }

    private static string Fingerprint(SyncNote note)
    {
        var content = ImageMarkers.Pattern().Replace(note.Content, marker =>
            note.Pictures.TryGetValue(ImageMarkers.Id(marker), out var key) ? $"[[resim:{key:N}]]" : marker.Value);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { note.Id, Time = "", Device = "", note.Title, Content = content, note.Manual, note.Color, note.Syntax,
                note.Group, note.Archived, note.Pinned, note.Checklist, note.Deleted, Pictures = new Dictionary<long, Guid>(),
                ConflictOf = (Guid?)null })));
    }

    private Dictionary<Guid, SyncNote> ReadSyncNotes(string device, ISyncProvider provider, SyncEncryption crypto,
        IEnumerable<long> ids, Dictionary<string, string> groupAliases)
    {
        var groups = Groups().ToDictionary(group => group.Id);
        var positions = BoardPositions();
        var result = new Dictionary<Guid, SyncNote>();
        foreach (var id in ids)
        {
            if (Get(id) is not { } note || !positions.TryGetValue(id, out var position)) continue;
            var pictures = new Dictionary<long, Guid>();
            using var query = Command("SELECT id,mime,data,thumb,width,height,file_name FROM attachments WHERE note_id=$id AND orphaned_at IS NULL", ("$id", id));
            using var attachments = query.ExecuteReader();
            while (attachments.Read())
            {
                var image = new SyncPicture(attachments.GetString(1), (byte[])attachments.GetValue(2),
                    attachments.IsDBNull(3) ? [] : (byte[])attachments.GetValue(3),
                    attachments.IsDBNull(4) ? 0 : attachments.GetInt32(4), attachments.IsDBNull(5) ? 0 : attachments.GetInt32(5), Optional(attachments, 6));
                var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(image));
                var key = new Guid(hash.AsSpan(0, 16));
                if (provider.GetBlob(key) == null) provider.PutBlob(key, crypto.Seal(key, "picture", image));
                pictures.Add(attachments.GetInt64(0), key);
            }
            var uuid = Guid.Parse(note.Uuid);
            var group = note.GroupId is long groupId ? groups.GetValueOrDefault(groupId) : null;
            result.Add(uuid, new(uuid, note.Updated, device, note.Title, note.Content, note.Manual, note.Color, note.Syntax,
                group?.Name, note.Archived, note.Pinned, note.Checklist, note.Deleted, pictures,
                Board: position.Updated == null ? null : position, Deadline: ReadDeadline(id),
                GroupUuid: group is null ? null : groupAliases.GetValueOrDefault(group.Uuid) ?? group.Uuid));
        }
        return result;
    }

    private Action PrepareSyncNote(SyncNote record, ISyncProvider provider, SyncEncryption crypto,
        Dictionary<string, string> groupAliases)
    {
        var pictures = ImageMarkers.Pattern().Matches(record.Content).Select(ImageMarkers.Id)
            .Distinct().Where(record.Pictures.ContainsKey).ToDictionary(id => id, id =>
            {
                var key = record.Pictures[id];
                var blob = provider.GetBlob(key) ?? throw new IOException("Senkron resmi henüz klasöre ulaşmadı.");
                return crypto.Open<SyncPicture>(key, "picture", blob);
            });
        return () => ApplySyncNote(record, pictures, groupAliases);
    }

    private void ApplySyncNote(SyncNote record, Dictionary<long, SyncPicture> pictures, Dictionary<string, string> groupAliases)
    {
        var id = FindSyncNoteId(record.Id);
        var group = record.GroupUuid is { } uuid
            ? Groups().FirstOrDefault(item => (groupAliases.GetValueOrDefault(item.Uuid) ?? item.Uuid) == uuid)?.Id
            : record.Group == null ? (long?)null : GetOrCreateGroup(record.Group);
        if (id == null)
        {
            if (record.Deleted != null && DateTimeOffset.TryParse(record.Deleted, out var deleted) &&
                deleted < DateTimeOffset.UtcNow.AddDays(-30)) return;
            using var insert = Command("""
                INSERT INTO notes(uuid,title,content,title_manual,created_at,updated_at,group_id)
                VALUES($uuid,$title,'',1,$time,$time,$group) RETURNING id
                """, ("$uuid", record.Id.ToString()), ("$title", record.Title), ("$time", record.Time), ("$group", group));
            id = (long)insert.ExecuteScalar()!;
        }
        var note = Get(id.Value)!;
        var imported = new Dictionary<long, long>();
        note.Content = ImageMarkers.Pattern().Replace(record.Content, marker =>
        {
            var old = ImageMarkers.Id(marker);
            if (!pictures.TryGetValue(old, out var p)) return marker.Value;
            if (!imported.TryGetValue(old, out var attachment))
            {
                imported[old] = attachment = InsertPicture(note.Id, new Picture(p.Mime, p.Bytes, p.Thumbnail, p.Width, p.Height, p.Name));
            }
            return ImageMarkers.Marker(attachment);
        });
        note.Title = record.Title;
        note.Manual = record.Manual;
        note.Color = record.Color;
        note.Syntax = record.Syntax;
        note.GroupId = group;
        note.Archived = record.Archived;
        note.Pinned = record.Pinned;
        note.Checklist = record.Checklist;
        note.Deleted = record.Deleted;
        Save(note);
        SyncPictures(note);
        if (record.Board is { } board) SetBoardPosition(note.Id, board);
        if (record.Deadline is { } deadline) WriteDeadline(note.Id, deadline);
        Execute("UPDATE notes SET updated_at=$time WHERE id=$id", ("$time", record.Time), ("$id", note.Id));
    }

    private long? FindSyncNoteId(Guid uuid)
    {
        using var find = Command("SELECT id FROM notes WHERE uuid=$uuid", ("$uuid", uuid.ToString()));
        return find.ExecuteScalar() as long?;
    }
}
