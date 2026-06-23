using System.Windows;
using System.Windows.Controls;

namespace TimeAgent;

/// Tasks & Bugs window: search, active-only, sprint/all scope, per-item status
/// change, parent-US link, hours total, direct logging, edit/delete entries.
public class TasksWindow : Window
{
    private readonly AppStore _store;
    private readonly TextBox _search = new() { Width = 260 };
    private readonly CheckBox _activeOnly = new() { Content = "Active only", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _scope = new() { Width = 130, ItemsSource = new[] { "Current sprint", "All" }, SelectedIndex = 0 };
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new();

    public TasksWindow(AppStore store)
    {
        _store = store;
        Title = "TimeAgent — Tasks";
        Width = 780; Height = 580;

        var root = new DockPanel();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        _search.SetValue(System.Windows.Controls.Control.ToolTipProperty, "Search name or #id");
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(8, 0, 0, 0) };
        toolbar.Children.Add(new Label { Content = "Search:" });
        toolbar.Children.Add(_search);
        toolbar.Children.Add(_activeOnly);
        toolbar.Children.Add(new Label { Content = "Scope:", Margin = new Thickness(8, 0, 0, 0) });
        toolbar.Children.Add(_scope);
        toolbar.Children.Add(refresh);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        _status.Margin = new Thickness(8, 4, 8, 6);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        var scroller = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _list.Margin = new Thickness(8);
        root.Children.Add(scroller);

        Content = root;

        _search.TextChanged += (_, _) => Render();
        _activeOnly.Checked += (_, _) => Render();
        _activeOnly.Unchecked += (_, _) => Render();
        _scope.SelectionChanged += async (_, _) => { _store.ScopeAll = _scope.SelectedIndex == 1; await _store.Refresh(); };
        refresh.Click += async (_, _) => await _store.Refresh();

        _store.ItemsChanged += Render;
        _store.TimesChanged += Render;
        _store.StatusChanged += s => _status.Text = s;
        Closed += (_, _) => { _store.ItemsChanged -= Render; _store.TimesChanged -= Render; };

        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        var q = _search.Text.ToLowerInvariant();
        bool activeOnly = _activeOnly.IsChecked == true;
        int shown = 0;
        foreach (var item in _store.Items)
        {
            if (activeOnly && item.IsFinal) continue;
            if (q.Length > 0 && !(item.Name.ToLowerInvariant().Contains(q) || item.Id.ToString().Contains(q))) continue;
            _list.Children.Add(Row(item));
            shown++;
        }
        _status.Text = $"{shown} shown";
    }

    private Border Row(WorkItem item)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

        // line 1: type + name + hours
        var l1 = new StackPanel { Orientation = Orientation.Horizontal };
        l1.Children.Add(new TextBlock { Text = $"[{item.DisplayType}] ", FontWeight = FontWeights.Bold });
        l1.Children.Add(new TextBlock { Text = item.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 480 });
        var total = _store.HoursFor(item.Id);
        var slots = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (total > 0)
        {
            var hbtn = new Button { Content = $"{total:0.00}h", Margin = new Thickness(8, 0, 0, 0) };
            bool open = false;
            hbtn.Click += (_, _) => { open = !open; slots.Children.Clear(); if (open) BuildSlots(slots, item.Id); };
            l1.Children.Add(hbtn);
        }
        box.Children.Add(l1);
        box.Children.Add(slots);

        // line 2: links + state + project + sprint
        var l2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var idLink = new Button { Content = $"#{item.Id} ↗", Margin = new Thickness(0, 0, 6, 0) };
        idLink.Click += (_, _) => _store.OpenInTp(item.Id);
        l2.Children.Add(idLink);
        if (item.UsId != 0)
        {
            var us = new Button { Content = $"US #{item.UsId} ↗", ToolTip = item.UsName, Margin = new Thickness(0, 0, 6, 0) };
            us.Click += (_, _) => _store.OpenInTp(item.UsId);
            l2.Children.Add(us);
        }
        var stateCombo = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 6, 0) };
        stateCombo.Items.Add(item.StateName); stateCombo.SelectedIndex = 0;
        _ = PopulateStates(stateCombo, item);
        l2.Children.Add(stateCombo);
        l2.Children.Add(new TextBlock { Text = item.ProjectName, Margin = new Thickness(0, 0, 6, 0), Foreground = System.Windows.Media.Brushes.Gray });
        if (item.Sprint.Length > 0) l2.Children.Add(new TextBlock { Text = item.Sprint });
        box.Children.Add(l2);

        // line 3: direct log
        var l3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var hrs = new TextBox { Width = 50 };
        var date = new TextBox { Width = 110, Text = DateTime.Now.ToString("yyyy-MM-dd") };
        var note = new TextBox { Width = 300 };
        var log = new Button { Content = "Log" };
        log.Click += async (_, _) =>
        {
            if (!double.TryParse(hrs.Text.Replace(',', '.'), out var h) || h <= 0) return;
            var d = DateOnly.TryParse(date.Text, out var dd) ? dd : DateOnly.FromDateTime(DateTime.Now);
            await _store.LogTime(item.Id, h, note.Text, d);
            hrs.Text = ""; note.Text = "";
        };
        l3.Children.Add(new TextBlock { Text = "hrs:", VerticalAlignment = VerticalAlignment.Center });
        l3.Children.Add(hrs); l3.Children.Add(date); l3.Children.Add(note); l3.Children.Add(log);
        box.Children.Add(l3);

        return new Border { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 6), Child = box };
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
            }
        };
    }

    private void BuildSlots(StackPanel holder, long itemId)
    {
        var slots = _store.Times.Where(t => t.ItemId == itemId).OrderByDescending(t => t.Day).ToList();
        holder.Children.Add(new TextBlock { Text = $"Time entries — total {slots.Sum(t => t.Hours):0.00}h", Foreground = System.Windows.Media.Brushes.Gray });
        foreach (var e in slots)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            r.Children.Add(new TextBlock { Text = e.Day, Width = 90, VerticalAlignment = VerticalAlignment.Center });
            var hrs = new TextBox { Width = 50, Text = e.Hours.ToString() };
            var note = new TextBox { Width = 280, Text = e.Description };
            var save = new Button { Content = "Save" };
            save.Click += async (_, _) =>
            {
                if (double.TryParse(hrs.Text.Replace(',', '.'), out var h) && h > 0)
                    await _store.UpdateTime(e.Id, h, note.Text, e.Day);
            };
            var del = new Button { Content = "✕", Margin = new Thickness(4, 0, 0, 0) };
            del.Click += async (_, _) =>
            {
                if (MessageBox.Show($"Delete this time entry?\n{e.Day} · {e.Hours:0.00}h", "Delete", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                    await _store.DeleteTime(e.Id);
            };
            r.Children.Add(hrs); r.Children.Add(note); r.Children.Add(save); r.Children.Add(del);
            holder.Children.Add(r);
        }
    }
}
