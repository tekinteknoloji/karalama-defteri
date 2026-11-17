using KaralamaDefteri;
using Xunit;

public sealed class CloudKeyTests
{
    [Theory]
    [InlineData(0, 5, true, true)]
    [InlineData(1, 5, true, false)]
    [InlineData(2, 5, true, false)]
    [InlineData(0, 60, false, true)]
    [InlineData(1, 3599, false, false)]
    [InlineData(1, 3600, false, true)]
    [InlineData(2, 3600, false, false)]
    [InlineData(2, 86400, false, true)]
    public void ScheduleRespectsSelectedFrequency(int frequency, int elapsed, bool edit, bool expected)
    {
        var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(3));
        Assert.Equal(expected, CloudSyncSchedule.ShouldRun(frequency, now, now.AddSeconds(-elapsed), new TimeOnly(9, 0), edit));
    }

    [Fact]
    public void DailyScheduleCatchesUpAfterMissingThePreviousDay()
    {
        var now = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.FromHours(3));
        Assert.True(CloudSyncSchedule.ShouldRun(2, now, now.AddDays(-2), new TimeOnly(9, 0), false));
        Assert.False(CloudSyncSchedule.ShouldRun(2, now, now.AddDays(-1).AddHours(2), new TimeOnly(9, 0), false));
    }

    [Fact]
    public void ResetOnlyRemovesObjectsFromSelectedArea()
    {
        using var http = new HttpClient();
        var storage = new MemoryStore(http);
        var original = new CloudSyncProvider(storage);
        var separate = new CloudSyncProvider(storage, "personal-v1-");
        var id = Guid.NewGuid();
        original.PutRecord(id, [1]);
        original.PutBlob(id, [2]);
        original.SaveDailyBackup(() => [3]);
        using var oldKey = new SyncEncryption("old-fixture", original.GetSalt());
        original.SaveKeyVerifier(oldKey);
        separate.PutRecord(id, [4]);
        storage.Write("unrelated", [5]);

        original.ResetArea();

        Assert.Null(original.GetRecord(id));
        Assert.Null(original.GetBlob(id));
        Assert.Equal(new byte[] { 4 }, separate.GetRecord(id));
        Assert.Equal(new byte[] { 5 }, storage.Read("unrelated"));
        Assert.DoesNotContain(storage.List(), name => name.StartsWith("backup-", StringComparison.Ordinal));
        using var freshKey = new SyncEncryption("new-fixture", original.GetSalt());
        original.SaveKeyVerifier(freshKey);
        Assert.Throws<CloudSyncKeyMismatchException>(() => original.VerifyKey(oldKey));
    }

    [Fact]
    public void VerifierRejectsWrongKeyWithoutChangingEitherArea()
    {
        using var http = new HttpClient();
        var storage = new MemoryStore(http);
        var original = new CloudSyncProvider(storage);
        var separate = new CloudSyncProvider(storage, "personal-v1-");
        var salt = original.GetSalt();
        using var right = new SyncEncryption("fixture-one", salt);
        using var wrong = new SyncEncryption("fixture-two", salt);
        original.VerifyKey(right);
        original.SaveKeyVerifier(right);
        original.VerifyKey(right);
        Assert.Throws<CloudSyncKeyMismatchException>(() => original.VerifyKey(wrong));
        using var separateKey = new SyncEncryption("fixture-two", separate.GetSalt());
        separate.SaveKeyVerifier(separateKey);
        separate.VerifyKey(separateKey);
        original.VerifyKey(right);
    }

    [Fact]
    public void LegacyCloudRecordsRejectWrongKeyBeforeUpload()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var http = new HttpClient();
            var storage = new MemoryStore(http);
            var provider = new CloudSyncProvider(storage);
            var salt = provider.GetSalt();
            var id = Guid.NewGuid();
            using var right = new SyncEncryption("fixture-one", salt);
            provider.PutRecord(id, right.Seal(id, "note", "legacy ciphertext"));
            using var notes = new NoteStore(Path.Combine(directory.FullName, "notes.db"));
            notes.Create("Yerel not");
            var names = storage.List().ToArray();
            Assert.Throws<CloudSyncKeyMismatchException>(() =>
                notes.SyncNotes(provider, "fixture-two", salt, _ => throw new InvalidOperationException()));
            Assert.Equal(names, storage.List());
            Assert.Single(notes.Search("", 0, null));
        }
        finally { directory.Delete(true); }
    }

    private sealed class MemoryStore(HttpClient http) : CloudObjectStore(http, () => "")
    {
        private readonly Dictionary<string, byte[]> files = new();
        internal override IEnumerable<string> List() => files.Keys;
        internal override byte[]? Read(string name) => files.GetValueOrDefault(name);
        internal override void Write(string name, byte[] value) => files[name] = value;
        internal override void Delete(string name) => files.Remove(name);
    }
}
