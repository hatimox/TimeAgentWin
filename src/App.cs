using System.Drawing;
using System.Reflection;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace TimeAgent;

/// Entry point + system-tray (NotifyIcon) host. WPF app with no main window;
/// windows are created on demand. The tray menu carries Split/Stop during a
/// meeting, plus Open tasks / Settings / Refresh / Quit.
public class App : Application
{
    /// App version shown in the UI. Comes from the assembly's
    /// InformationalVersion, which the CI sets from the git tag
    /// (`dotnet publish -p:Version=<tag>`); falls back to the csproj <Version>.
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+');                 // strip SourceLink build metadata (e.g. 1.2.3+abc)
            return plus >= 0 ? info[..plus] : info;
        }
        var v = asm.GetName().Version;
        return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
    }

    private WinForms.NotifyIcon _tray = null!;
    private System.Drawing.Icon _appIcon = null!;
    private AppStore _store = null!;
    private TasksWindow? _tasks;
    private SettingsWindow? _settings;
    private TrayPopup? _popup;

    // ---- single-instance enforcement ----
    private const string MutexName = "TimeAgent-SingleInstance-B64DF37A-A682-4350-A77D-CF8F0A9AB499";
    private const string ShowEventName = "TimeAgent-Show-B64DF37A-A682-4350-A77D-CF8F0A9AB499";
    private static Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    [STAThread]
    public static void Main()
    {
        // Single instance: the first launch owns the mutex; any later launch
        // signals the running instance to surface its window, then exits.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try { if (EventWaitHandle.TryOpenExisting(ShowEventName, out var h)) { h.Set(); h.Dispose(); } }
            catch { /* running instance may be mid-shutdown */ }
            return;
        }

        var app = new App();
        app.Run();
        GC.KeepAlive(_mutex);
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

        // Listen for "show" signals from launches that were blocked as duplicates.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => Dispatcher.Invoke(OpenTasks), null, Timeout.Infinite, executeOnlyOnce: false);

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
        if (_tasks.WindowState == WindowState.Minimized) _tasks.WindowState = WindowState.Normal;
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
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _appIcon.Dispose();
        _mutex?.Dispose();
        Shutdown();
    }
}
