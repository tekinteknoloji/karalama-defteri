using Xunit;

namespace KaralamaDefteri.Tests;

public sealed class TrashSyncTests
{
    [Fact]
    public void EmptyTrashDeletesOnlyTrashedNotesAndAttachmentsAcrossReplicas()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var first = new NoteStore(Path.Combine(root, "first.db"));
            using var second = new NoteStore(Path.Combine(root, "second.db"));
            var folder = Path.Combine(root, "sync");
            const string password = "trash-test";
            var active = first.Create("kalacak");
            var trashed = first.Create("silinecek");
            var picture = first.InsertPicture(trashed.Id, new Picture("image/png", [1], [2], 1, 1, "deneme.png"));
            trashed.Content += "\n" + ImageMarkers.Marker(picture);
            trashed.Deleted = NoteStore.Now();
            first.Save(trashed);
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            Assert.Single(second.Search("", 2, null));
            Assert.Equal(1, first.EmptyTrash());
            Assert.Null(first.GetPicture(trashed.Id, picture));
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            first.SyncFolder(folder, password);
            Assert.Empty(first.Search("", 2, null));
            Assert.Empty(second.Search("", 2, null));
            Assert.Equal(active.Uuid, second.Get(second.Search("", 0, null).Single().Id)?.Uuid);
        }
        finally { Directory.Delete(root, true); }
    }
}
