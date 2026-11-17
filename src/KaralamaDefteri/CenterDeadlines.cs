using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace KaralamaDefteri;

public sealed partial class Center
{
    private readonly List<(long Due, Border Card, TextBlock Label)> deadlineCards = [];

    private void ShowUpcoming(string period)
    {
        SelectGroup(SelectedGroup, 0);
        search.Text = "tarih:" + period;
        search.Focus();
        search.CaretIndex = search.Text.Length;
    }

    private void ApplyDeadlineAppearance(long id, Border border, Panel panel)
    {
        if (!Features.Enabled(Feature.BoardDeadlines) || store.ReadDeadline(id)?.Due is not long due) return;
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        panel.Children.Insert(1, text);
        deadlineCards.Add((due, border, text));
        UpdateDeadlineColor(due, border, text, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private static void UpdateDeadlineColor(long due, Border border, TextBlock text, long now)
    {
        var role = NoteDeadline.BorderRole(due, now);
        var label = role == "DeadlineOverdue" ? "Gecikti" : role == "DeadlineSoon" ? "Yaklaşıyor" : "Son tarih";
        text.Text = $"{label} · {DateTimeOffset.FromUnixTimeSeconds(due).ToLocalTime():dd.MM.yyyy HH:mm}";
        if (role == null) return;
        border.BorderThickness = new Thickness(2);
        border.SetResourceReference(Border.BorderBrushProperty, role + "Brush");
    }

    private void EditDeadline(Card card)
    {
        if (!Features.Enabled(Feature.BoardDeadlines)) return;
        var saved = store.ReadDeadline(card.Id);
        var local = saved?.Due is long due ? DateTimeOffset.FromUnixTimeSeconds(due).ToLocalTime() : DateTimeOffset.Now;
        var window = Owned("Son tarih ve hatırlatma", 380, 300);
        var panel = new StackPanel { Margin = new Thickness(16) };
        var date = new DatePicker { SelectedDate = local.Date };
        var time = new TextBox { Text = local.ToString("HH:mm", CultureInfo.InvariantCulture) };
        var reminder = new CheckBox { Content = "Son tarihinde Windows bildirimi göster", IsChecked = saved?.Reminder == true,
            Margin = new Thickness(0, 10, 0, 8) };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Theme.Brush("WarningInk") };
        AutomationProperties.SetName(date, "Son tarih");
        AutomationProperties.SetName(time, "Saat (SS:dd)");
        panel.Children.Add(new TextBlock { Text = "Son tarih" });
        panel.Children.Add(date);
        panel.Children.Add(new TextBlock { Text = "Saat (SS:dd)", Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(time);
        panel.Children.Add(reminder);
        var save = Button("Uygula", () =>
        {
            if (date.SelectedDate is not DateTime day ||
                !TimeOnly.TryParseExact(time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
            {
                error.Text = "Bir tarih seçip saati 14:30 biçiminde girin.";
                return;
            }
            var localTime = day.Date.Add(clock.ToTimeSpan());
            if (TimeZoneInfo.Local.IsInvalidTime(localTime))
            {
                error.Text = "Bu saat, saat dilimi geçişi nedeniyle kullanılamıyor. Başka bir saat seçin.";
                return;
            }
            var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTime)).ToUnixTimeSeconds();
            Save(utc, reminder.IsChecked == true);
        });
        save.IsDefault = true;
        panel.Children.Add(save);
        if (saved?.Due != null) panel.Children.Add(Button("Son tarihi kaldır", () => Save(null, false)));
        panel.Children.Add(error);
        window.Content = panel;
        window.ShowDialog();

        void Save(long? value, bool remind)
        {
            if (!Flush(false)) return;
            try
            {
                store.SaveDeadline(card.Id, value, remind);
                if (syncPassword != null && syncAfterSave != null) Restart(syncAfterSave);
                ScheduleCloudAfterEdit();
                RefreshCards();
                window.Close();
                CheckDeadlineReminders();
            }
            catch (SqliteException) { error.Text = "Son tarih kaydedilemedi."; }
        }
    }

    private long nextDeadlineBalloonAt;

    internal void StartDeadlineReminders()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) =>
        {
            CheckDeadlineReminders();
            if (!Features.Enabled(Feature.BoardDeadlines) || !IsVisible) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var (due, card, label) in deadlineCards) UpdateDeadlineColor(due, card, label, now);
        };
        Closed += (_, _) => timer.Stop();
        timer.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(CheckDeadlineReminders));
    }

    private void CheckDeadlineReminders()
    {
        if (exiting || RestoreInProgress || !Features.Enabled(Feature.BoardDeadlines)) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now < nextDeadlineBalloonAt) return;
        var pending = store.PendingDeadlineReminders(now);
        if (pending.Length == 0) return;
        var (id, title, due) = pending[0];
        var app = (App)Application.Current;
        if (app.ShowDeadlineBalloon(title, DateTimeOffset.FromUnixTimeSeconds(due).ToLocalTime()))
        {
            store.MarkDeadlineNotified(id);
            nextDeadlineBalloonAt = now + 15;
        }
    }
}
