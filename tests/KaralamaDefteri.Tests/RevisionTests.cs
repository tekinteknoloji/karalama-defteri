using KaralamaDefteri;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class RevisionTests
{
    [Fact]
    public void PictureUpdateFailureKeepsEditorDirtyUntilRetryCommits()
    {
        InFolder(path =>
        {
            using var store = new NoteStore(path);
            var root = store.GetOrCreateGroup("Ana grup");
            var child = store.CreateSubgroup("Alt grup", store.Groups().Single(group => group.Id == root));
            var note = store.Create("Önceki metin", child);
            store.InsertPicture(note.Id, new("image/png", [1], [1], 1, 1, "test.png"));
            using var db = new SqliteConnection($"Data Source={path};Pooling=False");
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TRIGGER test_picture_failure BEFORE UPDATE ON attachments BEGIN SELECT RAISE(ABORT, 'test'); END";
            command.ExecuteNonQuery();
            note.Content = "Yeni metin";
            note.Dirty = true;
            Assert.Throws<SqliteException>(() => store.SaveWithPictures(note));
            Assert.True(note.Dirty);
            Assert.Equal("Önceki metin", store.Get(note.Id)!.Content);
            command.CommandText = "DROP TRIGGER test_picture_failure";
            command.ExecuteNonQuery();
            store.SaveWithPictures(note);
            Assert.False(note.Dirty);
            Assert.Equal("Yeni metin", store.Get(note.Id)!.Content);
            Assert.Equal(note.Id, Assert.Single(store.Search("", 0, null, group: child)).Id);
        });
    }

    [Fact]
    public void OneCharacterPasswordReplacesOldKeyAndCanRestoreOldPasswordBackup()
    {
        var directory = Directory.CreateTempSubdirectory();
        const string oldPassword = "long-key-for-test";
        const string newPassword = "1";
        try
        {
            var path = Path.Combine(directory.FullName, "notes.db");
            var backups = Path.Combine(directory.FullName, "backups");
            string backup;
            long id;
            using (var original = new NoteStore(path, oldPassword))
            {
                id = original.Create("Korunan not").Id;
                original.InsertPicture(id, new("image/png", [1, 2, 3], [1], 1, 1, "resim.png"));
                original.Set("cloudSessionGoogle", "synthetic-session");
                backup = original.Backup(backups);
            }
            NoteStore.ReplaceDatabaseFormat(path, oldPassword, newPassword);
            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => NoteStore.VerifyPassword(path, oldPassword));
            using var changed = new NoteStore(path, newPassword);
            Assert.Equal("synthetic-session", changed.Setting("cloudSessionGoogle"));
            Assert.Equal(new byte[] { 1, 2, 3 }, changed.GetFirstPicture(id)!.Bytes);
            changed.Create("Sonradan eklenen not");
            changed.RestoreBackup(backup, backups, oldPassword);
            Assert.Empty(changed.Search("Sonradan", 0, null));
            Assert.NotNull(changed.Get(id));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void SyncUploadAllowsAnotherConnectionToSave()
    {
        InFolder(path =>
        {
            const string password = "concurrent-save-fixture";
            using var writer = new NoteStore(path, password);
            var note = writer.Create("Önce");
            using var worker = new NoteStore(path, password, syncConnection: true);
            var provider = new SaveDuringUpload(() =>
            {
                var current = writer.Get(note.Id)!;
                current.Content = "Eşitleme sırasında yazıldı";
                writer.Save(current);
            });
            worker.SyncNotes(provider, password, new byte[32], _ => {});
            Assert.Equal("Eşitleme sırasında yazıldı", writer.Get(note.Id)!.Content);
            Assert.Single(provider.ListRecords());
        });
    }

    private sealed class SaveDuringUpload(Action save) : ISyncProvider
    {
        private readonly Dictionary<Guid, byte[]> records = [];
        public IEnumerable<Guid> ListRecords() => records.Keys;
        public byte[]? GetRecord(Guid id) => records.GetValueOrDefault(id);
        public void PutRecord(Guid id, byte[] encrypted)
        {
            save();
            records[id] = encrypted;
        }
        public byte[]? GetBlob(Guid id) => null;
        public void PutBlob(Guid id, byte[] encrypted) => throw new InvalidOperationException();
        public void Delete(Guid id) => records.Remove(id);
    }

    [Fact]
    public void SyncResumesWithoutUploadingCompletedNotesAgain()
    {
        InFolder(path =>
        {
            using var first = new NoteStore(path);
            for (var i = 0; i < 62; i++) first.Create($"Deneme {i}");
            var attempts = 0;
            var provider = new SaveDuringUpload(() =>
            {
                if (++attempts == 56) throw new IOException("Simulated interruption");
            });
            using var key = new SyncEncryption(new byte[32]);
            var salt = new byte[32];
            Assert.Throws<IOException>(() => first.SyncNotes(provider, key, salt, _ => {}));
            Assert.Equal(55, provider.ListRecords().Count());
            first.SyncNotes(provider, key, salt, _ => {});
            Assert.Equal(62, provider.ListRecords().Count());
            Assert.Equal(63, attempts);
        });
    }

    [Fact]
    public void MigratesOriginalSchemaAndPreservesSearch()
    {
        InFolder(path =>
        {
            long id;
            using (var store = new NoteStore(path))
            {
                id = store.Create("İstanbul VPN").Id;
                store.Set("theme", "Koyu");
            }
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();
                db.CreateFunction<string, string>("search_fold", NoteStore.Fold, isDeterministic: true);
                using var command = db.CreateCommand();
                foreach (var table in new[] { "notes", "groups", "attachments", "tags" })
                {
                    command.CommandText = $"""
                        DROP TRIGGER {table}_metadata_insert;
                        DROP TRIGGER {table}_metadata_update;
                        DROP TRIGGER {table}_uuid_immutable;
                        DROP TRIGGER {table}_tombstone;
                        DROP INDEX {table}_uuid;
                        ALTER TABLE {table} DROP COLUMN uuid;
                        ALTER TABLE {table} DROP COLUMN device_id;
                        """;
                    command.ExecuteNonQuery();
                    if (table == "notes") continue;
                    command.CommandText = $"ALTER TABLE {table} DROP COLUMN updated_at";
                    command.ExecuteNonQuery();
                }
                command.CommandText = """
                    DROP TABLE deleted_records;
                    DROP TABLE board_columns;
                    ALTER TABLE notes DROP COLUMN column_uuid;
                    ALTER TABLE notes DROP COLUMN board_order;
                    ALTER TABLE notes DROP COLUMN board_updated_at;
                    DROP INDEX notes_deadline;
                    ALTER TABLE notes DROP COLUMN deadline_utc;
                    ALTER TABLE notes DROP COLUMN deadline_reminder;
                    ALTER TABLE notes DROP COLUMN deadline_updated_at;
                    ALTER TABLE notes DROP COLUMN deadline_notified_for;
                    ALTER TABLE groups DROP COLUMN parent_uuid;
                    PRAGMA user_version=1;
                    """;
                command.ExecuteNonQuery();
            }
            using (var migrated = new NoteStore(path))
            {
                Assert.Equal(id, Assert.Single(migrated.Search("istanbul", 0, null)).Id);
                Assert.Equal("Koyu", migrated.Setting("theme"));
                Assert.Null(migrated.Get(id)!.GroupId);
                Assert.Equal('7', migrated.Get(id)!.Uuid[14]);
                Assert.True(migrated.GetOrCreateGroup("Güven") > 0);
            }
        });
    }

    [Fact]
    public void MetadataAndTombstonesSurviveDeletesAndUndo()
    {
        InFolder(path =>
        {
            string noteUuid, groupUuid, attachmentUuid;
            using (var store = new NoteStore(path))
            {
                var groupId = store.GetOrCreateGroup("Grup");
                var group = Assert.Single(store.Groups());
                groupUuid = group.Uuid;
                var note = store.Create("örnek", groupId);
                noteUuid = note.Uuid;
                var attachment = store.InsertPicture(note.Id, new Picture("image/png", [1], [1], 1, 1, null));
                attachmentUuid = store.GetPicture(note.Id, attachment)!.Uuid;
                Assert.All(new[] { groupUuid, noteUuid, attachmentUuid }, value =>
                {
                    Assert.True(Guid.TryParse(value, out _));
                    Assert.Equal('7', value[14]);
                });
                Assert.Matches(@"\.\d{3}Z$", note.Updated);
                Assert.NotEmpty(note.DeviceId);
                var moved = store.DeleteGroup(groupId);
                store.RestoreGroup(group, moved);
                Assert.Equal(groupUuid, Assert.Single(store.Groups()).Uuid);
                Assert.Equal(noteUuid, store.Get(note.Id)!.Uuid);
                note = store.Get(note.Id)!;
                note.Deleted = DateTimeOffset.UtcNow.AddDays(-31).ToString("O");
                store.Save(note);
            }
            using (var reopened = new NoteStore(path)) Assert.Empty(reopened.Search("", 3, null));
            using var db = new SqliteConnection($"Data Source={path};Pooling=False");
            db.Open();
            using var command = db.CreateCommand();
                command.CommandText = """
                    SELECT uuid,kind,deleted_at,device_id FROM deleted_records ORDER BY kind;
                    """;
            using var rows = command.ExecuteReader();
            Assert.True(rows.Read());
            Assert.Equal(attachmentUuid, rows.GetString(0));
            Assert.Equal("attachment", rows.GetString(1));
            Assert.Matches(@"\.\d{3}Z$", rows.GetString(2));
            Assert.NotEmpty(rows.GetString(3));
            Assert.True(rows.Read());
            Assert.Equal(noteUuid, rows.GetString(0));
            Assert.Equal("note", rows.GetString(1));
            Assert.False(rows.Read());
        });
    }

    [Fact]
    public void GroupsUseTurkishNamesAndDeleteUndoKeepsNotes()
    {
        InFolder(path =>
        {
            using var store = new NoteStore(path);
            var id = store.GetOrCreateGroup("Güven Otomotiv");
            Assert.Equal(id, store.GetOrCreateGroup("GÜVEN OTOMOTİV"));
            var note = store.Create("VPN adresi", id);
            store.Create("Başka VPN");
            Assert.Equal(note.Id, Assert.Single(store.Search("@güven vpn", 0, null)).Id);
            Assert.Equal(note.Id, Assert.Single(store.Search("@\"Güven Otomotiv\" vpn", 0, null)).Id);
            var group = Assert.Single(store.Groups());
            var moved = store.DeleteGroup(id);
            Assert.Null(store.Get(note.Id)!.GroupId);
            Assert.Equal(2, store.Search("", 0, null, group: 0).Count);
            store.RestoreGroup(group, moved);
            Assert.Equal(id, store.Get(note.Id)!.GroupId);
        });
    }

    [Fact]
    public void CheckpointedPortableCopyRestoresPicturesGroupsAndSettings()
    {
        InFolder(path =>
        {
            var bytes = new byte[] { 1, 2, 3, 4 };
            long picture;
            long id;
            using (var store = new NoteStore(path))
            {
                var group = store.GetOrCreateGroup("Firma");
                var note = store.Create("", group);
                id = note.Id;
                picture = store.InsertPicture(id, new Picture("image/png", bytes, bytes, 1, 1, "örnek.png"));
                note.Content = $"[[resim:{picture}]]\nVPN notu";
                note.Title = NoteStore.AutoTitle(note.Content, note.Created);
                store.Save(note);
                Assert.Equal("VPN notu", note.Title);
                Assert.Equal(bytes, Assert.Single(store.Search("", 0, null)).Thumbnail);
                var content = note.Content;
                note.Content = "";
                store.Atomic(() => { store.Save(note); store.SyncPictures(note); });
                Assert.NotNull(store.GetPicture(id, picture));
                Assert.Equal(0, Assert.Single(store.Search("", 0, null)).Images);
                note.Content = content;
                store.Atomic(() => { store.Save(note); store.SyncPictures(note); });
                Assert.False(store.RemoveBlank(note));
                store.Set("theme", "Koyu");
                store.Set("group", group.ToString());
                store.Set("session", $"{{\"Tabs\":[{id}],\"Selected\":{id},\"Carets\":{{\"{id}\":0}}}}");
                store.Checkpoint();
            }
            Assert.False(File.Exists(path + "-wal"));
            var destination = Path.Combine(Path.GetDirectoryName(path)!, "Notlarım ve Resimler");
            Directory.CreateDirectory(destination);
            var copy = Path.Combine(destination, "notes.db");
            File.Copy(path, copy);
            using var reopened = new NoteStore(copy);
            Assert.Equal(bytes, reopened.GetPicture(id, picture)!.Bytes);
            Assert.Equal("Firma", Assert.Single(reopened.Groups()).Name);
            Assert.Equal("Koyu", reopened.Setting("theme"));
            Assert.Contains($"\"Tabs\":[{id}]", reopened.Setting("session"));
            var expired = reopened.Get(id)!;
            expired.Deleted = DateTimeOffset.UtcNow.AddDays(-31).ToString("O");
            reopened.Save(expired);
            reopened.Dispose();
            using var purged = new NoteStore(copy);
            Assert.Null(purged.Get(id));
            Assert.Null(purged.GetPicture(id, picture));
        });
    }

    [Fact]
    public void PathsAreRelativeToRunningExecutable()
    {
        Assert.Equal(Path.GetDirectoryName(Environment.ProcessPath), AppPaths.Root);
        Assert.Equal(Path.Combine(AppPaths.Root, "Veri", "notes.db"), AppPaths.Database);
    }

    [Fact]
    public void PictureProcessingKeepsSmallPngAndCompressesLargeImage()
    {
        var source = System.Windows.Media.Imaging.BitmapSource.Create(3900, 24, 96, 96,
            System.Windows.Media.PixelFormats.Bgr24, null, new byte[3900 * 24 * 3], 3900 * 3);
        var compressed = Pictures.FromClipboard(source, 1);
        Assert.Equal("image/jpeg", compressed.Mime);
        Assert.Equal(3840, compressed.Width);
        Assert.Equal(320, Pictures.Decode(compressed.Thumbnail).PixelWidth);
        var small = System.Windows.Media.Imaging.BitmapSource.Create(24, 24, 96, 96,
            System.Windows.Media.PixelFormats.Bgr24, null, new byte[24 * 24 * 3], 24 * 3);
        var png = Pictures.FromClipboard(small, 3 * 1024 * 1024);
        Assert.Equal("image/png", png.Mime);
        Assert.Equal(24, Pictures.Decode(png.Bytes).PixelWidth);
    }

    [Fact]
    public void BackupRestoresNotesAndKeepsSafetySnapshot()
    {
        InFolder(path =>
        {
            var directory = Path.Combine(Path.GetDirectoryName(path)!, "Yedekler");
            using var store = new NoteStore(path);
            var group = store.GetOrCreateGroup("Yedek grubu");
            var original = store.Create("Özgün not", group);
            var backup = store.Backup(directory);
            var later = store.Create("Sonraki not");
            Assert.Throws<InvalidDataException>(() => store.RestoreBackup(path, directory));
            store.RestoreBackup(backup, directory);
            Assert.Equal(original.Uuid, Assert.Single(store.GroupNotes(group)).Uuid);
            Assert.Null(store.Get(later.Id));
            using var safety = new NoteStore(Assert.Single(Directory.GetFiles(directory, "geri_yukleme_oncesi_*.db")));
            Assert.Equal(later.Uuid, safety.Get(later.Id)!.Uuid);
        });
    }

    [Fact]
    public void PictureRevisionKeepsFirstOriginalAndDesktopPinSurvivesBackup()
    {
        InFolder(path =>
        {
            using var store = new NoteStore(path);
            var note = store.Create("Resimli not");
            var first = new Picture("image/png", [1], [2], 2, 2, null);
            var id = store.InsertPicture(note.Id, first);
            note.Content += $"\n[[resim:{id}]]";
            store.Save(note);
            store.SyncPictures(note);
            store.ReplacePicture(note.Id, id, first with { Bytes = [3] });
            store.ReplacePicture(note.Id, id, first with { Bytes = [4] });
            Assert.Equal(new byte[] { 4 }, store.GetPicture(note.Id, id)!.Bytes);
            store.RestoreOriginal(note.Id, id);
            Assert.Equal(first.Bytes, store.GetPicture(note.Id, id)!.Bytes);
            Assert.False(store.HasOriginal(note.Id, id));
            store.SetDesktopPin(note.Id, "{}");
            Assert.Equal(note.Id, Assert.Single(store.DesktopPins()));
            var backup = store.Backup(Path.Combine(Path.GetDirectoryName(path)!, "Yedekler"));
            using var reopened = new NoteStore(backup);
            Assert.Equal("{}", reopened.DesktopPin(note.Id));
        });
    }

    [Fact]
    public void TagSearchCombinesGroupColorArchiveAndImageFilters()
    {
        InFolder(path =>
        {
            using var store = new NoteStore(path);
            var group = store.GetOrCreateGroup("Güven");
            var note = store.Create("VPN #YAZICI #ağ", group);
            note.Color = "#FFF475";
            note.Archived = true;
            store.Save(note);
            store.Create("VPN #yazıcı #ağ");
            Assert.Equal(note.Id, Assert.Single(store.Search("@güven #yazıcı #ağ renk:sarı arşiv: resim:hayır VPN", 0, null)).Id);
            Assert.Empty(store.Search("@güven #yazıcı renk:sarı arşiv: resim: VPN", 0, null));
            note.Content = "VPN #ağ";
            store.Save(note);
            Assert.Empty(store.Search("@güven #yazıcı arşiv:", 0, null));
            Assert.Equal("ağ", Assert.Single(store.Tags(note.Id)));
        });
    }

    [Fact]
    public void ChecklistProgressIsPersistedOnCard()
    {
        InFolder(path =>
        {
            using var store = new NoteStore(path);
            var note = store.Create("Bir\nİki");
            note.Checklist = true;
            note.Content = ChecklistText.Convert(note.Content, true).Replace("[ ] Bir", "[x] Bir");
            store.Save(note);
            Assert.True(store.Get(note.Id)!.Checklist);
            Assert.Equal("1/2", Assert.Single(store.Search("", 0, null)).Progress);
            Assert.Equal("Bir\nİki", ChecklistText.Convert(note.Content, false));
        });
    }

    [Fact]
    public void SqlCipherExportProtectsDataAndKeepsSearchAcrossRoundTrip()
    {
        InFolder(path =>
        {
            const string canary = "SifrelemeDenemeMetni753184";
            long id;
            using (var store = new NoteStore(path)) id = store.Create(canary).Id;
            var encrypted = path + ".encrypted";
            NoteStore.ExportDatabase(path, null, encrypted, "yalnizca-birim-testi-parolasi");
            Assert.DoesNotContain(canary, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(encrypted)));
            var wrongPasswordCopy = path + ".wrong-password-check";
            File.Copy(encrypted, wrongPasswordCopy);
            Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => NoteStore.VerifyPassword(wrongPasswordCopy, "yanlis"));
            using (var store = new NoteStore(encrypted, "yalnizca-birim-testi-parolasi"))
            {
                Assert.True(store.MatchesOpenPassword("yalnizca-birim-testi-parolasi"));
                Assert.False(store.MatchesOpenPassword("yalnizca-birim-testi-parolasi."));
                Assert.Equal(id, Assert.Single(store.Search(canary, 0, null)).Id);
                var backup = store.Backup(Path.Combine(Path.GetDirectoryName(path)!, "Yedekler"));
                Assert.DoesNotContain(canary, System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(backup)));
                using (var copy = new NoteStore(backup, "yalnizca-birim-testi-parolasi"))
                    Assert.Equal(canary, copy.Get(id)!.Content);
                var later = store.Create("SifreliSonrakiNot0531");
                store.RestoreBackup(backup, Path.GetDirectoryName(backup)!);
                Assert.Null(store.Get(later.Id));
                Assert.Equal(canary, store.Get(id)!.Content);
                Assert.Equal(id, Assert.Single(store.Search(canary, 0, null)).Id);
            }
            var plaintext = path + ".restored";
            NoteStore.ExportDatabase(encrypted, "yalnizca-birim-testi-parolasi", plaintext, null);
            using var restored = new NoteStore(plaintext);
            Assert.Equal(canary, restored.Get(id)!.Content);
            NoteStore.ReplaceDatabaseFormat(path, null, "yalnizca-birim-testi-parolasi");
            Assert.False(NoteStore.IsPlainDatabase(path));
            NoteStore.ReplaceDatabaseFormat(path, "yalnizca-birim-testi-parolasi", null);
            Assert.True(NoteStore.IsPlainDatabase(path));
            using var reopened = new NoteStore(path);
            Assert.Equal(canary, reopened.Get(id)!.Content);
        });
    }

    [Fact]
    public void RecoveryPhraseAndPendingConversionRejectWrongCredentials()
    {
        const string password = "only-test-credentials";
        var phrase = RecoveryPhrase.Generate();
        Assert.Equal(24, phrase.Split(' ').Length);
        var recovery = RecoveryPhrase.Protect(password, phrase);
        Assert.Equal(password, RecoveryPhrase.Unprotect(recovery, phrase));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            RecoveryPhrase.Unprotect(recovery, RecoveryPhrase.Generate()));
        var marker = RecoveryPhrase.TransitionMarker(true, password);
        Assert.True(RecoveryPhrase.VerifyTransition(marker, password));
        Assert.False(RecoveryPhrase.VerifyTransition(marker, "wrong"));
    }

    [Fact]
    public void InterruptedBackupConversionCanResumeInBothDirections()
    {
        InFolder(path =>
        {
            const string password = "only-test-credentials";
            var folder = Path.Combine(Path.GetDirectoryName(path)!, "backups");
            string first, second;
            using (var store = new NoteStore(path))
            {
                store.Create("Kesinti denemesi");
                first = store.Backup(folder);
                second = store.Backup(folder);
            }
            NoteStore.ReplaceDatabaseFormat(first, null, password);
            File.WriteAllText(second + ".convert", "Yarım kalan dönüşüm dosyası");
            NoteStore.ConvertBackups(folder, null, password);
            Assert.False(File.Exists(second + ".convert"));
            Assert.All(new[] { first, second }, item => Assert.False(NoteStore.IsPlainDatabase(item)));
            NoteStore.ReplaceDatabaseFormat(second, password, null);
            NoteStore.ConvertBackups(folder, password, null);
            foreach (var item in new[] { first, second })
            {
                using var store = new NoteStore(item);
                Assert.Single(store.Search("Kesinti", 0, null));
            }
        });
    }

    [Fact]
    public void DeadlineFiltersRemindersAndSyncPreserveDatesAndOptIn()
    {
        InFolder(path =>
        {
            using var first = new NoteStore(path);
            using var second = new NoteStore(path + ".second");
            var card = first.Create("Bugünkü teslim");
            var later = first.Create("Haftalık teslim");
            var localDay = DateTime.Today;
            long At(int days) => new DateTimeOffset(localDay.AddDays(days).AddHours(12)).ToUnixTimeSeconds();
            first.SaveDeadline(card.Id, At(0), true);
            first.SaveDeadline(later.Id, At(3), false);
            Assert.Equal(card.Id, Assert.Single(first.Search("tarih:bugün", 0, null, deadlineFiltersEnabled: true)).Id);
            Assert.Equal(2, first.Search("tarih:hafta teslim", 0, null, deadlineFiltersEnabled: true).Count);
            Assert.Empty(first.PendingDeadlineReminders(At(0) - 1));
            Assert.Equal(card.Id, Assert.Single(first.PendingDeadlineReminders(At(0))).Id);
            first.MarkDeadlineNotified(card.Id);
            Assert.Empty(first.PendingDeadlineReminders(At(4)));
            Assert.Equal("DeadlineSoon", NoteDeadline.BorderRole(At(0), At(0) - 86400));
            Assert.Equal("DeadlineOverdue", NoteDeadline.BorderRole(At(0), At(0)));
            Assert.Null(NoteDeadline.BorderRole(At(0), At(0) - 86401));
            var folder = Path.Combine(Path.GetDirectoryName(path)!, "deadlines");
            first.SyncFolder(folder, "deadline-fixture");
            second.SyncFolder(folder, "deadline-fixture");
            var copy = Assert.Single(second.Search("tarih:bugün", 0, null, deadlineFiltersEnabled: true));
            Assert.Equal(At(0), second.ReadDeadline(copy.Id)!.Due);
            Assert.True(second.ReadDeadline(copy.Id)!.Reminder);
            second.SaveDeadline(copy.Id, null, false);
            second.SyncFolder(folder, "deadline-fixture");
            first.SyncFolder(folder, "deadline-fixture");
            Assert.Null(first.ReadDeadline(card.Id)!.Due);
        });
    }

    [Fact]
    public void BoardColumnsAndCardOrderingSyncAlongsideConcurrentNoteEdits()
    {
        InFolder(path =>
        {
            const string password = "board-tests";
            var folder = Path.Combine(Path.GetDirectoryName(path)!, "shared");
            using var first = new NoteStore(path);
            using var second = new NoteStore(path + ".second");
            var group = first.GetOrCreateGroup("Proje");
            var board = first.BoardGroup(group);
            var columns = first.BoardColumns(board);
            Assert.Equal(new[] { "Fikir", "Yapılacak", "Yapılıyor", "Bitti" }, columns.Select(item => item.Name));
            first.SaveBoardColumn(board, columns[1] with { Order = 0, Name = "Sırada" });
            first.SaveBoardColumn(board, columns[0] with { Order = 1 });
            var custom = new BoardColumn(Guid.NewGuid().ToString(), "İnceleme", 4, "", "", false, false);
            first.SaveBoardColumn(board, custom);
            var a = first.Create("Birinci", group);
            var b = first.Create("İkinci", group);
            first.OrderBoardCards(columns[1].Id, [b.Id, a.Id]);
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            var group2 = second.Groups().Single(item => item.Name == "Proje");
            Assert.Equal("Sırada", second.BoardColumns(group2.Uuid)[0].Name);
            Assert.Contains(second.BoardColumns(group2.Uuid), item => item.Id == custom.Id && item.Name == custom.Name);
            Assert.Equal("Fikir", second.BoardColumns(null)[0].Name);
            var copies = second.Search("", 0, null, group: group2.Id).ToDictionary(card => card.Title);
            Assert.Equal(0, second.BoardPositions()[copies["İkinci"].Id].Order);
            Assert.Equal(1, second.BoardPositions()[copies["Birinci"].Id].Order);
            first.OrderBoardCards(columns[2].Id, [a.Id]);
            var edited = second.Get(copies["Birinci"].Id)!;
            edited.Content = "Birinci güncellendi";
            second.Save(edited);
            second.SyncFolder(folder, password);
            Assert.Equal(0, first.SyncFolder(folder, password));
            second.SyncFolder(folder, password);
            Assert.Equal("Birinci güncellendi", first.Get(a.Id)!.Content);
            Assert.Equal(columns[2].Id, second.BoardPositions()[edited.Id].Column);
            first.SaveBoardColumn(board, columns[2] with { Deleted = true });
            first.OrderBoardCards(columns[0].Id, [a.Id]);
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            Assert.DoesNotContain(second.BoardColumns(group2.Uuid), item => item.Id == columns[2].Id);
            Assert.Equal(columns[0].Id, second.BoardPositions()[edited.Id].Column);
            first.SetBoardPosition(a.Id, new(columns[3].Id, 0, DateTimeOffset.UtcNow.AddDays(-5).ToString("O")));
            Assert.Equal(1, first.ArchiveFinishedBoardCards(3));
            Assert.True(first.Get(a.Id)!.Archived);
        });
    }

    [Fact]
    public void FeatureSelectionsSyncWithoutRemovingDisabledModuleData()
    {
        InFolder(path =>
        {
            const string password = "feature-test-only";
            var folder = Path.Combine(Path.GetDirectoryName(path)!, "shared");
            using var first = new NoteStore(path, password);
            using var second = new NoteStore(path + ".second", password);
            var flags = new FeatureFlags(first);
            var other = new FeatureFlags(second);
            Assert.False(flags.Enabled(Feature.Authenticator));
            Assert.True(flags.Enabled(Feature.Tags));
            var note = first.Create("[x] Bitti #deneme");
            note.Checklist = true;
            first.Save(note);
            first.SetDesktopPin(note.Id, "{}");
            var account = new OtpAccount(Guid.NewGuid(), "Deneme", "QA", "GEZDGNBVGY3TQOJQ",
                "SHA1", 6, 30, null, null, null, Guid.Parse(note.Uuid));
            first.SaveAuthenticatorAccounts([account]);
            foreach (var feature in Enum.GetValues<Feature>())
            {
                flags.Set(feature, false);
                Assert.False(flags.Enabled(feature));
            }
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            Assert.All(Enum.GetValues<Feature>(), feature => Assert.False(other.Enabled(feature)));
            var pulled = Assert.Single(second.Search("#deneme", 0, null));
            Assert.True(second.Get(pulled.Id)!.Checklist);
            Assert.Equal(account.Id, Assert.Single(second.AuthenticatorAccounts()).Id);
            Assert.Equal("{}", first.DesktopPin(note.Id));
            other.SyncSelections = false;
            foreach (var feature in Enum.GetValues<Feature>()) flags.Set(feature, true);
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            Assert.False(other.Enabled(Feature.Tags));
            other.SyncSelections = true;
            second.SyncFolder(folder, password);
            Assert.All(Enum.GetValues<Feature>(), feature => Assert.True(other.Enabled(feature)));
            Assert.Equal(account.Id, Assert.Single(first.AuthenticatorAccounts()).Id);
            Assert.Equal("{}", first.DesktopPin(note.Id));
        });
    }

    [Fact]
    public void FolderSyncEncryptsNotesAndPreservesBothConcurrentEdits()
    {
        InFolder(path =>
        {
            const string password = "sadece-senkron-denemesi";
            var root = Path.GetDirectoryName(path)!;
            var folder = Path.Combine(root, "shared");
            using var first = new NoteStore(path);
            var portableData = Directory.CreateDirectory(Path.Combine(root, "İkinci kopya", "Veri"));
            using var second = new NoteStore(Path.Combine(portableData.FullName, "notes.db"));
            var note = first.Create("Canary First Portable 1324", first.GetOrCreateGroup("Deneme Firma"), "Ortak not");
            var picture = first.InsertPicture(note.Id, new Picture("image/png", [15, 25, 35], [4], 1, 1, "Deneme.png"));
            note.Content += $"\n[[resim:{picture}]]";
            first.Save(note);
            first.SyncPictures(note);
            first.SyncFolder(folder, password);
            Assert.All(Directory.GetFiles(folder, "*.record"), file =>
                Assert.DoesNotContain("Canary First Portable 1324", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file))));
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => second.SyncFolder(folder, "wrong password"));
            second.SyncFolder(folder, password);
            var otherNote = second.Get(Assert.Single(second.Search("Canary", 0, null)).Id)!;
            Assert.Equal(note.Uuid, otherNote.Uuid);
            Assert.Equal(new byte[] { 15, 25, 35 }, second.GetFirstPicture(otherNote.Id)!.Bytes);
            note.Content = note.Content.Replace("Canary First Portable 1324", "Birinci cihaz değişikliği");
            first.Save(note);
            otherNote.Content = otherNote.Content.Replace("Canary First Portable 1324", "İkinci cihaz değişikliği");
            second.Save(otherNote);
            first.SyncFolder(folder, password);
            Assert.Equal(1, second.SyncFolder(folder, password));
            first.SyncFolder(folder, password);
            foreach (var store in new[] { first, second })
            {
                var cards = store.Search("", 0, null);
                Assert.Equal(2, cards.Count);
                Assert.Single(cards, card => card.Title.StartsWith("Çakışma kopyası"));
                Assert.Contains(cards, card => card.Preview.Contains("Birinci cihaz"));
                Assert.Contains(cards, card => card.Preview.Contains("İkinci cihaz"));
            }
            Assert.Equal(0, second.SyncFolder(folder, password));
            var toDelete = first.Get(note.Id)!;
            toDelete.Deleted = NoteStore.Now();
            first.Save(toDelete);
            first.SyncFolder(folder, password);
            second.SyncFolder(folder, password);
            Assert.NotNull(second.Get(otherNote.Id)!.Deleted);
            Assert.Single(second.Search("", 0, null));
            using var fresh = new NoteStore(Path.Combine(root, "fresh.db"));
            fresh.SyncFolder(folder, password);
            var trashed = Assert.Single(fresh.Search("", 2, null));
            Assert.Equal(note.Uuid, fresh.Get(trashed.Id)!.Uuid);
            Assert.Equal(new byte[] { 15, 25, 35 }, fresh.GetFirstPicture(trashed.Id)!.Bytes);
        });
    }

    [Fact]
    public void CloudProviderRoutesEncryptedFilesToOneDriveAppRoot()
    {
        var id = Guid.NewGuid();
        using var client = new HttpClient(new CloudStub(id));
        var provider = new CloudSyncProvider(new OneDriveObjectStore(client, () => "fake-token"));
        Assert.Equal(id, Assert.Single(provider.ListRecords()));
        provider.PutBlob(id, [7, 9]);
    }

    private sealed class CloudStub(Guid id) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer fake-token", request.Headers.Authorization!.ToString());
            Assert.Equal("graph.microsoft.com", request.RequestUri!.Host);
            Assert.StartsWith("/v1.0/me/drive/special/approot", request.RequestUri.AbsolutePath);
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""{"value":[{"name":"{{id:N}}.record"},{"name":"ignore.blob"}]}""")
                };
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith($"{id:N}.blob:/content", request.RequestUri.AbsolutePath);
            Assert.Equal(new byte[] { 7, 9 }, request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult());
            return new HttpResponseMessage(System.Net.HttpStatusCode.Created);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DesktopOAuthUsesLoopbackPkceAndProtectsTokens(bool google)
    {
        using var stub = new OAuthStub();
        using var http = new HttpClient(stub);
        using var browser = new HttpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var oauth = new DesktopOAuth(http, new OAuthConfiguration(new("https://example.test/auth"),
            new("https://example.test/token"), "fixture", "fixture.scope", google));
        Task<HttpResponseMessage>? callback = null;
        var tokens = await oauth.LoginAsync(uri =>
        {
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            Assert.Equal("S256", q["code_challenge_method"]);
            Assert.Equal(google ? "offline" : null, q["access_type"]);
            stub.Challenge = q["code_challenge"];
            callback = browser.GetAsync(q["redirect_uri"] + "?state=" + q["state"] + "&code=fixture-code", timeout.Token);
        }, timeout.Token);
        using var acknowledged = await callback!;
        Assert.True(acknowledged.IsSuccessStatusCode);
        Assert.Equal("refresh-fixture", tokens.Refresh);
        var refreshed = await oauth.RefreshAsync(tokens.Refresh, timeout.Token);
        Assert.Equal(tokens.Refresh, refreshed.Refresh);
        var id = Guid.NewGuid();
        using var vault = new SyncEncryption("test-password", new byte[32]);
        var sealedTokens = vault.Seal(id, "oauth", refreshed);
        Assert.DoesNotContain(tokens.Refresh, System.Text.Encoding.UTF8.GetString(sealedTokens));
        Assert.Equal(refreshed, vault.Open<OAuthTokens>(id, "oauth", sealedTokens));
        using var wrong = new SyncEncryption("wrong-password", new byte[32]);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => wrong.Open<OAuthTokens>(id, "oauth", sealedTokens));
    }

    private sealed class OAuthStub : HttpMessageHandler
    {
        internal string? Challenge;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("fixture", form["client_id"]);
            var refresh = form["grant_type"] == "refresh_token";
            if (refresh) Assert.Equal("refresh-fixture", form["refresh_token"]);
            else
            {
                Assert.Equal("fixture-code", form["code"]);
                var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(form["code_verifier"]!));
                Assert.Equal(Challenge, Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(refresh
                    ? """{"access_token":"updated","expires_in":3600}"""
                    : """{"access_token":"access","refresh_token":"refresh-fixture","expires_in":3600}""")
            };
        }
    }

    [Fact]
    public void CloudKitAdapterUsesPrivateDatabaseAndRoundTripsOpaqueRecords()
    {
        var id = Guid.NewGuid();
        using var client = new HttpClient(new CloudKitStub());
        var provider = new CloudSyncProvider(new CloudKitObjectStore(client, "iCloud.fixture", false, "fake-api", () => "fake-user"));
        Assert.Null(provider.GetRecord(id));
        provider.PutRecord(id, [7, 9]);
        provider.PutBlob(id, [3, 5]);
        Assert.Equal(id, Assert.Single(provider.ListRecords()));
        Assert.Equal(new byte[] { 7, 9 }, provider.GetRecord(id));
        Assert.Equal(new byte[] { 3, 5 }, provider.GetBlob(id));
        provider.Delete(id);
        provider.Delete(id);
        Assert.Empty(provider.ListRecords());
    }

    private sealed class CloudKitStub : HttpMessageHandler
    {
        private readonly Dictionary<string, string> records = [];
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.Equal("api.apple-cloudkit.com", uri.Host);
            Assert.StartsWith("/database/1/iCloud.fixture/development/private/records/", uri.AbsolutePath);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            Assert.Equal("fake-api", query["ckAPIToken"]);
            Assert.Equal("fake-user", query["ckWebAuthToken"]);
            Assert.Null(request.Headers.Authorization);
            using var document = System.Text.Json.JsonDocument.Parse(request.Content!.ReadAsStream(cancellationToken));
            var body = document.RootElement;
            object response;
            if (uri.AbsolutePath.EndsWith("/query"))
            {
                Assert.Equal("KaralamaSyncObject", body.GetProperty("query").GetProperty("recordType").GetString());
                response = new { records = records.Keys.Select(name => new { recordName = name }).ToArray() };
            }
            else if (uri.AbsolutePath.EndsWith("/lookup"))
            {
                var name = body.GetProperty("records")[0].GetProperty("recordName").GetString()!;
                response = new { records = new object[] { records.TryGetValue(name, out var payload)
                    ? new { recordName = name, fields = new { payload = new { value = payload } } }
                    : new { recordName = name, serverErrorCode = "NOT_FOUND" } } };
            }
            else
            {
                Assert.EndsWith("/modify", uri.AbsolutePath);
                var operation = body.GetProperty("operations")[0];
                var record = operation.GetProperty("record");
                var name = record.GetProperty("recordName").GetString()!;
                if (operation.GetProperty("operationType").GetString() == "forceReplace")
                {
                    var payload = record.GetProperty("fields").GetProperty("payload");
                    Assert.Equal("BYTES", payload.GetProperty("type").GetString());
                    records[name] = payload.GetProperty("value").GetString()!;
                    response = new { records = new[] { new { recordName = name } } };
                }
                else
                {
                    Assert.Equal("forceDelete", operation.GetProperty("operationType").GetString());
                    response = records.Remove(name)
                        ? new { records = new[] { new { recordName = name } } }
                        : (object)new { records = new[] { new { recordName = name, serverErrorCode = "NOT_FOUND" } } };
                }
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(response))
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    private static void InFolder(Action<string> test)
    {
        var folder = Directory.CreateTempSubdirectory("KaralamaDefteri-test-");
        try { test(Path.Combine(folder.FullName, "notes.db")); }
        catch
        {
            try { folder.Delete(true); }
            catch (IOException) { }
            throw;
        }
        folder.Delete(true);
    }
}
