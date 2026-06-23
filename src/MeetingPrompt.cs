using System.Windows;
using System.Windows.Controls;

namespace TimeAgent;

/// End-of-meeting flow: Daily / [Defined list] / Choose task / Cancel, then a
/// task or defined-meeting picker. WPF dialogs on the UI thread.
public static class MeetingPrompt
{
    public static void Present(AppStore store, DateTime startUtc, DateTime endUtc)
    {
        var rawH = (endUtc - startUtc).TotalHours;
        var hours = Math.Round(store.BillableHours(rawH) * 100) / 100;
        var date = DateOnly.FromDateTime(startUtc.ToLocalTime());
        string win = $"{startUtc.ToLocalTime():HH:mm}-{endUtc.ToLocalTime():HH:mm}";
        bool hasDynamic = store.Settings.DynamicMeetings.Count > 0;

        var dlg = new Window { Title = "Meeting ended", Width = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        var p = new StackPanel { Margin = new Thickness(14) };
        p.Children.Add(new TextBlock { Text = $"Meeting ended ({win}, {hours:0.00}h)", FontWeight = FontWeights.Bold });
        p.Children.Add(new TextBlock { Text = "How should this be logged?", Margin = new Thickness(0, 4, 0, 10) });
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var daily = new Button { Content = "Daily", Margin = new Thickness(4, 0, 0, 0), MinWidth = 70 };
        var defined = new Button { Content = "Defined list", Margin = new Thickness(4, 0, 0, 0), MinWidth = 90 };
        var choose = new Button { Content = "Choose task", Margin = new Thickness(4, 0, 0, 0), MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(4, 0, 0, 0), MinWidth = 70 };
        btns.Children.Add(daily);
        if (hasDynamic) btns.Children.Add(defined);
        btns.Children.Add(choose); btns.Children.Add(cancel);
        p.Children.Add(btns);
        dlg.Content = p;

        daily.Click += async (_, _) => { dlg.Close(); await store.LogTime(store.Settings.DailyTaskId, hours, "", date); };
        choose.Click += (_, _) => { dlg.Close(); PickTask(store, hours, date); };
        if (hasDynamic) defined.Click += (_, _) => { dlg.Close(); PickDefined(store, hours, date); };
        cancel.Click += async (_, _) => { dlg.Close(); await store.Refresh(); };

        dlg.Show();
    }

    private static void PickTask(AppStore store, double hours, DateOnly date)
    {
        var items = store.Items.Where(i => !i.IsFinal).ToList();
        var dlg = new Window { Title = "Choose task", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        var p = new StackPanel { Margin = new Thickness(14) };
        p.Children.Add(new TextBlock { Text = "Select the task to log this time to" });
        var combo = new ComboBox { Margin = new Thickness(0, 6, 0, 6) };
        foreach (var it in items) combo.Items.Add($"#{it.Id} — {it.Name}");
        var defIdx = items.FindIndex(i => i.Id == store.Settings.MeetingsTaskId);
        combo.SelectedIndex = defIdx >= 0 ? defIdx : 0;
        var note = new TextBox { };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "Log", MinWidth = 70 };
        var cancel = new Button { Content = "Cancel", MinWidth = 70, Margin = new Thickness(4, 0, 0, 0) };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        p.Children.Add(combo); p.Children.Add(new TextBlock { Text = "Description (optional)" }); p.Children.Add(note); p.Children.Add(btns);
        dlg.Content = p;
        ok.Click += async (_, _) => { var i = combo.SelectedIndex; dlg.Close(); if (i >= 0 && i < items.Count) await store.LogTime(items[i].Id, hours, note.Text, date); };
        cancel.Click += (_, _) => dlg.Close();
        dlg.Show();
    }

    private static void PickDefined(AppStore store, double hours, DateOnly date)
    {
        var meetings = store.Settings.DynamicMeetings;
        var dlg = new Window { Title = "Select meeting", Width = 400, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        var p = new StackPanel { Margin = new Thickness(14) };
        var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var m in meetings) combo.Items.Add($"{m.Name} (#{m.TaskId})");
        combo.SelectedIndex = 0;
        var note = new TextBox { Text = meetings.FirstOrDefault()?.Description ?? "" };
        combo.SelectionChanged += (_, _) => { var i = combo.SelectedIndex; if (i >= 0 && i < meetings.Count) note.Text = meetings[i].Description; };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "Log", MinWidth = 70 };
        var cancel = new Button { Content = "Cancel", MinWidth = 70, Margin = new Thickness(4, 0, 0, 0) };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        p.Children.Add(combo); p.Children.Add(new TextBlock { Text = "Description (editable)" }); p.Children.Add(note); p.Children.Add(btns);
        dlg.Content = p;
        ok.Click += async (_, _) => { var i = combo.SelectedIndex; dlg.Close(); if (i >= 0 && i < meetings.Count) await store.LogTime(meetings[i].TaskId, hours, note.Text, date); };
        cancel.Click += (_, _) => dlg.Close();
        dlg.Show();
    }
}
