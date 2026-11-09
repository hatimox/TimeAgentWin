using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static TimeAgent.Ui;

namespace TimeAgent;

/// Tasks & Bugs window (parity with the macOS/Linux ports): search, Active
/// toggle, Sprint/Status/Sort filters, card rows with a TASK/BUG badge, a
/// per-task Start/Stop stopwatch, status change, US link, hours total with
/// inline edit/delete, direct logging, and a Today/Week/Month totals footer.
public class TasksWindow : Window
{
    private readonly AppStore _store;

    private readonly TextBox _search = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private bool _activeOnly = true;
    private Border _activeChip = null!;
    private readonly ComboBox _sprint = new() { Width = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _statusFilter = new() { Width = 160, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _typeFilter = new() { Width = 120, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _summary = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox _sort = new() { Width = 150, VerticalAlignment = VerticalAlignment.Center };

    private readonly StackPanel _list = new();
    private readonly TextBlock _loaded = new() { Foreground = B("#8A8A8A"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _shown = new() { Foreground = B("#8A8A8A"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private TextBlock _todayVal = null!, _weekVal = null!, _monthVal = null!, _monthName = null!;
    private TextBlock _dayLabel = null!, _weekLabel = null!;
    private Border _dayNext = null!, _weekNext = null!;
    private int _dayOffset, _weekOffset;   // 0 = current, -1 = previous, ...

    private DateOnly _month = FirstOfThisMonth();
    private readonly DispatcherTimer _ticker;
    private TextBlock? _activeElapsed;   // elapsed label on the running task's Stop button
    private bool _suppressSprintEvent;

    public TasksWindow(AppStore store)
    {
        _store = store;
        Title = "TimeAgent — Tasks";
        Width = 880; Height = 680;
        Background = B("#F2EEEA");
        Icon = AppIcon;

        Content = BuildLayout();

        _search.TextChanged += (_, _) => Render();
        _statusFilter.SelectionChanged += (_, _) => Render();
        _typeFilter.SelectionChanged += (_, _) => Render();
        _sort.SelectionChanged += (_, _) => Render();
        _sprint.SelectionChanged += async (_, _) =>
        {
            if (_suppressSprintEvent) return;
            _store.ScopeAll = _sprint.SelectedIndex == 1;
            await _store.Refresh();
        };

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => { if (_activeElapsed != null && _store.ActiveTimerStart is { } s) _activeElapsed.Text = Elapsed(s); };
        _ticker.Start();

        _store.ItemsChanged += OnItems;
        _store.TimesChanged += OnTimes;
        _store.StatusChanged += OnStatus;
        _store.TimerChanged += OnTimer;
        Closed += (_, _) =>
        {
            _store.ItemsChanged -= OnItems; _store.TimesChanged -= OnTimes;
            _store.StatusChanged -= OnStatus; _store.TimerChanged -= OnTimer;
            _ticker.Stop();
        };

        UpdateStatusFilterOptions();
        Render();
    }

    private void OnItems() { UpdateStatusFilterOptions(); Render(); }
    private void OnTimes() => Render();
    private void OnStatus(string s) => _loaded.Text = s;
    private void OnTimer() => Render();

    // ---------- layout ----------

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var status = BuildStatusBar();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        _list.Margin = new Thickness(12, 4, 12, 8);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0) });
        return root;
    }

    private UIElement BuildToolbar()
    {
        var wrap = new StackPanel { Margin = new Thickness(12, 12, 12, 6) };

        // row 1: search + Active + refresh + gear
        var r1 = new DockPanel();
        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal };
        rightBtns.Children.Add(IconButton("⟳", () => _ = _store.Refresh()));
        var gear = IconButton("⚙", () => new SettingsWindow(_store) { Owner = this }.Show());
        gear.Margin = new Thickness(6, 0, 0, 0);
        rightBtns.Children.Add(gear);
        DockPanel.SetDock(rightBtns, Dock.Right);
        r1.Children.Add(rightBtns);

        _activeChip = Clickable(IconText("◉", "Active", "#FFFFFF", 13, FontWeights.SemiBold), () => { _activeOnly = !_activeOnly; StyleActiveChip(); Render(); }, "#3B82F6", "#2F6BFF", 8, new Thickness(14, 7, 14, 7));
        _activeChip.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(_activeChip, Dock.Right);
        r1.Children.Add(_activeChip);
        StyleActiveChip();

        // search box (fills remaining width)
        var searchBorder = new Border { CornerRadius = new CornerRadius(8), Background = B("#FFFFFF"), BorderBrush = B("#E2DDD7"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6) };
        var sg = new Grid();
        sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sg.ColumnDefinitions.Add(new ColumnDefinition());
        sg.Children.Add(new TextBlock { Text = "🔍", FontSize = 13, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = B("#9A938C") });
        var sw = Watermark(_search, "Search stories, tasks & bugs (name or #id)…");
        Grid.SetColumn(sw, 1); sg.Children.Add(sw);
        searchBorder.Child = sg;
        r1.Children.Add(searchBorder);
        wrap.Children.Add(r1);

        // row 2: Sprint / Status / Sort
        var r2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _sprint.ItemsSource = new[] { "Current sprint", "All" };
        _suppressSprintEvent = true; _sprint.SelectedIndex = _store.ScopeAll ? 1 : 0; _suppressSprintEvent = false;
        _sort.ItemsSource = new[] { "Name A–Z", "Name Z–A", "Hours (high→low)", "# ID" };
        _sort.SelectedIndex = 0;
        r2.Children.Add(FilterLabel("⚑", "Sprint")); r2.Children.Add(_sprint);
        _typeFilter.ItemsSource = new[] { "All types", "User Story", "Task", "Bug" };
        _typeFilter.SelectedIndex = 0;
        r2.Children.Add(FilterLabel("▦", "Type")); r2.Children.Add(_typeFilter);
        r2.Children.Add(FilterLabel("●", "Status")); r2.Children.Add(_statusFilter);
        r2.Children.Add(FilterLabel("↕", "Sort")); r2.Children.Add(_sort);
        wrap.Children.Add(r2);
        wrap.Children.Add(_summary);

        return wrap;
    }

    private UIElement BuildStatusBar()
    {
        var bar = new Border { Background = B("#ECE7E2"), Padding = new Thickness(14, 8, 14, 8) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_loaded); left.Children.Add(_shown);
        grid.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(Stepped(out _todayVal, out _dayLabel, out _dayNext,
            () => { _dayOffset--; RefreshTotals(); },
            () => { if (_dayOffset < 0) { _dayOffset++; RefreshTotals(); } }));
        right.Children.Add(Stepped(out _weekVal, out _weekLabel, out _weekNext,
            () => { _weekOffset--; RefreshTotals(); },
            () => { if (_weekOffset < 0) { _weekOffset++; RefreshTotals(); } }));

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(Clickable(new TextBlock { Text = "‹", FontSize = 16, Foreground = B("#555") }, () => { _month = _month.AddMonths(-1); RefreshTotals(); }, "#00FFFFFF", "#22000000", 6, new Thickness(8, 2, 8, 2)));
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        _monthVal = new TextBlock { FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#1A1A1A") };
        _monthName = new TextBlock { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#8A8A8A") };
        center.Children.Add(_monthVal); center.Children.Add(_monthName);
        nav.Children.Add(center);
        nav.Children.Add(Clickable(new TextBlock { Text = "›", FontSize = 16, Foreground = B("#555") }, () => { _month = _month.AddMonths(1); RefreshTotals(); }, "#00FFFFFF", "#22000000", 6, new Thickness(8, 2, 8, 2)));
        right.Children.Add(nav);

        Grid.SetColumn(right, 1); grid.Children.Add(right);
        bar.Child = grid;
        return bar;
    }

    // ---------- rendering ----------

    private void Render()
    {
        _list.Children.Clear();
        var q = _search.Text.Trim().ToLowerInvariant();
        string statusF = _statusFilter.SelectedItem as string ?? "All statuses";

        var filtered = _store.Items.Where(item =>
        {
            if (_activeOnly && item.IsFinal) return false;
            if (statusF != "All statuses" && item.StateName != statusF) return false;
            if (_typeFilter.SelectedItem is string tf && tf != "All types" && item.DisplayType != tf) return false;
            if (q.Length > 0 && !(item.Name.ToLowerInvariant().Contains(q) || item.Id.ToString().Contains(q))) return false;
            return true;
        });

        IEnumerable<WorkItem> ordered = _sort.SelectedIndex switch
        {
            1 => filtered.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
            2 => filtered.OrderByDescending(i => _store.HoursFor(i.Id)),
            3 => filtered.OrderBy(i => i.Id),
            _ => filtered.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };

        _activeElapsed = null;
        int shown = 0;
        foreach (var item in ordered) { _list.Children.Add(Row(item)); shown++; }
        _shown.Text = $"{shown} shown";
        RenderSummary();
        if (string.IsNullOrEmpty(_loaded.Text)) _loaded.Text = _store.Status;
        RefreshTotals();
    }

    private Border Row(WorkItem item)
    {
        var box = new StackPanel();
        bool isBug = item.IsBug;

        // line 1: badge + name ... start + hours
        var l1 = new Grid();
        l1.ColumnDefinitions.Add(new ColumnDefinition());
        l1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nameRow.Children.Add(Badge(item.DisplayType.ToUpperInvariant(), item.IsStory ? "#8B5CF6" : isBug ? "#EF6C2F" : "#3B82F6"));
        nameRow.Children.Add(new TextBlock { Text = item.Name, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, Foreground = B("#1A1A1A") });
        l1.Children.Add(nameRow);

        var right1 = new StackPanel { Orientation = Orientation.Horizontal };
        right1.Children.Add(StartCell(item));
        var slots = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var total = _store.HoursFor(item.Id);
        if (total > 0)
        {
            bool open = false;
            var pill = Clickable(IconText("🕐", $"{total:0.00}h", "#5B6470", 12), () => { open = !open; slots.Children.Clear(); if (open) BuildSlots(slots, item.Id); }, "#EEF1F6", "#E3E8F0", 7, new Thickness(10, 6, 10, 6));
            pill.Margin = new Thickness(8, 0, 0, 0);
            right1.Children.Add(pill);
        }
        Grid.SetColumn(right1, 1); l1.Children.Add(right1);
        box.Children.Add(l1);
        box.Children.Add(slots);

        // line 2: id + US + status + project + sprint
        var l2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        l2.Children.Add(Link($"#{item.Id}", () => _store.OpenInTp(item.Id)));
        if (item.UsId != 0 && !item.IsStory)
        {
            var us = Link($"US #{item.UsId}", () => _store.OpenInTp(item.UsId));
            us.Foreground = B("#8A8A8A"); us.ToolTip = item.UsName;
            us.Margin = new Thickness(8, 0, 0, 0);
            l2.Children.Add(us);
        }
        var stateCombo = new ComboBox { Width = 150, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        stateCombo.Items.Add(item.StateName); stateCombo.SelectedIndex = 0;
        _ = PopulateStates(stateCombo, item);
        l2.Children.Add(stateCombo);
        var proj = IconText("🗀", item.ProjectName, "#8A8A8A", 12);
        proj.Margin = new Thickness(12, 0, 0, 0);
        l2.Children.Add(proj);
        if (item.Sprint.Length > 0)
        {
            var sp = Badge(item.Sprint, "#E8EEFB", "#3B82F6", 11);
            sp.Margin = new Thickness(10, 0, 0, 0);
            l2.Children.Add(sp);
        }
        box.Children.Add(l2);

        // line 3: direct log
        var l3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var hrs = new TextBox { Width = 56, VerticalAlignment = VerticalAlignment.Center };
        var date = new DatePicker { SelectedDate = DateTime.Today, Width = 130, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var note = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        var noteWrap = new Border { Width = 360, Margin = new Thickness(8, 0, 0, 0), Child = Watermark(note, _store.Settings.TimeDescriptionRequired ? "Description (required)" : "Add a note…") };
        var err = new TextBlock { Foreground = B("#E5484D"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        bool logging = false;
        Border log = null!;
        bool Disabled() => logging || (_store.Settings.TimeDescriptionRequired && string.IsNullOrWhiteSpace(note.Text));
        void UpdateLog() => log.Opacity = Disabled() ? 0.45 : 1;
        // Fields are cleared only once TP accepts the entry; on failure they stay
        // filled and the error shows under the row.
        log = Clickable(IconText("＋", "Log", "#FFFFFF", 13, FontWeights.SemiBold), async () =>
        {
            if (Disabled()) return;
            if (!double.TryParse(hrs.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) || h <= 0)
            { err.Text = "Enter hours greater than 0."; err.Visibility = Visibility.Visible; return; }
            var d = DateOnly.FromDateTime(date.SelectedDate ?? DateTime.Today);
            logging = true; UpdateLog(); err.Visibility = Visibility.Collapsed;
            var e = await _store.LogTime(item.Id, h, note.Text, d);
            logging = false;
            if (e == null) { hrs.Text = ""; note.Text = ""; }
            else { err.Text = "⚠ " + e; err.Visibility = Visibility.Visible; }
            UpdateLog();
        }, "#3B82F6", "#2F6BFF", 8, new Thickness(14, 7, 14, 7));
        note.TextChanged += (_, _) => UpdateLog();
        UpdateLog();
        log.Margin = new Thickness(8, 0, 0, 0);
        l3.Children.Add(WrapInput(hrs, 56, "hrs")); l3.Children.Add(date); l3.Children.Add(noteWrap); l3.Children.Add(log);
        box.Children.Add(l3);
        box.Children.Add(err);

        return new Border { Background = B("#FFFFFF"), BorderBrush = B("#E7E2DD"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = box };
    }

    private UIElement StartCell(WorkItem item)
    {
        bool running = _store.ActiveTimerTaskId == item.Id;
        if (running)
        {
            var label = new TextBlock { Text = _store.ActiveTimerStart is { } s ? Elapsed(s) : "0:00:00", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = B("#FFFFFF"), VerticalAlignment = VerticalAlignment.Center };
            _activeElapsed = label;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = "■  ", FontSize = 12, Foreground = B("#FFFFFF"), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(label);
            return Clickable(content, async () => await _store.StopTaskTimer(), "#E5484D", "#D33B40", 8, new Thickness(14, 7, 14, 7));
        }
        return Clickable(IconText("▶", "Start", "#FFFFFF", 13, FontWeights.SemiBold), async () => await _store.StartTaskTimer(item.Id), "#3B82F6", "#2F6BFF", 8, new Thickness(14, 7, 14, 7));
    }

    private async Task PopulateStates(ComboBox combo, WorkItem item)
    {
        var states = await _store.StatesFor(item);
        if (states.Count == 0) return;
        combo.Items.Clear();
        foreach (var s in states) combo.Items.Add(s.Name);
        var idx = states.FindIndex(s => s.Id == item.StateId);
        if (idx >= 0) combo.SelectedIndex = idx;
        combo.SelectionChanged += async (_, _) =>
        {
            var i = combo.SelectedIndex;
            if (i >= 0 && i < states.Count && states[i].Id != item.StateId)
            {
                if (MessageBox.Show($"Change #{item.Id} to \"{states[i].Name}\"?", "Change status", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                    await _store.ChangeState(item, states[i]);
                else
                {
                    var back = states.FindIndex(s => s.Id == item.StateId);
                    if (back >= 0) combo.SelectedIndex = back;
                }
            }
        };
    }

    private void BuildSlots(StackPanel holder, long itemId)
    {
        var slots = _store.Times.Where(t => t.ItemId == itemId).OrderByDescending(t => t.Day).ToList();
        holder.Children.Add(new TextBlock { Text = $"Time entries — total {slots.Sum(t => t.Hours):0.00}h", Foreground = B("#8A8A8A"), Margin = new Thickness(0, 0, 0, 4) });
        foreach (var e in slots)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            r.Children.Add(new TextBlock { Text = e.Day, Width = 92, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#5B6470") });
            var hrs = new TextBox { Width = 56, Text = e.Hours.ToString("0.##"), VerticalAlignment = VerticalAlignment.Center };
            var note = new TextBox { Width = 300, Text = e.Description, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var save = Clickable(IconText("", "Save", "#FFFFFF", 12, FontWeights.SemiBold), async () => { if (double.TryParse(hrs.Text.Replace(',', '.'), out var h) && h > 0) await _store.UpdateTime(e.Id, h, note.Text, e.Day); }, "#3B82F6", "#2F6BFF", 7, new Thickness(12, 5, 12, 5));
            save.Margin = new Thickness(6, 0, 0, 0);
            var del = Clickable(new TextBlock { Text = "✕", Foreground = B("#FFFFFF"), FontSize = 12 }, async () => { if (MessageBox.Show($"Delete this time entry?\n{e.Day} · {e.Hours:0.00}h", "Delete", MessageBoxButton.OKCancel) == MessageBoxResult.OK) await _store.DeleteTime(e.Id); }, "#E5484D", "#D33B40", 7, new Thickness(10, 5, 10, 5));
            del.Margin = new Thickness(6, 0, 0, 0);
            r.Children.Add(hrs); r.Children.Add(note); r.Children.Add(save); r.Children.Add(del);
            holder.Children.Add(r);
        }
    }

    // ---------- totals + filters ----------

    private void RefreshTotals()
    {
        int off = _store.Settings.TzOffsetMinutes();
        _todayVal.Text = $"{Totals.HoursOn(_store.Times, Totals.DayAt(_dayOffset, off)):0.00}h";
        _dayLabel.Text = Totals.DayLabel(_dayOffset, off);
        _dayNext.Opacity = _dayOffset < 0 ? 1 : 0.25;
        _weekVal.Text = $"{Totals.HoursInWeek(_store.Times, Totals.WeekStart(_weekOffset, off)):0.00}h";
        _weekLabel.Text = Totals.WeekLabel(_weekOffset, off);
        _weekNext.Opacity = _weekOffset < 0 ? 1 : 0.25;
        _monthVal.Text = $"{Sum(d => d.Year == _month.Year && d.Month == _month.Month):0.00}h";
        _monthName.Text = _month.ToString("MMMM yyyy");
    }

    private double Sum(Func<DateOnly, bool> pred) =>
        _store.Times.Where(t => DateOnly.TryParse(t.Day, out var d) && pred(d)).Sum(t => t.Hours);

    /// Open/done counts per type and my hours for the selected sprint scope
    /// (ignores the search, type, status and Active filters).
    private void RenderSummary()
    {
        _summary.Children.Clear();
        var scoped = _store.Items;
        var hours = scoped.Sum(i => _store.HoursFor(i.Id));
        var title = _store.ScopeAll ? "All sprints" : "Current sprint";
        _summary.Children.Add(new TextBlock { Text = "⚑  " + title, FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = B("#1A1A1A") });
        _summary.Children.Add(SummaryChip("User Stories", scoped.Where(i => i.IsStory).ToList(), "#8B5CF6"));
        _summary.Children.Add(SummaryChip("Tasks", scoped.Where(i => !i.IsStory && !i.IsBug).ToList(), "#3B82F6"));
        _summary.Children.Add(SummaryChip("Bugs", scoped.Where(i => i.IsBug).ToList(), "#E5484D"));
        _summary.Children.Add(new TextBlock { Text = $"🕐 {hours:0.00}h logged", FontSize = 12, Foreground = B("#3FB870"), VerticalAlignment = VerticalAlignment.Center, ToolTip = "All time you logged on these items" });
    }

    private static UIElement SummaryChip(string label, List<WorkItem> items, string color)
    {
        int done = items.Count(i => i.IsFinal);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = B(color), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = $"{items.Count - done} open", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = $" · {done} done", FontSize = 12, Foreground = B("#8A8A8A"), VerticalAlignment = VerticalAlignment.Center });
        return new Border { CornerRadius = new CornerRadius(12), Background = B("#1A" + color.TrimStart('#')), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 8, 4), Child = sp, ToolTip = $"{items.Count} {label.ToLowerInvariant()} assigned to you" };
    }

    private void UpdateStatusFilterOptions()
    {
        var current = _statusFilter.SelectedItem as string ?? "All statuses";
        var names = _store.Items.Select(i => i.StateName).Where(n => !string.IsNullOrEmpty(n) && n != "?").Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        var options = new List<string> { "All statuses" };
        options.AddRange(names);
        _statusFilter.ItemsSource = options;
        _statusFilter.SelectedItem = options.Contains(current) ? current : "All statuses";
    }

    private void StyleActiveChip()
    {
        var on = _activeOnly;
        _activeChip.Background = B(on ? "#3B82F6" : "#FFFFFF");
        var txt = (TextBlock)_activeChip.Child;
        txt.Foreground = B(on ? "#FFFFFF" : "#5B6470");
        if (!on) { _activeChip.BorderBrush = B("#E2DDD7"); _activeChip.BorderThickness = new Thickness(1); }
        else _activeChip.BorderThickness = new Thickness(0);
    }

    // ---------- small builders ----------

    private static UIElement FilterLabel(string glyph, string text)
        => new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Children = { new TextBlock { Text = glyph, FontSize = 12, Foreground = B("#9A938C"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 5, 0) }, new TextBlock { Text = text, FontSize = 12, Foreground = B("#5B6470"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) } } };

    private static UIElement Stat(string label, out TextBlock value)
    {
        var p = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        value = new TextBlock { Text = "0.00h", FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#1A1A1A") };
        p.Children.Add(value);
        p.Children.Add(new TextBlock { Text = label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#8A8A8A") });
        return p;
    }

    /// A footer total with ‹ › around it to step its period back (never past the current one).
    private static UIElement Stepped(out TextBlock value, out TextBlock label, out Border next, Action onPrev, Action onNext)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Clickable(new TextBlock { Text = "‹", FontSize = 16, Foreground = B("#555") }, onPrev, "#00FFFFFF", "#22000000", 6, new Thickness(6, 2, 6, 2)));
        value = new TextBlock { Text = "0.00h", FontSize = 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#1A1A1A") };
        label = new TextBlock { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#8A8A8A") };
        var center = new StackPanel { Margin = new Thickness(2, 0, 2, 0), MinWidth = 70 };
        center.Children.Add(value); center.Children.Add(label);
        row.Children.Add(center);
        next = Clickable(new TextBlock { Text = "›", FontSize = 16, Foreground = B("#555") }, onNext, "#00FFFFFF", "#22000000", 6, new Thickness(6, 2, 6, 2));
        row.Children.Add(next);
        return row;
    }

    private static TextBlock Link(string text, Action onClick)
    {
        var tb = new TextBlock { Text = text, Foreground = B("#3B82F6"), FontSize = 12, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        tb.MouseLeftButtonUp += (_, _) => onClick();
        return tb;
    }

    private Border IconButton(string glyph, Action onClick)
    {
        var b = Clickable(new TextBlock { Text = glyph, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#5B6470") }, onClick, "#FFFFFF", "#ECE7E2", 8, new Thickness(10, 7, 10, 7));
        b.BorderBrush = B("#E2DDD7"); b.BorderThickness = new Thickness(1);
        return b;
    }

    private static Border WrapInput(TextBox tb, double width, string hint)
    {
        tb.Width = double.NaN; tb.MinWidth = width - 16;
        return new Border { Width = width, CornerRadius = new CornerRadius(6), Background = B("#FFFFFF"), BorderBrush = B("#E2DDD7"), BorderThickness = new Thickness(1), Padding = new Thickness(6, 4, 6, 4), Child = Watermark(tb, hint, 12) };
    }

    private static string Elapsed(DateTime startLocal)
    {
        var ts = DateTime.Now - startLocal;
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static DateOnly FirstOfThisMonth() { var n = DateTime.Now; return new DateOnly(n.Year, n.Month, 1); }
}
