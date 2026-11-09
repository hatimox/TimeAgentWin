using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static TimeAgent.Ui;

namespace TimeAgent;

/// Settings (styled to match the tray popover + tasks window): a segmented tab
/// bar over card sections — Account, Meetings, Recurring, Days off.
public class SettingsWindow : Window
{
    private readonly AppStore _store;

    private readonly ContentControl _body = new();
    private readonly List<(string name, Func<UIElement> build)> _tabs;
    private readonly UIElement?[] _cache;
    private readonly List<Border> _chips = new();
    private int _sel;

    public SettingsWindow(AppStore store)
    {
        _store = store;
        Title = "TimeAgent Settings";
        Width = 560; Height = 600;
        Background = B("#F2EEEA");
        Icon = AppIcon;

        _tabs = new()
        {
            ("Account", AccountTab),
            ("Meetings", MeetingsTab),
            ("Recurring", RecurringTab),
            ("Days off", DaysOffTab),
        };
        _cache = new UIElement?[_tabs.Count];

        var root = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 14, 14, 4) };
        for (int i = 0; i < _tabs.Count; i++)
        {
            int idx = i;
            var chip = Clickable(new TextBlock { Text = _tabs[i].name, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, () => SwitchTo(idx), "#FFFFFF", "#ECE7E2", 8, new Thickness(16, 7, 16, 7));
            chip.Margin = new Thickness(0, 0, 6, 0);
            _chips.Add(chip); bar.Children.Add(chip);
        }
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14, 8, 14, 14) });
        Content = root;

        SwitchTo(0);
    }

    private void SwitchTo(int i)
    {
        _sel = i;
        _cache[i] ??= _tabs[i].build();
        _body.Content = _cache[i];
        for (int j = 0; j < _chips.Count; j++)
        {
            bool on = j == _sel;
            _chips[j].Background = B(on ? "#3B82F6" : "#FFFFFF");
            _chips[j].BorderBrush = B("#E2DDD7");
            _chips[j].BorderThickness = new Thickness(on ? 0 : 1);
            ((TextBlock)_chips[j].Child).Foreground = B(on ? "#FFFFFF" : "#5B6470");
        }
    }

    // ---------- tabs ----------

    private UIElement AccountTab()
    {
        var url = Tb(_store.Settings.TpUrl);
        var token = new PasswordBox { Password = _store.Settings.Token, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        var who = new TextBlock { Margin = new Thickness(0, 10, 0, 0), Foreground = B("#5B6470") };
        if (_store.Settings.MyUserId != 0) who.Text = $"Signed in: {_store.Settings.MyUserName} (id {_store.Settings.MyUserId})";

        var save = PrimaryButton("Save", async () =>
        {
            var changed = token.Password != _store.Settings.Token || url.Text != _store.Settings.TpUrl;
            _store.Settings.TpUrl = url.Text; _store.Settings.Token = token.Password;
            if (changed) { _store.Settings.MyUserId = 0; _store.Settings.MyUserName = ""; _store.Settings.MyUserEmail = ""; }
            _store.Settings.Save();
            _store.RebuildClient();
            who.Text = "Saved — detecting user…";
            await _store.EnsureUser();
            await _store.Refresh();
            who.Text = _store.Settings.MyUserId != 0
                ? $"Signed in: {_store.Settings.MyUserName} (id {_store.Settings.MyUserId})"
                : "Saved — could not detect user (check URL/token).";
        });

        return Stack(
            Card("Account",
                Field("Instance URL", Shell(url)),
                Field("API token", Shell(token)),
                who,
                Row(save),
                Hint("Token stored encrypted on this PC (DPAPI, per-user).")));
    }

    private UIElement MeetingsTab()
    {
        var daily = Tb(_store.Settings.DailyTaskId.ToString());
        var meet = Tb(_store.Settings.MeetingsTaskId.ToString());
        var min = Tb(_store.Settings.MeetingMinMinutes.ToString());
        var step = Tb(_store.Settings.MeetingStepMinutes.ToString());

        var hpd = Tb(_store.Settings.HoursPerDay.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var descReq = new CheckBox { Content = "Description required", IsChecked = _store.Settings.TimeDescriptionRequired };

        var dynBox = new StackPanel();
        var rows = new List<(TextBox name, TextBox tid, DynamicMeeting m)>();
        void RenderDyn()
        {
            dynBox.Children.Clear(); rows.Clear();
            foreach (var m in _store.Settings.DynamicMeetings)
            {
                var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var name = Tb(m.Name); var tid = Tb(m.TaskId.ToString());
                r.Children.Add(WatermarkShell(name, "Meeting name", 250));
                var tidShell = WatermarkShell(tid, "Task id", 90); tidShell.Margin = new Thickness(6, 0, 0, 0);
                r.Children.Add(tidShell);
                dynBox.Children.Add(r);
                rows.Add((name, tid, m));
            }
        }
        RenderDyn();
        var add = SecondaryButton("＋ Add meeting", () => { _store.Settings.DynamicMeetings.Add(new DynamicMeeting { Name = "New meeting" }); RenderDyn(); });

        var save = PrimaryButton("Save", () =>
        {
            _store.Settings.DailyTaskId = long.TryParse(daily.Text, out var a) ? a : 0;
            _store.Settings.MeetingsTaskId = long.TryParse(meet.Text, out var b) ? b : 0;
            _store.Settings.MeetingMinMinutes = int.TryParse(min.Text, out var c) ? c : 30;
            _store.Settings.MeetingStepMinutes = int.TryParse(step.Text, out var d) ? d : 15;
            _store.Settings.TimeDescriptionRequired = descReq.IsChecked == true;
            _store.Settings.HoursPerDay = double.TryParse(hpd.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) && h >= 0 ? h : 8;
            _store.Settings.DynamicMeetings = rows
                .Select(r => new DynamicMeeting { Id = r.m.Id, Name = r.name.Text, TaskId = long.TryParse(r.tid.Text, out var t) ? t : 0, Description = r.m.Description })
                .Where(m => !string.IsNullOrWhiteSpace(m.Name) || m.TaskId != 0).ToList();
            _store.Settings.Save();
        });

        return Stack(
            Card("Meeting logging",
                Field("Daily task id", Shell(daily, 120)),
                Field("Meetings task id", Shell(meet, 120)),
                Field("Min minutes", Shell(min, 120)),
                Field("Step minutes", Shell(step, 120)),
                Hint("Detected meetings round up to a multiple of the step, at least the minimum.")),
            Card("Time entries",
                Field("Hours per working day", Shell(hpd, 120)),
                Hint("Targets on the Day / Week / Month totals: working days × this. Weekly days off, days off and holidays don't count."),
                descReq,
                Hint("Turns on automatically when TargetProcess rejects a time entry without a description.")),
            Card("Dynamic meeting shortcuts", dynBox, Row(add)),
            Row(save));
    }

    private UIElement RecurringTab()
    {
        var box = new StackPanel();
        var rows = new List<(TextBox label, TextBox tid, TextBox hrs, RecurringEntry r)>();
        void RenderRec()
        {
            box.Children.Clear(); rows.Clear();
            foreach (var r in _store.Settings.Recurring)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var label = Tb(r.Label); var tid = Tb(r.TaskId.ToString()); var hrs = Tb(r.Hours.ToString("0.##"));
                row.Children.Add(WatermarkShell(label, "Label", 220));
                var ts = WatermarkShell(tid, "Task id", 90); ts.Margin = new Thickness(6, 0, 0, 0);
                var hs = WatermarkShell(hrs, "Hrs", 60); hs.Margin = new Thickness(6, 0, 0, 0);
                row.Children.Add(ts); row.Children.Add(hs);
                box.Children.Add(row);
                rows.Add((label, tid, hrs, r));
            }
        }
        RenderRec();
        var add = SecondaryButton("＋ Add recurring", () => { _store.Settings.Recurring.Add(new RecurringEntry { Label = "New" }); RenderRec(); });
        var save = PrimaryButton("Save", () =>
        {
            _store.Settings.Recurring = rows.Select(r => new RecurringEntry
            {
                Id = r.r.Id, Label = r.label.Text,
                TaskId = long.TryParse(r.tid.Text, out var t) ? t : 0,
                Hours = double.TryParse(r.hrs.Text.Replace(',', '.'), out var h) ? h : 1,
            }).ToList();
            _store.Settings.Save();
        });

        return Stack(
            Card("Recurring auto-log", box, Row(add),
                Hint("Auto-logged once per working day on launch, skipping days off.")),
            Row(save));
    }

    private UIElement DaysOffTab()
    {
        var region = new ComboBox { Width = 140, ItemsSource = new[] { "none", "morocco" }, VerticalAlignment = VerticalAlignment.Center };
        region.SelectedIndex = _store.Settings.Region == "morocco" ? 1 : 0;

        var wk = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var names = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        var checks = new List<CheckBox>();
        for (int i = 0; i < 7; i++)
        {
            var c = new CheckBox { Content = names[i], IsChecked = _store.Settings.WeeklyOff.Contains(i), Margin = new Thickness(0, 0, 10, 0) };
            wk.Children.Add(c); checks.Add(c);
        }

        var daysBox = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var current = new List<string>(_store.Settings.DaysOff);
        void RenderDays()
        {
            daysBox.Children.Clear();
            foreach (var d in current)
            {
                var localD = d;
                var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                r.Children.Add(new TextBlock { Text = d, Width = 120, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#5B6470") });
                r.Children.Add(Clickable(new TextBlock { Text = "✕", Foreground = B("#FFFFFF"), FontSize = 11 }, () => { current.Remove(localD); RenderDays(); }, "#E5484D", "#D33B40", 6, new Thickness(8, 3, 8, 3)));
                daysBox.Children.Add(r);
            }
        }
        RenderDays();
        var newDay = Tb("");
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var newShell = WatermarkShell(newDay, "YYYY-MM-DD", 140);
        var addBtn = SecondaryButton("Add", () => { if (DateOnly.TryParse(newDay.Text, out var d)) { var s = d.ToString("yyyy-MM-dd"); if (!current.Contains(s)) current.Add(s); newDay.Text = ""; RenderDays(); } });
        addBtn.Margin = new Thickness(6, 0, 0, 0);
        addRow.Children.Add(newShell); addRow.Children.Add(addBtn);

        // ---- Holidays (Morocco only) ----
        var slots = _store.Settings.ReligiousSlots
            .GroupBy(x => x.Key).Select(g => g.First())
            .Select(x => new ReligiousSlot { Key = x.Key, Date = x.Date, On = x.On }).ToList();
        int hYear = (DateTime.Today.Year >= 2026 && DateTime.Today.Year <= 2030) ? DateTime.Today.Year : 2026;
        var holidayBox = new StackPanel();
        var yearLabel = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, Width = 50, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        void RenderHolidays()
        {
            holidayBox.Children.Clear();
            yearLabel.Text = hYear.ToString();
            holidayBox.Children.Add(new TextBlock { Text = "Public holidays (fixed)", FontWeight = FontWeights.SemiBold, Foreground = B("#1A1A1A"), Margin = new Thickness(0, 8, 0, 2) });
            foreach (var d in Holidays.FixedHolidays(hYear))
                holidayBox.Children.Add(new TextBlock { Text = d, Foreground = B("#5B6470"), FontSize = 13, Margin = new Thickness(0, 1, 0, 0) });
            holidayBox.Children.Add(new TextBlock { Text = "Religious holidays (estimates — confirm/adjust)", FontWeight = FontWeights.SemiBold, Foreground = B("#1A1A1A"), Margin = new Thickness(0, 12, 0, 2) });
            var days = Holidays.ReligiousDays(hYear);
            if (days.Count == 0)
            {
                holidayBox.Children.Add(new TextBlock { Text = $"No estimates for {hYear}. Add specific days off above.", Foreground = B("#9A938C"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
                return;
            }
            foreach (var day in days)
            {
                var d = day;
                var slot = slots.FirstOrDefault(x => x.Key == d.Key);
                bool on = slot?.On ?? true;
                string date = !string.IsNullOrEmpty(slot?.Date) ? slot!.Date : d.Estimate;
                ReligiousSlot Ensure()
                {
                    var sl = slots.FirstOrDefault(x => x.Key == d.Key);
                    if (sl == null) { sl = new ReligiousSlot { Key = d.Key, Date = d.Estimate, On = true }; slots.Add(sl); }
                    return sl;
                }
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                var cb = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                cb.Click += (_, _) => Ensure().On = cb.IsChecked == true;
                var tb = Tb(date);
                tb.TextChanged += (_, _) => Ensure().Date = tb.Text.Trim();
                row.Children.Add(cb);
                row.Children.Add(new TextBlock { Text = d.Label, Width = 190, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#5B6470"), FontSize = 13 });
                row.Children.Add(WatermarkShell(tb, "YYYY-MM-DD", 130));
                holidayBox.Children.Add(row);
            }
        }
        RenderHolidays();
        var yearPrev = SecondaryButton("◀", () => { if (hYear > 2026) { hYear--; RenderHolidays(); } });
        var yearNext = SecondaryButton("▶", () => { if (hYear < 2030) { hYear++; RenderHolidays(); } });
        var yearRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        yearRow.Children.Add(new TextBlock { Text = "Holiday year", Width = 150, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#5B6470"), FontSize = 13 });
        yearRow.Children.Add(yearPrev); yearRow.Children.Add(yearLabel); yearRow.Children.Add(yearNext);
        var holidayCard = Card("Holidays", yearRow, holidayBox);
        void UpdateHolidayVisibility() => holidayCard.Visibility = region.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        region.SelectionChanged += (_, _) => UpdateHolidayVisibility();
        UpdateHolidayVisibility();

        var save = PrimaryButton("Save", () =>
        {
            _store.Settings.ReligiousSlots = slots
                .Where(x => string.IsNullOrEmpty(x.Date) || DateOnly.TryParse(x.Date, out _))
                .Select(x => new ReligiousSlot { Key = x.Key, Date = x.Date, On = x.On }).ToList();
            _store.Settings.Region = region.SelectedIndex == 1 ? "morocco" : "none";
            _store.Settings.WeeklyOff = checks.Select((c, i) => (c, i)).Where(x => x.c.IsChecked == true).Select(x => x.i).ToList();
            _store.Settings.DaysOff = new List<string>(current);
            _store.Settings.Save();
        });

        return Stack(
            Card("Region & weekend",
                Field("Region", region),
                new TextBlock { Text = "Weekly off", Foreground = B("#5B6470"), Margin = new Thickness(0, 10, 0, 0) },
                wk),
            Card("Specific days off", daysBox, addRow),
            holidayCard,
            Row(save));
    }

    // ---------- builders ----------

    private static StackPanel Stack(params UIElement[] kids)
    {
        var p = new StackPanel();
        foreach (var k in kids) p.Children.Add(k);
        return p;
    }

    private static Border Card(string title, params UIElement[] kids)
    {
        var p = new StackPanel();
        if (title.Length > 0) p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = B("#1A1A1A"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var k in kids) p.Children.Add(k);
        return new Border { Background = B("#FFFFFF"), BorderBrush = B("#E7E2DD"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = p };
    }

    private static UIElement Field(string label, UIElement input)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#5B6470"), FontSize = 13 });
        Grid.SetColumn(input, 1); g.Children.Add(input);
        return g;
    }

    private static TextBox Tb(string text) => new() { Text = text, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };

    private static Border Shell(UIElement inner, double width = double.NaN) => new()
    {
        Width = width,
        HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
        CornerRadius = new CornerRadius(7), Background = B("#FFFFFF"), BorderBrush = B("#E2DDD7"), BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center, Child = inner,
    };

    private static Border WatermarkShell(TextBox tb, string hint, double width)
    {
        var b = Shell(Watermark(tb, hint, 13), width);
        return b;
    }

    private static Border PrimaryButton(string text, Action onClick)
    {
        var b = Clickable(new TextBlock { Text = text, Foreground = B("#FFFFFF"), FontWeight = FontWeights.SemiBold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center }, onClick, "#3B82F6", "#2F6BFF", 8, new Thickness(18, 8, 18, 8));
        b.HorizontalAlignment = HorizontalAlignment.Left;
        return b;
    }

    private static Border SecondaryButton(string text, Action onClick)
    {
        var b = Clickable(new TextBlock { Text = text, Foreground = B("#5B6470"), FontWeight = FontWeights.SemiBold, FontSize = 13 }, onClick, "#FFFFFF", "#ECE7E2", 8, new Thickness(14, 6, 14, 6));
        b.BorderBrush = B("#E2DDD7"); b.BorderThickness = new Thickness(1);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        return b;
    }

    private static UIElement Row(UIElement inner)
    {
        inner.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 10, 0, 0));
        return inner;
    }

    private static TextBlock Hint(string text) => new() { Text = text, Foreground = B("#9A938C"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
}
