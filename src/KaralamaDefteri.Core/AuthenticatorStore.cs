using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class NoteStore
{
    private void RequireAuthenticator()
    {
        if (!IsEncrypted) throw new InvalidOperationException("Doğrulayıcı için önce şifrelemeyi açın.");
        Execute("""
            CREATE TABLE IF NOT EXISTS authenticator_accounts(
                uuid TEXT PRIMARY KEY, issuer TEXT NOT NULL, account_name TEXT NOT NULL, secret TEXT NOT NULL,
                algorithm TEXT NOT NULL, digits INTEGER NOT NULL, period INTEGER NOT NULL, counter INTEGER,
                group_id INTEGER REFERENCES groups(id) ON DELETE SET NULL, color TEXT, note_uuid TEXT,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS authenticator_deleted(uuid TEXT PRIMARY KEY, deleted_at TEXT NOT NULL);
            """);
    }

    internal static bool HasAuthenticatorAccounts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_schema WHERE name='authenticator_accounts'";
        if ((long)command.ExecuteScalar()! == 0) return false;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM authenticator_accounts)";
        return (long)command.ExecuteScalar()! != 0;
    }

    internal bool HasAuthenticatorAccounts() => IsEncrypted && HasAuthenticatorAccounts(db);

    internal bool HasAuthenticatorForNote(Guid note)
    {
        RequireAuthenticator();
        using var command = Command("SELECT EXISTS(SELECT 1 FROM authenticator_accounts WHERE note_uuid=$note)", ("$note", note.ToString()));
        return (long)command.ExecuteScalar()! != 0;
    }

    internal IReadOnlyList<OtpAccount> AuthenticatorAccounts()
    {
        RequireAuthenticator();
        using var command = Command("SELECT uuid, issuer, account_name, secret, algorithm, digits, period, counter, group_id, color, note_uuid FROM authenticator_accounts ORDER BY issuer, account_name");
        using var row = command.ExecuteReader();
        var accounts = new List<OtpAccount>();
        while (row.Read())
            accounts.Add(new(Guid.Parse(row.GetString(0)), row.GetString(1), row.GetString(2), row.GetString(3),
                row.GetString(4), row.GetInt32(5), row.GetInt32(6), row.IsDBNull(7) ? null : row.GetInt64(7),
                row.IsDBNull(8) ? null : row.GetInt64(8), row.IsDBNull(9) ? null : row.GetString(9),
                row.IsDBNull(10) ? null : Guid.Parse(row.GetString(10))));
        return accounts;
    }

    internal void SaveAuthenticatorAccounts(IEnumerable<OtpAccount> accounts)
    {
        RequireAuthenticator();
        using var transaction = db.BeginTransaction();
        foreach (var account in accounts)
        {
            Authenticator.Validate(account);
            using var command = Command("""
                INSERT INTO authenticator_accounts VALUES($id,$issuer,$name,$secret,$algorithm,$digits,$period,$counter,$group,$color,$note,$now,$now)
                ON CONFLICT(uuid) DO UPDATE SET issuer=excluded.issuer,account_name=excluded.account_name,secret=excluded.secret,
                algorithm=excluded.algorithm,digits=excluded.digits,period=excluded.period,counter=excluded.counter,
                group_id=excluded.group_id,color=excluded.color,note_uuid=excluded.note_uuid,updated_at=excluded.updated_at
                """, ("$id", account.Id.ToString()), ("$issuer", account.Issuer), ("$name", account.Name),
                ("$secret", account.Secret), ("$algorithm", account.Algorithm), ("$digits", account.Digits),
                ("$period", account.Period), ("$counter", account.Counter), ("$group", account.Group),
                ("$color", account.Color), ("$note", account.Note?.ToString()), ("$now", Now()));
            command.Transaction = transaction;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    internal void RemoveAuthenticatorAccount(Guid id)
    {
        RequireAuthenticator();
        Atomic(() =>
        {
            Execute("DELETE FROM authenticator_accounts WHERE uuid=$id", ("$id", id.ToString()));
            Execute("INSERT OR REPLACE INTO authenticator_deleted VALUES($id,$time)", ("$id", id.ToString()), ("$time", Now()));
        });
    }

    private sealed record SyncedAuthenticator(OtpAccount? Account, string? Group, string Time);
    private static readonly Guid AuthenticatorSyncId = new("6903dfbd-73a0-49bb-8116-71ca27167ab2");

    private void SyncAuthenticator(ISyncProvider provider, SyncEncryption crypto)
    {
        if (!IsEncrypted) return;
        var accounts = AuthenticatorAccounts().ToDictionary(account => account.Id);
        var groups = Groups().ToDictionary(group => group.Id, group => group.Name);
        var local = new Dictionary<Guid, SyncedAuthenticator>();
        using (var command = Command("SELECT uuid, updated_at, 0 FROM authenticator_accounts UNION ALL SELECT uuid, deleted_at, 1 FROM authenticator_deleted"))
        using (var rows = command.ExecuteReader())
            while (rows.Read())
            {
                var id = Guid.Parse(rows.GetString(0));
                var account = rows.GetInt32(2) == 0 ? accounts[id] : null;
                var group = account?.Group is long groupId ? groups.GetValueOrDefault(groupId) : null;
                local[id] = new(account == null ? null : account with { Group = null }, group, rows.GetString(1));
            }
        var remote = provider.GetBlob(AuthenticatorSyncId) is { } bytes
            ? crypto.Open<Dictionary<Guid, SyncedAuthenticator>>(AuthenticatorSyncId, "authenticator", bytes)
            : [];
        var publish = false;
        foreach (var (id, record) in local)
            if (!remote.TryGetValue(id, out var other) || string.CompareOrdinal(record.Time, other.Time) > 0)
            {
                remote[id] = record;
                publish = true;
            }
        foreach (var (id, record) in remote)
        {
            if (local.TryGetValue(id, out var existing) && string.CompareOrdinal(existing.Time, record.Time) >= 0) continue;
            if (record.Account is { } account)
            {
                SaveAuthenticatorAccounts([account with { Id = id, Group = record.Group == null ? null : GetOrCreateGroup(record.Group) }]);
                Execute("UPDATE authenticator_accounts SET updated_at=$time WHERE uuid=$id; DELETE FROM authenticator_deleted WHERE uuid=$id",
                    ("$time", record.Time), ("$id", id.ToString()));
            }
            else
            {
                RemoveAuthenticatorAccount(id);
                Execute("UPDATE authenticator_deleted SET deleted_at=$time WHERE uuid=$id", ("$time", record.Time), ("$id", id.ToString()));
            }
        }
        if (publish) provider.PutBlob(AuthenticatorSyncId, crypto.Seal(AuthenticatorSyncId, "authenticator", remote));
    }
}
