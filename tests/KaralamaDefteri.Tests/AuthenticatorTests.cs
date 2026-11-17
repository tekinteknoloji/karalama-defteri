using System.IO;
using KaralamaDefteri;
using Xunit;

public sealed class AuthenticatorTests
{
    [Fact]
    public void GoogleTransferExportRoundTripsSelectedAccountsAcrossMultipleCodes()
    {
        var accounts = Enumerable.Range(1, 5).Select(i => Authenticator.Parse(
            $"otpauth://totp/Deneme:Test{i}?secret={Secret(20)}&issuer=Deneme")).ToArray();
        var payloads = AuthenticatorMigration.Export(accounts);
        Assert.Equal(2, payloads.Length);
        var importer = new AuthenticatorMigration();
        Assert.Null(importer.Import(payloads[1]));
        var result = importer.Import(payloads[0])!;
        Assert.Equal(accounts.Select(a => a with { Id = Guid.Empty }), result.Select(a => a with { Id = Guid.Empty }));
        Assert.Throws<FormatException>(() => AuthenticatorMigration.Export([accounts[0] with { Period = 60 }]));
    }

    private static string Secret(int length) =>
        Authenticator.Encode(System.Text.Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234"[..length]));

    [Theory]
    [InlineData(59, "94287082", "46119246", "90693936")]
    [InlineData(1111111109, "07081804", "68084774", "25091201")]
    [InlineData(1111111111, "14050471", "67062674", "99943326")]
    [InlineData(1234567890, "89005924", "91819424", "93441116")]
    [InlineData(2000000000, "69279037", "90698825", "38618901")]
    [InlineData(20000000000, "65353130", "77737706", "47863826")]
    public void Rfc6238(long time, string sha1, string sha256, string sha512)
    {
        Assert.Equal(sha1, Authenticator.Code(Secret(20), "SHA1", 8, time / 30));
        Assert.Equal(sha256, Authenticator.Code(Secret(32), "SHA256", 8, time / 30));
        Assert.Equal(sha512, Authenticator.Code(Secret(64), "SHA512", 8, time / 30));
    }

    [Fact]
    public void Rfc4226()
    {
        string[] expected = ["755224", "287082", "359152", "969429", "338314", "254676", "287922", "162583", "399871", "520489"];
        Assert.Equal(expected, Enumerable.Range(0, 10).Select(counter => Authenticator.Code(Secret(20), "SHA1", 6, counter)));
    }

    [Fact]
    public void EncryptionGateAndEncryptedReplica()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var first = Path.Combine(dir.FullName, "first.db");
            var second = Path.Combine(dir.FullName, "second.db");
            var sync = dir.CreateSubdirectory("sync").FullName;
            var account = Authenticator.Parse($"otpauth://hotp/Deneme:hesap?secret={Secret(20)}&counter=0");
            using (var plain = new NoteStore(first))
            {
                Assert.Throws<InvalidOperationException>(() => plain.AuthenticatorAccounts());
                Assert.Throws<InvalidOperationException>(() => plain.SaveAuthenticatorAccounts([account]));
                Assert.Throws<InvalidOperationException>(() => plain.RemoveAuthenticatorAccount(account.Id));
            }
            NoteStore.ReplaceDatabaseFormat(first, null, "test-local-first-1234");
            using var source = new NoteStore(first, "test-local-first-1234");
            using var target = new NoteStore(second, "test-local-second-1234");
            source.SaveAuthenticatorAccounts([account]);
            Assert.Throws<InvalidOperationException>(() => NoteStore.ExportDatabase(first, "test-local-first-1234", Path.Combine(dir.FullName, "plaintext.db"), null));
            Assert.Throws<InvalidOperationException>(() => source.ApplyDatabasePassword(null, dir.FullName));
            source.SyncFolder(sync, "test-sync-only-1234");
            target.SyncFolder(sync, "test-sync-only-1234");
            Assert.Equal(account, Assert.Single(target.AuthenticatorAccounts()));
            source.RemoveAuthenticatorAccount(account.Id);
            source.SyncFolder(sync, "test-sync-only-1234");
            target.SyncFolder(sync, "test-sync-only-1234");
            Assert.Empty(target.AuthenticatorAccounts());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MultipartImportAcceptsOutOfOrderParts(byte version)
    {
        string Part(int index, byte label)
        {
            byte[] payload = [10, 10, 10, 3, 1, 2, 3, 18, 1, label, 48, 2, 16, version, 24, 2, 32, (byte)index, 40, 20];
            return "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload));
        }
        var importer = new AuthenticatorMigration();
        Assert.Null(importer.Import(Part(1, (byte)'B')));
        Assert.Equal(new[] { "A", "B" }, importer.Import(Part(0, (byte)'A'))!.Select(account => account.Name));
    }
}
