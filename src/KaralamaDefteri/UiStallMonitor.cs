using System.Diagnostics;
using System.Windows.Threading;

namespace KaralamaDefteri;

public sealed partial class Center
{
    internal void StartUiStallMonitor()
    {
        var stop = new CancellationTokenSource();
        Closed += (_, _) => stop.Cancel();
        var dispatcher = Dispatcher;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested && !dispatcher.HasShutdownStarted)
                {
                    var started = Stopwatch.GetTimestamp();
                    var pulse = dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input, stop.Token);
                    var delay = Task.Delay(200, stop.Token);
                    if (await Task.WhenAny(pulse.Task, delay) == delay && !pulse.Task.IsCompleted && !stop.IsCancellationRequested)
                    {
                        WriteStall(started, "bekliyor");
                        await pulse.Task;
                        WriteStall(started, "tamamlandı");
                    }
                    else await pulse.Task;
                    await Task.Delay(200, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        });

        static void WriteStall(long started, string state)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            try
            {
                var directory = Path.Combine(AppPaths.Data, "Logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "donma.log");
                if (File.Exists(path) && new FileInfo(path).Length > 262144)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} dispatcher_ms={elapsed.TotalMilliseconds:F0} durum={state}{Environment.NewLine}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
