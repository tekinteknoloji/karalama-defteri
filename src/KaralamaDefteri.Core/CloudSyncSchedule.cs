namespace KaralamaDefteri;

internal static class CloudSyncSchedule
{
    internal static bool ShouldRun(int mode, DateTimeOffset now, DateTimeOffset last, TimeOnly dailyTime, bool afterEdit)
    {
        if (afterEdit) return mode == 0;
        return mode switch
        {
            1 => now - last >= TimeSpan.FromHours(1),
            2 => last.Date < now.Date && (TimeOnly.FromDateTime(now.DateTime) >= dailyTime ||
                last.Date < now.Date.AddDays(-1) ||
                last.Date == now.Date.AddDays(-1) && TimeOnly.FromDateTime(last.DateTime) < dailyTime),
            _ => now - last >= TimeSpan.FromMinutes(1)
        };
    }
}
