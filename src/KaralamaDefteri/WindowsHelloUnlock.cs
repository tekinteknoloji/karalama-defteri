using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;

namespace KaralamaDefteri;

internal static class WindowsHelloUnlock
{
    private static readonly byte[] Header = "KDHello2"u8.ToArray();
    private static string KeyName => $"KaralamaDefteri-{AppPaths.InstanceId}";
    internal static bool Configured => File.Exists(AppPaths.HelloEnvelope);

    internal static async Task<bool> IsSupported()
    {
        try { return await KeyCredentialManager.IsSupportedAsync(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Windows Hello: {0:X8}", error.HResult);
            return false;
        }
    }

    internal static async Task Enable(string password)
    {
        try
        {
            var result = await KeyCredentialManager.RequestCreateAsync(KeyName, KeyCredentialCreationOption.ReplaceExisting);
            if (result.Status != KeyCredentialStatus.Success) throw Failure(result.Status);
            var salt = RandomNumberGenerator.GetBytes(32);
            var key = await DeriveKey(result.Credential, salt);
            var plain = Encoding.UTF8.GetBytes(password);
            try
            {
                var payload = new byte[Header.Length + 32 + 28 + plain.Length];
                Header.CopyTo(payload, 0);
                salt.CopyTo(payload, Header.Length);
                Encrypt(key, plain, payload);
                Center.SaveSecurityFile(AppPaths.HelloEnvelope, payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (System.Runtime.InteropServices.COMException error)
        { throw new CryptographicException("Windows Hello anahtarı oluşturulamadı.", error); }
    }

    internal static async Task<string> Unlock(Window owner)
    {
        var payload = File.ReadAllBytes(AppPaths.HelloEnvelope);
        if (!payload.AsSpan().StartsWith(Header) || payload.Length <= Header.Length + 60)
            throw new CryptographicException("Windows Hello bağlantısını Güvenlik ayarlarından yeniden kurun.");
        try
        {
            owner.Activate();
            var result = await KeyCredentialManager.OpenAsync(KeyName);
            if (result.Status != KeyCredentialStatus.Success) throw Failure(result.Status);
            var key = await DeriveKey(result.Credential, payload.AsSpan(Header.Length, 32).ToArray());
            try { return Decrypt(key, payload); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        catch (System.Runtime.InteropServices.COMException error)
        { throw new CryptographicException("Windows Hello anahtarı açılamadı.", error); }
    }

    private static async Task<byte[]> DeriveKey(KeyCredential credential, byte[] salt)
    {
        var challenge = Header.Concat(salt).ToArray();
        var result = await credential.RequestSignAsync(CryptographicBuffer.CreateFromByteArray(challenge));
        if (result.Status != KeyCredentialStatus.Success) throw Failure(result.Status);
        CryptographicBuffer.CopyToByteArray(result.Result, out var signature);
        try { return SHA256.HashData(signature); }
        finally { CryptographicOperations.ZeroMemory(signature); }
    }

    private static void Encrypt(byte[] key, byte[] plain, byte[] payload)
    {
        var envelope = payload.AsSpan(Header.Length + 32);
        RandomNumberGenerator.Fill(envelope[..12]);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(envelope[..12], plain, envelope[28..], envelope.Slice(12, 16));
    }

    private static string Decrypt(byte[] key, byte[] payload)
    {
        var envelope = payload.AsSpan(Header.Length + 32);
        var plain = new byte[envelope.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope[..12], envelope[28..], envelope.Slice(12, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static CryptographicException Failure(KeyCredentialStatus status) =>
        new($"Windows Hello: {status}");

    internal static async Task Disable()
    {
        File.Delete(AppPaths.HelloEnvelope);
        try { await KeyCredentialManager.DeleteAsync(KeyName); }
        catch (System.Runtime.InteropServices.COMException error)
        { System.Diagnostics.Trace.TraceWarning("Windows Hello: {0:X8}", error.HResult); }
    }
}
