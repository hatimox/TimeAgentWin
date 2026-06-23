using System.Windows;
using System.Windows.Controls;

namespace TimeAgent;

/// Settings: account/token, meetings (ids+rounding+dynamic), recurring, days off.
public class SettingsWindow : Window
{
    private readonly AppStore _store;

    public SettingsWindow(AppStore store)
    {
        _store = store;
        Title = "TimeAgent Settings";
        Width = 520; Height = 520;
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Account", Content = AccountTab() });
        tabs.Items.Add(new TabItem { Header = "Meetings", Content = MeetingsTab() });
        tabs.Items.Add(new TabItem { Header = "Recurring", Content = RecurringTab() });
        tabs.Items.Add(new TabItem { Header = "Days off", Content = DaysOffTab() });
        Content = tabs;
    }

    private static StackPanel Panel() => new() { Margin = new Thickness(12) };
    private static StackPanel Field(string label, FrameworkElement input)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        p.Children.Add(new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center });
        input.MinWidth = 240;
        p.Children.Add(input);
        return p;
    }

    private UIElement AccountTab()
    {
        var p = Panel();
        var url = new TextBox { Text = _store.Settings.TpUrl };
        var token = new PasswordBox();
        token.Password = _store.Settings.Token;
        var who = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
        if (_store.Settings.MyUserId != 0) who.Text = $"Signed in: {_store.Settings.MyUserName} (id {_store.Settings.MyUserId})";
        var save = new Button { Content = "Save", Margin = new Thickness(0, 8, 0, 0), Width = 80 };
        save.Click += async (_, _) =>
        {
            var changed = token.Password != _store.Settings.Token || url.Text != _store.Settings.TpUrl;
            _store.Settings.TpUrl = url.Text; _store.Settings.Token = token.Password;
            if (changed) { _store.Settings.MyUserId = 0; _store.Settings.MyUserName = ""; _store.Settings.MyUserEmail = ""; }
            _store.Settings.Save();
            _store.RebuildClient();
            await _store.EnsureUser();
            await _store.Refresh();
            who.Text = "Saved — detecting user…";
        };
        p.Children.Add(Field("Instance URL", url));
        p.Children.Add(Field("API token", token));
        p.Children.Add(who);
        p.Children.Add(save);
        p.Children.Add(new TextBlock { Text = "Token stored encrypted (DPAPI).", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });
        return p;
    }

    private UIElement MeetingsTab()
    {
        var p = Panel();
        var daily = new TextBox { Text = _store.Settings.DailyTaskId.ToString(), Width = 100 };
        var meet = new TextBox { Text = _store.Settings.MeetingsTaskId.ToString(), Width = 100 };
        var min = new TextBox { Text = _store.Settings.MeetingMinMinutes.ToString(), Width = 100 };
        var step = new TextBox { Text = _store.Settings.MeetingStepMinutes.ToString(), Width = 100 };
        p.Children.Add(Field("Daily task id", daily));
        p.Children.Add(Field("Meetings task id", meet));
        p.Children.Add(Field("Min minutes", min));
        p.Children.Add(Field("Step minutes", step));

        p.Children.Add(new TextBlock { Text = "Dynamic meeting shortcuts", Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.Bold });
        var dynBox = new StackPanel();
        var rows = new List<(TextBox name, TextBox tid, DynamicMeeting m)>();
        void RenderDyn()
        {
            dynBox.Children.Clear(); rows.Clear();
            foreach (var m in _store.Settings.DynamicMeetings)
            {
                var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                var name = new TextBox { Text = m.Name, Width = 220 };
                var tid = new TextBox { Text = m.TaskId.ToString(), Width = 80 };
                r.Children.Add(name); r.Children.Add(tid);
                dynBox.Children.Add(r);
                rows.Add((name, tid, m));
            }
        }
        RenderDyn();
        var add = new Button { Content = "+ Add meeting", Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) => { _store.Settings.DynamicMeetings.Add(new DynamicMeeting { Name = "New meeting" }); RenderDyn(); };
        p.Children.Add(dynBox); p.Children.Add(add);

        var save = new Button { Content = "Save", Margin = new Thickness(0, 8, 0, 0), Width = 80 };
        save.Click += (_, _) =>
        {
            _store.Settings.DailyTaskId = long.TryParse(daily.Text, out var a) ? a : 0;
            _store.Settings.MeetingsTaskId = long.TryParse(meet.Text, out var b) ? b : 0;
            _store.Settings.MeetingMinMinutes = int.TryParse(min.Text, out var c) ? c : 30;
            _store.Settings.MeetingStepMinutes = int.TryParse(step.Text, out var d) ? d : 15;
            _store.Settings.DynamicMeetings = rows
                .Select(r => new DynamicMeeting { Id = r.m.Id, Name = r.name.Text, TaskId = long.TryParse(r.tid.Text, out var t) ? t : 0, Description = r.m.Description })
                .Where(m => !string.IsNullOrWhiteSpace(m.Name) || m.TaskId != 0).ToList();
            _store.Settings.Save();
        };
        p.Children.Add(save);
        return new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement RecurringTab()
    {
        var p = Panel();
        var box = new StackPanel();
        var rows = new List<(TextBox label, TextBox tid, TextBox hrs, RecurringEntry r)>();
        void RenderRec()
        {
            box.Children.Clear(); rows.Clear();
            foreach (var r in _store.Settings.Recurring)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                var label = new TextBox { Text = r.Label, Width = 200 };
                var tid = new TextBox { Text = r.TaskId.ToString(), Width = 80 };
                var hrs = new TextBox { Text = r.Hours.ToString(), Width = 56 };
                row.Children.Add(label); row.Children.Add(tid); row.Children.Add(hrs);
                box.Children.Add(row);
                rows.Add((label, tid, hrs, r));
            }
        }
        RenderRec();
        var add = new Button { Content = "+ Add recurring", Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) => { _store.Settings.Recurring.Add(new RecurringEntry { Label = "New" }); RenderRec(); };
        var save = new Button { Content = "Save", Margin = new Thickness(0, 8, 0, 0), Width = 80 };
        save.Click += (_, _) =>
        {
            _store.Settings.Recurring = rows.Select(r => new RecurringEntry
            {
                Id = r.r.Id, Label = r.label.Text,
                TaskId = long.TryParse(r.tid.Text, out var t) ? t : 0,
                Hours = double.TryParse(r.hrs.Text.Replace(',', '.'), out var h) ? h : 1,
            }).ToList();
            _store.Settings.Save();
        };
        p.Children.Add(box); p.Children.Add(add); p.Children.Add(save);
        p.Children.Add(new TextBlock { Text = "Auto-logged once per working day on launch, skipping days off.", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });
        return p;
    }

    private UIElement DaysOffTab()
    {
        var p = Panel();
        var region = new ComboBox { Width = 120, ItemsSource = new[] { "none", "morocco" } };
        region.SelectedIndex = _store.Settings.Region == "morocco" ? 1 : 0;
        p.Children.Add(Field("Region", region));

        p.Children.Add(new TextBlock { Text = "Weekly off (0=Sun … 6=Sat)", Margin = new Thickness(0, 8, 0, 0) });
        var wk = new StackPanel { Orientation = Orientation.Horizontal };
        var names = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        var checks = new List<CheckBox>();
        for (int i = 0; i < 7; i++)
        {
            var c = new CheckBox { Content = names[i], IsChecked = _store.Settings.WeeklyOff.Contains(i), Margin = new Thickness(4, 0, 0, 0) };
            wk.Children.Add(c); checks.Add(c);
        }
        p.Children.Add(wk);

        p.Children.Add(new TextBlock { Text = "Specific days off", Margin = new Thickness(0, 8, 0, 0) });
        var daysBox = new StackPanel();
        foreach (var d in _store.Settings.DaysOff) daysBox.Children.Add(new TextBlock { Text = d });
        var addRow = new StackPanel { Orientation = Orientation.Horizontal };
        var newDay = new TextBox { Width = 120 };
        var addBtn = new Button { Content = "Add", Margin = new Thickness(4, 0, 0, 0) };
        addBtn.Click += (_, _) => { if (newDay.Text.Length == 10) { _store.Settings.DaysOff.Add(newDay.Text); daysBox.Children.Add(new TextBlock { Text = newDay.Text }); newDay.Text = ""; } };
        addRow.Children.Add(newDay); addRow.Children.Add(addBtn);
        p.Children.Add(daysBox); p.Children.Add(addRow);

        var save = new Button { Content = "Save", Margin = new Thickness(0, 8, 0, 0), Width = 80 };
        save.Click += (_, _) =>
        {
            _store.Settings.Region = region.SelectedIndex == 1 ? "morocco" : "none";
            _store.Settings.WeeklyOff = checks.Select((c, i) => (c, i)).Where(x => x.c.IsChecked == true).Select(x => x.i).ToList();
            _store.Settings.Save();
        };
        p.Children.Add(save);
        return p;
    }
}
