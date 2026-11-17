using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using KaralamaDefteri;
using Xunit;
using Xunit.Abstractions;

public sealed class CoreTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("{\"a\":1}", "json")]
    [InlineData("<note>text</note>", "xml")]
    [InlineData("$value = Get-Date", "powershell")]
    [InlineData("SELECT * FROM notes", "sql")]
    [InlineData("[section]\nkey=value", "ini")]
    [InlineData("2026-11-13 11:20:32 INFO event", "log")]
    [InlineData("{invalid", "text")]
    public void Syntax(string text, string expected) => Assert.Equal(expected, Editing.Detect(text));

    [Fact]
    public void FormatAndFind()
    {
        var json = Editing.Format("{\"a\":1}", "json");
        Assert.Contains("\n", json);
        Assert.Equal(1, JsonDocument.Parse(json).RootElement.GetProperty("a").GetInt32());
        var xml = Editing.Format("<note><line>text</line></note>", "xml");
        Assert.Contains("\n", xml);
        Assert.Equal("text", XElement.Parse(xml).Element("line")!.Value);
        Assert.Single(Editing.Pattern("abc", false, true, false).Matches("ABC abcd"));
        Assert.Equal("a2", Editing.Pattern(@"(\d+)([a-z])", true, false, true).Replace("2a", "$2$1"));
    }

    [Fact]
    public void TitlesAndBlankCleanup()
    {
        WithStore(store =>
        {
            var blank = store.Create("");
            Assert.Matches(@"^Not – \d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$", blank.Title);
            Assert.True(store.RemoveBlank(blank));
            Assert.Null(store.Get(blank.Id));
            var note = store.Create("\n \n  İlk satır  \nİkinci");
            Assert.Equal("İlk satır", note.Title);
            Assert.Equal(80, NoteStore.AutoTitle(new string('a', 100), note.Created).Length);
            note.Title = "Elle verilen başlık";
            note.Manual = true;
            note.Content = "";
            store.Save(note);
            Assert.False(store.RemoveBlank(note));
            Assert.Equal("Elle verilen başlık", store.Get(note.Id)!.Title);
        });
    }

    [Fact]
    public void TurkishFtsAndNoteLifecycle()
    {
        WithStore(store =>
        {
            var note = store.Create("IP IŞIK İstanbul I\u0307zmir");
            store.Create("ip işik ıstanbul");
            for (var index = 0; index < 998; index++) store.Create($"Log kaydı {index}");
            var timer = Stopwatch.StartNew();
            var hit = Assert.Single(store.Search("ıp ış ist iz", 0, null));
            timer.Stop();
            output.WriteLine($"1.000 notta FTS sorgusu: {timer.Elapsed.TotalMilliseconds:F1} ms");
            Assert.Equal(note.Id, hit.Id);
            Assert.Contains("\u0001", hit.Match);
            Assert.Equal(note.Id, Assert.Single(store.Search("IP", 0, null)).Id);
            Assert.Equal(note.Id, Assert.Single(store.Search("ışık", 0, null)).Id);
            Assert.Equal(note.Id, Assert.Single(store.Search("İstanbul", 0, null)).Id);
            note.Content = "Yeni içerik";
            note.Title = note.Content;
            note.Archived = true;
            store.Save(note);
            Assert.Empty(store.Search("ışık", 3, null));
            Assert.Equal(note.Id, Assert.Single(store.Search("yeni", 1, null)).Id);
            note.Deleted = NoteStore.Now();
            store.Save(note);
            Assert.Empty(store.Search("yeni", 1, null));
            Assert.Equal(note.Id, Assert.Single(store.Search("yeni", 2, null)).Id);
            note.Deleted = null;
            store.Save(note);
            Assert.Equal(note.Id, Assert.Single(store.Search("yeni", 1, null)).Id);
        });
    }

    [Fact]
    public void LargeLogPersists()
    {
        WithStore(store =>
        {
            const string line = "2026-11-13 11:20:32 INFO event 12345\n";
            var builder = new StringBuilder();
            while (builder.Length < 5 * 1024 * 1024) builder.Append(line);
            var text = builder.ToString();
            Assert.Equal("log", Editing.Detect(text));
            var note = store.Create("");
            note.Content = text;
            note.Title = NoteStore.AutoTitle(text, note.Created);
            var timer = Stopwatch.StartNew();
            store.Save(note);
            output.WriteLine($"5 MB SQLite yazımı: {timer.Elapsed.TotalMilliseconds:F1} ms");
            Assert.Equal(text, store.Get(note.Id)!.Content);
        });
    }

    private static void WithStore(Action<NoteStore> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"KaralamaDefteri-test-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new NoteStore(path);
            action(store);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }
}
