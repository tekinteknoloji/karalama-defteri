using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KaralamaDefteri;

internal static class UpdateSignature
{
    internal static void Verify(string path, string? thumbprint)
    {
        var file = new FileInfoNative { Size = Marshal.SizeOf<FileInfoNative>(), Path = path };
        var pointer = Marshal.AllocHGlobal(file.Size);
        Marshal.StructureToPtr(file, pointer, false);
        try
        {
            var trust = new TrustData { Size = Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer, StateAction = 1 };
            var policy = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            var result = WinVerifyTrust(new IntPtr(-1), ref policy, ref trust);
            trust.StateAction = 2;
            WinVerifyTrust(new IntPtr(-1), ref policy, ref trust);
            if (result == unchecked((int)0x800B0100) && string.IsNullOrWhiteSpace(thumbprint)) return;
            if (result != 0) throw new CryptographicException($"Güncellemenin Authenticode imzası doğrulanamadı (0x{result:X8}).");
            if (!string.IsNullOrWhiteSpace(thumbprint))
            {
                using var certificate = X509Certificate.CreateFromSignedFile(path);
                if (!certificate.GetCertHashString().Equals(thumbprint.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException("Güncelleme imzasının yayımcısı beklenen sertifikayla eşleşmiyor.");
            }
        }
        finally
        {
            Marshal.DestroyStructure<FileInfoNative>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoNative
    {
        internal int Size;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Path;
        internal IntPtr Handle, KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        internal int Size;
        internal IntPtr PolicyData, ClientData;
        internal int Ui, Revocation, Choice;
        internal IntPtr File;
        internal int StateAction;
        internal IntPtr State, Url;
        internal int Flags, Context;
        internal IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);
}
