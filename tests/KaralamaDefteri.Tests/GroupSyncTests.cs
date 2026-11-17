using Xunit;

namespace KaralamaDefteri.Tests;

public sealed class GroupSyncTests
{
    [Fact]
    public void LegacyGroupAliasesPreserveNotesThroughRenameDeleteAndUndo()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var first = new NoteStore(Path.Combine(root, "first.db"));
            using var second = new NoteStore(Path.Combine(root, "second.db"));
            var folder = Path.Combine(root, "sync");
            const string password = "group-sync-test";
            var firstGroup = first.GetOrCreateGroup("Deneme");
            var secondGroup = second.GetOrCreateGroup("Deneme");
            var note = first.Create("korunan not", firstGroup);
            Assert.NotEqual(first.Groups().Single().Uuid, second.Groups().Single().Uuid);

            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            Assert.Equal(secondGroup, second.Search("korunan", 0, null).Single().Group == "Deneme"
                ? second.Get(second.Search("korunan", 0, null).Single().Id)?.GroupId : null);
            var group = first.Groups().Single();
            first.UpdateGroup(group with { Name = "Yeni ad", Color = "#FFF475", Archived = true, Order = 2 });
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            var renamed = Assert.Single(second.Groups());
            Assert.Equal(secondGroup, renamed.Id);
            Assert.Equal("Yeni ad", renamed.Name);
            Assert.Equal("#FFF475", renamed.Color);
            Assert.True(renamed.Archived);
            Assert.Equal(2, renamed.Order);

            group = first.Groups().Single();
            var ids = first.DeleteGroup(firstGroup);
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            Assert.Empty(second.Groups());
            Assert.Null(second.Get(second.Search("korunan", 0, null).Single().Id)?.GroupId);

            first.RestoreGroup(group, ids);
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            var restored = Assert.Single(second.Groups());
            Assert.Equal("Yeni ad", restored.Name);
            Assert.Equal(restored.Id, second.Get(second.Search("korunan", 0, null).Single().Id)?.GroupId);
            Assert.Equal(note.Uuid, second.Get(second.Search("korunan", 0, null).Single().Id)?.Uuid);
        }
        finally { Directory.Delete(root, true); }
    }
}
