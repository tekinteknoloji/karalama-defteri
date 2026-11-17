using System.Text.Json;

namespace KaralamaDefteri;

public sealed partial class NoteStore
{
    private static readonly Guid GroupSyncId = new("302be7d1-ffa2-4084-b40d-ac6ab804b258");
    private sealed record GroupRecord(string? Name, string? Color, bool Archived, int Order, string Time, string Device, bool Deleted = false);
    private static readonly Guid GroupTreeSyncId = new("ff4766f8-02b7-4a2c-9407-c1df8ac98620");

    private Dictionary<string, string> SyncGroups(ISyncProvider provider, SyncEncryption crypto, string stateKey, Action? localChanges)
    {
        var groups = Groups();
        var remote = provider.GetBlob(GroupSyncId) is { } blob
            ? crypto.Open<Dictionary<string, GroupRecord>>(GroupSyncId, "groups", blob) : [];
        var aliasKey = stateKey + ":groups";
        var aliases = JsonSerializer.Deserialize<Dictionary<string, string>>(Setting(aliasKey) ?? "{}")!;
        string Canonical(string uuid) => aliases.GetValueOrDefault(uuid) ?? uuid;
        var locals = new Dictionary<string, NoteGroup>();
        foreach (var group in groups)
        {
            if (!aliases.ContainsKey(group.Uuid) && !remote.ContainsKey(group.Uuid))
            {
                var match = remote.FirstOrDefault(record => !record.Value.Deleted && record.Value.Name is { } name &&
                    Fold(name) == Fold(group.Name));
                if (match.Key is not null) aliases[group.Uuid] = match.Key;
            }
            locals.TryAdd(Canonical(group.Uuid), group);
        }
        var tombstones = new Dictionary<string, GroupRecord>();
        using (var command = Command("SELECT uuid,deleted_at,device_id FROM deleted_records WHERE kind='group'"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var key = Canonical(reader.GetString(0));
                if (locals.ContainsKey(key)) continue;
                var tombstone = new GroupRecord(remote.GetValueOrDefault(key)?.Name, null, false, 0,
                    reader.GetString(1), reader.GetString(2), true);
                if (Newer(tombstone, tombstones.GetValueOrDefault(key))) tombstones[key] = tombstone;
            }

        var changed = false;
        Atomic(() =>
        {
            foreach (var key in locals.Keys.Union(tombstones.Keys).Union(remote.Keys).ToArray())
            {
                var group = locals.GetValueOrDefault(key);
                var local = group is null ? tombstones.GetValueOrDefault(key) :
                    new GroupRecord(group.Name, group.Color, group.Archived, group.Order, group.UpdatedAt, group.DeviceId);
                var incoming = remote.GetValueOrDefault(key);
                if (local is not null && Newer(local, incoming))
                {
                    remote[key] = local;
                    changed = true;
                    continue;
                }
                if (incoming is null || incoming == local) continue;
                if (incoming.Deleted)
                {
                    if (group is null) continue;
                    DeleteGroup(group.Id);
                    Execute("UPDATE deleted_records SET deleted_at=$time, device_id=$device WHERE uuid=$uuid AND kind='group'",
                        ("$time", incoming.Time), ("$device", incoming.Device), ("$uuid", group.Uuid));
                }
                else
                {
                    if (incoming.Name is not { Length: > 0 } name || Groups().Any(other =>
                        other.Id != group?.Id && Fold(other.Name) == Fold(name))) continue;
                    if (group is null)
                        Execute("""
                            INSERT INTO groups(name,color,is_archived,sort_order,created_at,uuid)
                            VALUES($name,$color,$archived,$order,$time,$uuid)
                            """, ("$name", name), ("$color", incoming.Color), ("$archived", incoming.Archived),
                            ("$order", incoming.Order), ("$time", incoming.Time), ("$uuid", key));
                    else UpdateGroup(group with { Name = name, Color = incoming.Color, Archived = incoming.Archived, Order = incoming.Order });
                    Execute("UPDATE groups SET updated_at=$time, device_id=$device WHERE uuid=$uuid",
                        ("$time", incoming.Time), ("$device", incoming.Device), ("$uuid", group?.Uuid ?? key));
                }
                localChanges?.Invoke();
            }
            Set(aliasKey, JsonSerializer.Serialize(aliases));
        });
        if (changed) provider.PutBlob(GroupSyncId, crypto.Seal(GroupSyncId, "groups", remote));
        SyncGroupTree(provider, crypto, aliases, localChanges);
        return aliases;

        static bool Newer(GroupRecord actual, GroupRecord? other)
        {
            if (other is null) return true;
            var time = string.CompareOrdinal(actual.Time, other.Time);
            return time > 0 || time == 0 && string.CompareOrdinal(actual.Device, other.Device) > 0;
        }
    }

    // Kept apart from the legacy group snapshot, which older clients can rewrite.
    private void SyncGroupTree(ISyncProvider provider, SyncEncryption crypto, Dictionary<string, string> aliases,
        Action? localChanges)
    {
        string Canonical(string id) => aliases.GetValueOrDefault(id) ?? id;
        var groups = Groups();
        var byCanonical = groups.ToLookup(group => Canonical(group.Uuid), StringComparer.Ordinal);
        var parents = provider.GetBlob(GroupTreeSyncId) is { } blob
            ? crypto.Open<Dictionary<string, string>>(GroupTreeSyncId, "group-tree", blob) : [];
        var upload = false;
        var changed = false;
        Atomic(() =>
        {
            foreach (var group in groups)
            {
                var child = Canonical(group.Uuid);
                if (!parents.TryGetValue(child, out var parent))
                {
                    if (group.ParentUuid is not { } current) continue;
                    parent = Canonical(current);
                    parents.Add(child, parent);
                    upload = true;
                }
                var localParent = byCanonical[parent].FirstOrDefault()?.Uuid ?? parent;
                if (localParent == group.Uuid || localParent == group.ParentUuid) continue;
                Execute("UPDATE groups SET parent_uuid=$parent WHERE id=$id",
                    ("$parent", localParent), ("$id", group.Id));
                changed = true;
            }
        });
        if (changed) localChanges?.Invoke();
        if (upload) provider.PutBlob(GroupTreeSyncId, crypto.Seal(GroupTreeSyncId, "group-tree", parents));
    }
}
