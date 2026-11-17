using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;
using Xunit;

namespace KaralamaDefteri.Tests;

public sealed class DriveInteropTests
{
    [Fact]
    public void DriveExchangeSharesNotesAndImagesWithDesktopProvider()
    {
        using var drive = new MemoryDrive();
        using var http = new HttpClient(drive);
        var root = Directory.CreateTempSubdirectory();
        try
        {
            const string password = "G054 test password";
            const string area = "personal-v1-";
            var mobilePath = Path.Combine(root.FullName, "mobile.db");
            using var windows = new NoteStore(Path.Combine(root.FullName, "windows.db"));
            using var mobile = new NoteStore(mobilePath);
            var windowsNote = Add(windows, "Windows canary", [11, 12, 13]);
            var provider = new CloudSyncProvider(new GoogleDriveObjectStore(http, () => "qa"), area);
            var salt = provider.GetSalt();
            Assert.Equal(0, windows.SyncNotes(provider, password, salt, provider.SaveDailyBackup));

            var mobileNote = Add(mobile, "Android canary", [21, 22, 23]);
            var pulled = 0;
            var exchange = DriveSyncExchange.Run(http, "qa", mobilePath, null, area, password,
                localChanges: () => pulled++);
            Assert.Equal(0, exchange.Conflicts);
            Assert.Equal(salt, exchange.Salt);
            Assert.Equal(1, pulled);
            AssertNote(mobile, windowsNote, "Windows canary", [11, 12, 13]);
            Assert.Equal(0, windows.SyncNotes(provider, password, salt, provider.SaveDailyBackup));
            AssertNote(windows, mobileNote, "Android canary", [21, 22, 23]);

            var before = drive.Writes;
            Assert.Throws<CloudSyncKeyMismatchException>(() =>
                DriveSyncExchange.Run(http, "qa", mobilePath, null, area, "wrong password"));
            Assert.Equal(before, drive.Writes);
            Assert.Throws<CloudSyncKeyMismatchException>(() =>
                DriveSyncExchange.Run(http, "qa", mobilePath, null, "missing-", null, exchange.Salt, exchange.Key));
            Assert.Equal(before, drive.Writes);

            windowsNote.Content += "\nWindows edit";
            windows.Save(windowsNote);
            windows.SyncNotes(provider, password, salt, provider.SaveDailyBackup);
            var keyReuse = DriveSyncExchange.Run(http, "qa", mobilePath, null, area, null, exchange.Salt, exchange.Key,
                isNoteEditing: id => id == Guid.Parse(windowsNote.Uuid));
            Assert.DoesNotContain("Windows edit", mobile.Search("Windows canary", 0, null).Single().Preview);
            var afterClose = DriveSyncExchange.Run(http, "qa", mobilePath, null, area, null, keyReuse.Salt, keyReuse.Key);
            Assert.Contains("Windows edit", mobile.Search("Windows canary", 0, null).Single().Preview);
            CryptographicOperations.ZeroMemory(exchange.Key);
            CryptographicOperations.ZeroMemory(keyReuse.Key);
            CryptographicOperations.ZeroMemory(afterClose.Key);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void DriveSharesNestedGroupsInBothDirections()
    {
        using var drive = new MemoryDrive();
        using var http = new HttpClient(drive);
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            const string password = "subgroup-test";
            const string area = "g064-qa-";
            var mobilePath = Path.Combine(directory.FullName, "android.db");
            using var windows = new NoteStore(Path.Combine(directory.FullName, "windows.db"));
            using var android = new NoteStore(mobilePath);
            var root = windows.GetOrCreateGroup("Firma");
            var firstChild = windows.CreateSubgroup("Şube", windows.Groups().Single(group => group.Id == root));
            windows.Create("Windows notu", firstChild);
            var provider = new CloudSyncProvider(new GoogleDriveObjectStore(http, () => "qa"), area);
            var salt = provider.GetSalt();
            windows.SyncNotes(provider, password, salt, provider.SaveDailyBackup);
            var exchange = DriveSyncExchange.Run(http, "qa", mobilePath, null, area, password);
            CryptographicOperations.ZeroMemory(exchange.Key);

            var branch = android.Groups().Single(group => group.Name == "Şube");
            Assert.Equal(android.Groups().Single(group => group.Name == "Firma").Uuid, branch.ParentUuid);
            Assert.Equal(branch.Id, android.Get(android.Search("Windows", 0, null).Single().Id)!.GroupId);
            var current = branch;
            for (var depth = 2; depth <= 12; depth++)
            {
                var id = android.CreateSubgroup($"Alt grup {depth}", current);
                current = android.Groups().Single(group => group.Id == id);
            }
            android.Create("Android notu", current.Id);
            exchange = DriveSyncExchange.Run(http, "qa", mobilePath, null, area, password);
            CryptographicOperations.ZeroMemory(exchange.Key);
            windows.SyncNotes(provider, password, salt, provider.SaveDailyBackup);

            var tree = NoteStore.GroupTree(windows.Groups()).ToArray();
            Assert.Equal(Enumerable.Range(0, 13), tree.Select(item => item.Depth));
            Assert.Equal(current.Uuid, tree[^1].Group.Uuid);
            Assert.Equal(tree[^1].Group.Id, windows.Get(windows.Search("Android", 0, null).Single().Id)!.GroupId);
            var expanded = new HashSet<string>();
            Assert.Single(NoteStore.VisibleGroupTree(windows.Groups(), expanded));
            expanded.Add(tree[0].Group.Uuid);
            expanded.Add(tree[1].Group.Uuid);
            Assert.Equal(new[] { 0, 1, 2 }, NoteStore.VisibleGroupTree(windows.Groups(), expanded).Select(item => item.Depth));
            expanded.Remove(tree[0].Group.Uuid);
            Assert.Single(NoteStore.VisibleGroupTree(windows.Groups(), expanded));
        }
        finally { directory.Delete(true); }
    }

    private static Note Add(NoteStore store, string text, byte[] bytes)
    {
        var note = store.Create(text);
        var id = store.InsertPicture(note.Id, new("image/png", bytes, [1], 1, 1, "canary.png"));
        note.Content += "\n" + ImageMarkers.Marker(id);
        store.Save(note);
        return note;
    }

    private static void AssertNote(NoteStore store, Note original, string text, byte[] expected)
    {
        var note = store.Get(Assert.Single(store.Search(text, 0, null)).Id)!;
        Assert.Equal(original.Uuid, note.Uuid);
        Assert.StartsWith(text, note.Content);
        Assert.Equal(expected, store.GetFirstPicture(note.Id)!.Bytes);
    }

    private sealed class MemoryDrive : HttpMessageHandler
    {
        private readonly Dictionary<string, (string Name, byte[] Data)> records = [];
        internal int Writes { get; private set; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer qa", request.Headers.Authorization?.ToString());
            var uri = request.RequestUri!;
            Assert.Equal("www.googleapis.com", uri.Host);
            var query = HttpUtility.ParseQueryString(uri.Query);
            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/files"))
            {
                Assert.Equal("appDataFolder", query["spaces"]);
                var name = query["q"];
                return Json(new { files = records.Where(item => name is null || name == $"name = '{item.Value.Name}'")
                    .Select(item => new { id = item.Key, name = item.Value.Name }) });
            }
            if (request.Method == HttpMethod.Post)
            {
                var parts = Assert.IsType<MultipartContent>(request.Content).ToArray();
                using var meta = JsonDocument.Parse(parts[0].ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
                Assert.Equal("appDataFolder", meta.RootElement.GetProperty("parents")[0].GetString());
                var id = Guid.NewGuid().ToString("N");
                records.Add(id, (meta.RootElement.GetProperty("name").GetString()!,
                    parts[1].ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult()));
                Writes++;
                return Json(new { id });
            }
            var key = uri.Segments[^1];
            if (!records.TryGetValue(key, out var record)) return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(record.Data) };
            if (request.Method == HttpMethod.Patch)
            {
                records[key] = (record.Name, request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult());
                Writes++;
                return Json(new { id = key });
            }
            throw new NotSupportedException(request.Method.Method);
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
