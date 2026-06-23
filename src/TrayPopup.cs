using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static TimeAgent.Ui;

namespace TimeAgent;

/// Rich tray popover (parity with the macOS/Linux ports): avatar + live meeting
/// timer, Split/Stop, TODAY/WEEK hour cards, a month navigator, and the action
/// buttons. A borderless WPF window anchored bottom-right near the notification
/// area; dismisses on deactivate. Left-clicking the NotifyIcon toggles it.
public class TrayPopup : Window
{
    private readonly AppStore _store;
    private readonly Action _openTasks, _openSettings, _refresh, _quit;
    private readonly DispatcherTimer _ticker;

    // displayed month for the navigator (first of month)
    private DateOnly _month = FirstOfThisMonth();

    // dynamic bits updated by the ticker / TimesChanged
    private TextBlock _statusText = null!, _todayVal = null!, _weekVal = null!, _monthVal = null!, _monthName = null!;
    private Border _nameDot = null!, _statusDot = null!, _splitStopRow = null!;

    private DateTime _lastHidden = DateTime.MinValue;

    public TrayPopup(AppStore store, Action openTasks, Action openSettings, Action refresh, Action quit)
    {
        _store = store; _openTasks = openTasks; _openSettings = openSettings; _refresh = refresh; _quit = quit;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;

        Content = BuildContent();

        Deactivated += (_, _) => { Hide(); _lastHidden = DateTime.Now; };
        _store.TimesChanged += OnTimesChanged;

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => RefreshValues();
    }

    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        // Clicking the tray while open first triggers Deactivated (which hides);
        // swallow the immediate re-open so the click reads as "close".
        if ((DateTime.Now - _lastHidden).TotalMilliseconds < 250) return;
        _month = FirstOfThisMonth();
        RefreshValues();
        Show();
        UpdateLayout();
        PositionBottomRight();
        Activate();
        _ticker.Start();
    }

    protected override void OnDeactivated(EventArgs e) { base.OnDeactivated(e); _ticker.Stop(); }

    private void OnTimesChanged() => Dispatcher.Invoke(RefreshValues);

    private void PositionBottomRight()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 8;
        Top = wa.Bottom - ActualHeight - 8;
    }

    // ---- layout ----

    private UIElement BuildContent()
    {
        var col = new StackPanel();

        // header: avatar + name/status
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(Avatar(Initials(_store.Settings.MyUserName)));
        var who = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(_store.Settings.MyUserName) ? "TimeAgent" : _store.Settings.MyUserName, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = B("#1A1A1A") });
        _nameDot = Dot("#E5484D"); _nameDot.Margin = new Thickness(7, 0, 0, 0); _nameDot.VerticalAlignment = VerticalAlignment.Center;
        nameRow.Children.Add(_nameDot);
        who.Children.Add(nameRow);
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        _statusDot = Dot("#8A8A8A"); _statusDot.VerticalAlignment = VerticalAlignment.Center;
        _statusText = new TextBlock { FontSize = 12, Margin = new Thickness(6, 0, 0, 0), Foreground = B("#8A8A8A") };
        statusRow.Children.Add(_statusDot); statusRow.Children.Add(_statusText);
        who.Children.Add(statusRow);
        header.Children.Add(who);
        col.Children.Add(header);

        // Split / Stop (only while in a meeting)
        _splitStopRow = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = B("#FBE9E9"),
            BorderBrush = B("#F0C9C9"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 12),
        };
        var ssGrid = new Grid();
        ssGrid.ColumnDefinitions.Add(new ColumnDefinition());
        ssGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var split = Clickable(Centered("✂", "Split"), () => { _store.Watcher.SplitNow(); Hide(); }, "#00FFFFFF", "#22000000");
        var stop = Clickable(Centered("■", "Stop"), () => { _store.Watcher.StopTracking(); Hide(); }, "#00FFFFFF", "#22000000");
        Grid.SetColumn(stop, 1);
        ssGrid.Children.Add(split); ssGrid.Children.Add(stop);
        _splitStopRow.Child = ssGrid;
        col.Children.Add(_splitStopRow);

        // TODAY / WEEK cards
        var cards = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        var todayCard = Card("☀", "TODAY", "#FCEFE0", out _todayVal);
        var weekCard = Card("\U0001F4C5", "WEEK", "#E8ECF4", out _weekVal);
        Grid.SetColumn(todayCard, 0); Grid.SetColumn(weekCard, 2);
        cards.Children.Add(todayCard); cards.Children.Add(weekCard);
        col.Children.Add(cards);

        // month navigator
        var nav = new Border { CornerRadius = new CornerRadius(10), Background = B("#ECE8E5"), Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(0, 0, 0, 12) };
        var navGrid = new Grid();
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition());
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var prev = Clickable(new TextBlock { Text = "‹", FontSize = 18, Foreground = B("#555"), HorizontalAlignment = HorizontalAlignment.Center }, () => { _month = _month.AddMonths(-1); RefreshValues(); }, "#00FFFFFF", "#22000000", 8, new Thickness(12, 2, 12, 2));
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        _monthVal = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#1A1A1A") };
        _monthName = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("#8A8A8A") };
        center.Children.Add(_monthVal); center.Children.Add(_monthName);
        var next = Clickable(new TextBlock { Text = "›", FontSize = 18, Foreground = B("#555"), HorizontalAlignment = HorizontalAlignment.Center }, () => { _month = _month.AddMonths(1); RefreshValues(); }, "#00FFFFFF", "#22000000", 8, new Thickness(12, 2, 12, 2));
        Grid.SetColumn(prev, 0); Grid.SetColumn(center, 1); Grid.SetColumn(next, 2);
        navGrid.Children.Add(prev); navGrid.Children.Add(center); navGrid.Children.Add(next);
        nav.Child = navGrid;
        col.Children.Add(nav);

        // open tasks
        col.Children.Add(Clickable(LeftRow("\U0001F4CB", "Open tasks…"), () => { Hide(); _openTasks(); }, "#F1ECE8", "#E4DDD7", 8, new Thickness(12, 10, 12, 10)));

        // settings / refresh / quit
        var actions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition());
        var settings = Clickable(Centered("⚙", "Settings"), () => { Hide(); _openSettings(); }, "#F1ECE8", "#E4DDD7");
        var refresh = Clickable(Centered("⟳", "Refresh"), () => _refresh(), "#F1ECE8", "#E4DDD7");
        var quit = Clickable(Centered("⏻", "Quit"), () => _quit(), "#F1ECE8", "#E4DDD7");
        settings.Margin = new Thickness(0, 0, 4, 0); refresh.Margin = new Thickness(4, 0, 4, 0); quit.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(settings, 0); Grid.SetColumn(refresh, 1); Grid.SetColumn(quit, 2);
        actions.Children.Add(settings); actions.Children.Add(refresh); actions.Children.Add(quit);
        col.Children.Add(actions);

        // footer
        col.Children.Add(new TextBlock { Text = $"TimeAgent v{App.Version}", FontSize = 11, Foreground = B("#B5ADA6"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });

        // card holder with shadow; transparent margin gives the shadow room
        return new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Background = B("#FBF6F2"),
            Padding = new Thickness(16),
            Width = 340,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.25, Color = Colors.Black },
            Child = col,
        };
    }

    // ---- dynamic refresh ----

    private void RefreshValues()
    {
        bool inMeeting = _store.Watcher.InMeeting;
        _splitStopRow.Visibility = inMeeting ? Visibility.Visible : Visibility.Collapsed;
        _nameDot.Visibility = inMeeting ? Visibility.Visible : Visibility.Collapsed;
        if (inMeeting && _store.Watcher.SessionStart is { } start)
        {
            var ts = DateTime.UtcNow - start;
            _statusDot.Background = B("#E5484D");
            _statusText.Foreground = B("#E5484D");
            _statusText.Text = $"In meeting · {(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        else if (_store.ActiveTimerStart is { } tstart)
        {
            var ts = DateTime.Now - tstart;
            _statusDot.Background = B("#3B82F6");
            _statusText.Foreground = B("#3B82F6");
            _statusText.Text = $"Tracking · {(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        else
        {
            _statusDot.Background = B("#3FB870");
            _statusText.Foreground = B("#8A8A8A");
            _statusText.Text = "Idle";
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        int delta = ((int)today.DayOfWeek + 6) % 7;     // Monday = start of week
        var weekStart = today.AddDays(-delta);
        var weekEnd = weekStart.AddDays(6);

        _todayVal.Text = $"{Sum(d => d == today):0.00}h";
        _weekVal.Text = $"{Sum(d => d >= weekStart && d <= weekEnd):0.00}h";
        _monthVal.Text = $"{Sum(d => d.Year == _month.Year && d.Month == _month.Month):0.00}h";
        _monthName.Text = _month.ToString("MMMM yyyy");

        if (IsVisible) { UpdateLayout(); PositionBottomRight(); }
    }

    private double Sum(Func<DateOnly, bool> pred) =>
        _store.Times.Where(t => DateOnly.TryParse(t.Day, out var d) && pred(d)).Sum(t => t.Hours);

    // ---- small builders ----

    private static DateOnly FirstOfThisMonth() { var n = DateTime.Now; return new DateOnly(n.Year, n.Month, 1); }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var s = parts[0][..1] + (parts.Length > 1 ? parts[^1][..1] : "");
        return s.ToUpperInvariant();
    }

    private static Border Avatar(string initials) => new()
    {
        Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = B("#3FB870"),
        Child = new TextBlock { Text = initials, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    private static Border Dot(string hex) => new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = B(hex) };

    private static Border Card(string glyph, string label, string bg, out TextBlock value)
    {
        var p = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        top.Children.Add(new TextBlock { Text = glyph, FontSize = 12, Margin = new Thickness(0, 0, 5, 0) });
        top.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B("#8A8A8A"), VerticalAlignment = VerticalAlignment.Center });
        value = new TextBlock { Text = "0.00h", FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Foreground = B("#1A1A1A") };
        p.Children.Add(top); p.Children.Add(value);
        return new Border { CornerRadius = new CornerRadius(10), Background = B(bg), Padding = new Thickness(10, 12, 10, 12), Child = p };
    }

    private static UIElement Centered(string glyph, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = glyph, FontSize = 13, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#1A1A1A") });
        return sp;
    }

    private static UIElement LeftRow(string glyph, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = glyph, FontSize = 13, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#1A1A1A") });
        return sp;
    }

}
