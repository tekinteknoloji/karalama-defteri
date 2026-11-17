using System.Diagnostics;

namespace KaralamaDefteri;

internal static class UpdateStartup
{
    internal static string Healthy => Path.Combine(AppPaths.Updates, "healthy");
    internal static string Recovered => Path.Combine(AppPaths.Updates, "recovered");
    internal static string Helper => Path.Combine(AppPaths.Updates, "update-helper.exe");
    internal static string Download => Path.Combine(AppPaths.Updates, "next.exe");
    private const string LeaseName = "update.lock";

    internal static bool RecoverInterrupted(string[] arguments)
    {
        if (arguments is ["--update-first-launch"] || !File.Exists(UpdateFiles.Previous(AppPaths.Executable))) return false;
        FileStream lease;
        try { lease = new FileStream(Path.Combine(AppPaths.Updates, LeaseName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return true; }
        using (lease)
        {
            if (File.Exists(Healthy))
            {
                UpdateFiles.Recover(AppPaths.Executable, true);
                File.Delete(Healthy);
                return false;
            }
        }
        LaunchHelper("--recover-update");
        return true;
    }

    internal static void LaunchHelper(string command, string? hash = null)
    {
        if (command == "--apply-update") File.Copy(AppPaths.Executable, Helper, true);
        var launch = new ProcessStartInfo(Helper) { UseShellExecute = false, CreateNoWindow = true };
        launch.ArgumentList.Add(command);
        launch.ArgumentList.Add(AppPaths.Executable);
        launch.ArgumentList.Add(Environment.ProcessId.ToString());
        launch.ArgumentList.Add(hash ?? "");
        using var helper = Process.Start(launch);
    }

    // The helper exits before the application initializes its portable paths or opens SQLite.
    internal static bool Handle(string[] arguments)
    {
        if (arguments is not [var mode, var installation, var processId, var hash] ||
            mode is not ("--apply-update" or "--recover-update") ||
            !int.TryParse(processId, out var previous)) return false;
        var folder = Path.GetDirectoryName(Environment.ProcessPath)!;
        var expected = Path.GetFullPath(installation);
        if (!string.Equals(Path.GetDirectoryName(expected), Path.GetFullPath(Path.Combine(folder, "..", "..")), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(expected).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return true;
        var success = Path.Combine(folder, "healthy");
        var rolledBack = Path.Combine(folder, "recovered");
        FileStream lease;
        try { lease = new FileStream(Path.Combine(folder, LeaseName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return true; }
        using var updateLease = lease;
        try
        {
            try
            {
                using var parent = Process.GetProcessById(previous);
                if (!parent.WaitForExit(15000)) return true;
            }
            catch (ArgumentException) { }
            if (mode == "--recover-update")
            {
                if (UpdateFiles.Recover(expected, false)) RestartPrevious(expected, rolledBack);
                return true;
            }
            File.Delete(success);
            var downloaded = Path.Combine(folder, "next.exe");
            using (var file = File.OpenRead(downloaded))
                if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Güncelleme özeti değişti.");
            UpdateFiles.Install(expected, downloaded);
            var start = new ProcessStartInfo(expected) { UseShellExecute = false };
            start.ArgumentList.Add("--update-first-launch");
            using var candidate = Process.Start(start);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(60) && !File.Exists(success) && candidate is { HasExited: false })
                Thread.Sleep(200);
            var healthy = File.Exists(success);
            if (!healthy && candidate is { HasExited: false })
            {
                candidate.Kill();
                candidate.WaitForExit();
            }
            if (UpdateFiles.Recover(expected, healthy)) RestartPrevious(expected, rolledBack);
            File.Delete(success);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Trace.TraceError(error.Message);
            // Keep the rollback executable in place for the next startup if a file is busy.
            try { if (UpdateFiles.Recover(expected, File.Exists(success))) RestartPrevious(expected, rolledBack); }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { System.Diagnostics.Trace.TraceError(failure.Message); }
        }
        return true;
    }

    private static void RestartPrevious(string executable, string marker)
    {
        File.WriteAllText(marker, "");
        using var recovered = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
    }
}
