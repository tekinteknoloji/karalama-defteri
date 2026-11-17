using System.Globalization;
using System.Text.RegularExpressions;

namespace KaralamaDefteri;

internal sealed record NoteDeadline(long? Due, bool Reminder, string Updated)
{
    internal static string? BorderRole(long? due, long now) => due switch
    {
        null => null,
        _ when due <= now => "DeadlineOverdue",
        _ when due <= now + 86400 => "DeadlineSoon",
        _ => null
    };

    internal static NoteDeadline? Latest(NoteDeadline? local, NoteDeadline? remote)
    {
        var comparison = string.CompareOrdinal(local?.Updated, remote?.Updated);
        if (comparison == 0) comparison = Nullable.Compare(local?.Due, remote?.Due);
        if (comparison == 0) comparison = Nullable.Compare(local?.Reminder, remote?.Reminder);
        return comparison >= 0 ? local : remote;
    }

    internal static (long? Start, long? End) SearchRange(ref string query, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        long? start = null, end = null;
        query = Regex.Replace(query, @"(?<!\S)tarih:(bugün|hafta)(?!\S)", match =>
        {
            var days = NoteStore.Fold(match.Groups[1].Value) == "bugün" ? 1 : 7;
            start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(today, zone)).ToUnixTimeSeconds();
            var boundary = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(today.AddDays(days), zone)).ToUnixTimeSeconds();
            end = end is long previous ? Math.Min(previous, boundary) : boundary;
            return " ";
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return (start, end);
    }
}

public sealed partial class NoteStore
{
    internal NoteDeadline? ReadDeadline(long id)
    {
        using var command = Command("SELECT deadline_utc,deadline_reminder,deadline_updated_at FROM notes WHERE id=$id", ("$id", id));
        using var rows = command.ExecuteReader();
        if (!rows.Read() || rows.IsDBNull(2)) return null;
        return new(rows.IsDBNull(0) ? null : rows.GetInt64(0), rows.GetBoolean(1), rows.GetString(2));
    }

    internal void SaveDeadline(long id, long? due, bool reminder) =>
        WriteDeadline(id, new(due, due != null && reminder, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)));

    private void WriteDeadline(long id, NoteDeadline deadline) => Execute("""
        UPDATE notes SET deadline_utc=$due,deadline_reminder=$reminder,deadline_updated_at=$updated WHERE id=$id
        """, ("$id", id), ("$due", deadline.Due), ("$reminder", deadline.Reminder), ("$updated", deadline.Updated));

    internal (long Id, string Title, long Due)[] PendingDeadlineReminders(long now)
    {
        using var command = Command("""
            SELECT id,title,deadline_utc FROM notes
            WHERE deadline_reminder=1 AND deadline_utc <= $now AND deleted_at IS NULL AND is_archived=0
              AND deadline_notified_for IS NOT deadline_updated_at
            ORDER BY deadline_utc,id
            """, ("$now", now));
        using var rows = command.ExecuteReader();
        var reminders = new List<(long, string, long)>();
        while (rows.Read()) reminders.Add((rows.GetInt64(0), rows.GetString(1), rows.GetInt64(2)));
        return reminders.ToArray();
    }

    internal void MarkDeadlineNotified(long id) => Execute(
        "UPDATE notes SET deadline_notified_for=deadline_updated_at WHERE id=$id", ("$id", id));
}
