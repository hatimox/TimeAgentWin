using System.Windows;
using System.Windows.Controls;
using static TimeAgent.Ui;

namespace TimeAgent;

/// Every time entry logged on one day (today by default), across all tasks -
/// editable/deletable inline, with ‹ › to browse days and a Today reset.
public class TodayWindow : Window
{
    private readonly AppStore _store;
    private readonly StackPanel _list = new();
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#1A1A1A"), Margin = new Thickness(10, 0, 10, 0) };
    private readonly TextBlock _total = new() { FontSize = 20, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#E8873A") };
    private Border _next = null!, _todayBtn = null!;

    public TodayWindow(AppStore store)
    {
        _store = store;
        Title = "TimeAgent — Entries";
        Width = 720; Height = 520;
        Background = B("#F2EEEA");
        Icon = AppIcon;

        var root = new DockPanel();
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        _list.Margin = new Thickness(12, 4, 12, 8);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        _store.TimesChanged += Render;
        _store.ItemsChanged += Render;
        Closed += (_, _) => { _store.TimesChanged -= Render; _store.ItemsChanged -= Render; };
        Render();
    }

    /// Show the given day (0 = today, -1 = yesterday...).
    public void ShowDay(int offset)
    {
        _store.ViewDayOffset = Math.Min(0, offset);
        Render();
    }

    private UIElement BuildHeader()
    {
        var dock = new DockPanel { Margin = new Thickness(14, 12, 14, 6) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Clickable(new TextBlock { Text = "‹", FontSize = 18, Foreground = B("#555") }, () => { _store.ViewDayOffset--; Render(); }, "#00FFFFFF", "#22000000", 6, new Thickness(10, 0, 10, 2)));
        _next = Clickable(new TextBlock { Text = "›", FontSize = 18, Foreground = B("#555") }, () => { if (_store.ViewDayOffset < 0) { _store.ViewDayOffset++; Render(); } }, "#00FFFFFF", "#22000000", 6, new Thickness(10, 0, 10, 2));
        left.Children.Add(_next);
        left.Children.Add(_title);
        _todayBtn = Clickable(new TextBlock { Text = "Today", FontSize = 12, Foreground = B("#3B82F6"), FontWeight = FontWeights.SemiBold }, () => { _store.ViewDayOffset = 0; Render(); }, "#E8EEFB", "#D9E4FA", 6, new Thickness(10, 4, 10, 4));
        left.Children.Add(_todayBtn);
        DockPanel.SetDock(_total, Dock.Right);
        dock.Children.Add(_total);
        dock.Children.Add(left);
        return dock;
    }

    private void Render()
    {
        int off = _store.Settings.TzOffsetMinutes();
        int offset = _store.ViewDayOffset;
        var dayKey = Totals.Key(Totals.DayAt(offset, off));
        var entries = _store.Times.Where(t => t.Day == dayKey).OrderByDescending(t => t.Id).ToList();

        _title.Text = Totals.LongDayLabel(offset, off);
        _next.Opacity = offset < 0 ? 1 : 0.25;
        _todayBtn.Visibility = offset != 0 ? Visibility.Visible : Visibility.Collapsed;
        var dayDate = Totals.DayAt(offset, off);
        SetTarget(_total, entries.Sum(t => t.Hours), Totals.DayTarget(_store.Settings, dayDate), 20);

        _list.Children.Clear();
        if (entries.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = offset == 0 ? "No time logged today" : "No time logged on this day", Foreground = B("#8A8A8A"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0) });
            return;
        }
        foreach (var e in entries) _list.Children.Add(EntryRow(e));
    }

    private Border EntryRow(TimeEntry e)
    {
        var item = _store.Items.FirstOrDefault(i => i.Id == e.ItemId);
        var name = item != null ? $"#{item.Id} — {item.Name}" : $"#{e.ItemId}";

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 4; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var link = new TextBlock { Text = name, Foreground = B("#3B82F6"), FontSize = 13, Cursor = System.Windows.Input.Cursors.Hand, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Open in TargetProcess" };
        link.MouseLeftButtonUp += (_, _) => _store.OpenInTp(e.ItemId);

        var hrs = new TextBox { Width = 52, Text = e.Hours.ToString("0.##"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var note = new TextBox { Width = 200, Text = e.Description, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var save = Clickable(new TextBlock { Text = "✓", Foreground = B("#FFFFFF"), FontSize = 13, FontWeight = FontWeights.Bold }, async () =>
        {
            if (double.TryParse(hrs.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0)
                await _store.UpdateTime(e.Id, h, note.Text, e.Day);
        }, "#3FB870", "#35A062", 7, new Thickness(10, 4, 10, 4));
        save.Margin = new Thickness(6, 0, 0, 0); save.ToolTip = "Save";
        var del = Clickable(new TextBlock { Text = "✕", Foreground = B("#FFFFFF"), FontSize = 12 }, async () =>
        {
            if (MessageBox.Show(this, $"{name}\n{e.Day} · {e.Hours:0.00}h", "Delete this time entry?", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                await _store.DeleteTime(e.Id);
        }, "#E5484D", "#D33B40", 7, new Thickness(10, 4, 10, 4));
        del.Margin = new Thickness(6, 0, 0, 0); del.ToolTip = "Delete";

        Grid.SetColumn(link, 0); Grid.SetColumn(hrs, 1); Grid.SetColumn(note, 2); Grid.SetColumn(save, 3); Grid.SetColumn(del, 4);
        row.Children.Add(link); row.Children.Add(hrs); row.Children.Add(note); row.Children.Add(save); row.Children.Add(del);

        return new Border { Background = B("#FFFFFF"), BorderBrush = B("#E7E2DD"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8), Child = row };
    }
}
