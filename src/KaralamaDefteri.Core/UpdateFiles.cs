namespace KaralamaDefteri;

internal static class UpdateFiles
{
    internal static string Previous(string executable) => Path.ChangeExtension(executable, ".old.exe");

    internal static void Install(string executable, string verified)
    {
        var old = Previous(executable);
        if (File.Exists(old)) throw new IOException("Önceki güncelleme henüz sonuçlanmadı.");
        File.Replace(verified, executable, old);
    }

    internal static bool Recover(string executable, bool healthy)
    {
        var old = Previous(executable);
        if (!File.Exists(old)) return false;
        if (healthy) File.Delete(old);
        else File.Replace(old, executable, null);
        return !healthy;
    }
}
