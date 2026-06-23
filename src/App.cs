using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace TimeAgent;

/// Entry point + system-tray (NotifyIcon) host. WPF app with no main window;
/// windows are created on demand. The tray menu carries Split/Stop during a
/// meeting, plus Open tasks / Settings / Refresh / Quit.
public class App : Application
{
    public const string Version = "0.0.4";

    private WinForms.NotifyIcon _tray = null!;
    private System.Drawing.Icon _appIcon = null!;
    private AppStore _store = null!;
    private TasksWindow? _tasks;
    private SettingsWindow? _settings;
    private TrayPopup? _popup;

    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app: no window keeps it alive

        _store = new AppStore();
        BuildTray();

        _store.Watcher.MeetingStateChanged += inMeeting =>
            Dispatcher.Invoke(() => { UpdateTrayIcon(inMeeting); RebuildTrayMenu(); });
        _store.Watcher.MeetingEnded += (start, end) =>
            Dispatcher.Invoke(() =>
            {
                MeetingPrompt.Present(_store, start, end);
                _store.Watcher.ClearBusy();
            });

        _store.Watcher.Start();
        _ = RunStartup();
    }

    private async Task RunStartup()
    {
        await _store.EnsureUser();
        await _store.Refresh();
        await _store.LogRecurringIfDue();
        if (!_store.Settings.IsConfigured) Dispatcher.Invoke(OpenSettings);
    }

    private void BuildTray()
    {
        _appIcon = LoadAppIcon();
        _tray = new WinForms.NotifyIcon
        {
            Icon = _appIcon,
            Visible = true,
            Text = "TimeAgent",
        };
        _tray.MouseClick += (_, ev) => { if (ev.Button == WinForms.MouseButtons.Left) TogglePopup(); };
        RebuildTrayMenu();
    }

    private void UpdateTrayIcon(bool inMeeting)
    {
        // Keep the branded icon; the tooltip + popup convey the meeting state.
        _tray.Icon = _appIcon;
        _tray.Text = inMeeting ? "TimeAgent — in meeting" : "TimeAgent";
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var sri = GetResourceStream(new Uri("appicon.ico", UriKind.Relative));
            if (sri != null) return new System.Drawing.Icon(sri.Stream, 32, 32);
        }
        catch { /* fall back below */ }
        return SystemIcons.Application;
    }

    private void RebuildTrayMenu()
    {
        var menu = new WinForms.ContextMenuStrip();
        if (_store.Watcher.InMeeting)
        {
            menu.Items.Add("⏹▶ Split meeting", null, (_, _) => _store.Watcher.SplitNow());
            menu.Items.Add("⏹ Stop tracking", null, (_, _) => _store.Watcher.StopTracking());
            menu.Items.Add(new WinForms.ToolStripSeparator());
        }
        menu.Items.Add("Open tasks…", null, (_, _) => OpenTasks());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Refresh", null, async (_, _) => await _store.Refresh());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        _tray.ContextMenuStrip = menu;
    }

    private void TogglePopup()
    {
        _popup ??= new TrayPopup(_store, OpenTasks, OpenSettings, () => _ = _store.Refresh(), Quit);
        _popup.Toggle();
    }

    private void OpenTasks()
    {
        if (_tasks == null || !_tasks.IsLoaded)
        {
            _tasks = new TasksWindow(_store);
            _tasks.Closed += (_, _) => _tasks = null;
        }
        _tasks.Show();
        _tasks.Activate();
    }

    private void OpenSettings()
    {
        if (_settings == null || !_settings.IsLoaded)
        {
            _settings = new SettingsWindow(_store);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.Show();
        _settings.Activate();
    }

    private void Quit()
    {
        _store.Watcher.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _appIcon.Dispose();
        Shutdown();
    }
}
