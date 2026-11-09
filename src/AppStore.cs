using System.IO;
using System.Text.Json;
using System.Windows;

namespace TimeAgent;

/// App-wide state + TP orchestration. Async ops run on the thread pool; UI
/// updates are marshalled back onto the WPF Dispatcher via the events below.
public class AppStore
{
    public Settings Settings { get; private set; }
    public TpClient? Client { get; private set; }
    public List<WorkItem> Items { get; private set; } = new();
    public List<TimeEntry> Times { get; private set; } = new();
    public Dictionary<long, Dictionary<string, List<WorkflowState>>> States { get; } = new();
    public bool ScopeAll { get; set; }
    public string Status { get; private set; } = "";
    /// Day shown in the day-entries window (0 = today, -1 = yesterday...).
    public int ViewDayOffset { get; set; }

    public const string DescriptionRequiredMessage = "TargetProcess requires a description for time entries.";

    public event Action? ItemsChanged;
    public event Action? TimesChanged;
    public event Action<string>? StatusChanged;
    public event Action? TimerChanged;

    // ---- manual per-task time tracking (Start/Stop stopwatch) ----
    public long ActiveTimerTaskId { get; private set; }
    public DateTime? ActiveTimerStart { get; private set; }   // local clock

    public readonly MeetingWatcher Watcher = new();

    public AppStore()
    {
        Settings = Settings.Load();
        RebuildClient();
    }

    public void RebuildClient() =>
        Client = Settings.IsConfigured ? new TpClient(Settings.TpUrl, Settings.Token, Settings.MyUserId) : null;

    private void Dispatch(Action a) => Application.Current?.Dispatcher.Invoke(a);
    public void NoteStatus(string s) => SetStatus(s);
    private void SetStatus(string s) { Status = s; Dispatch(() => StatusChanged?.Invoke(s)); }

    public async Task EnsureUser()
    {
        if (Client == null || (Settings.MyUserId != 0 && Settings.MyUserName.Length > 0)) return;
        try
        {
            var (id, name, email) = await Client.WhoAmI();
            Settings.MyUserId = id; Settings.MyUserName = name; Settings.MyUserEmail = email;
            Settings.Save();
            Client.MyUserId = id;
        }
        catch { }
    }

    public async Task Refresh()
    {
        if (Client == null) return;
        SetStatus("Loading…");
        try
        {
            var itemsTask = Client.FetchAllAssigned(!ScopeAll);
            var timesTask = Client.FetchMyTimes();
            await Task.WhenAll(itemsTask, timesTask);
            Items = itemsTask.Result; Times = timesTask.Result;
            Dispatch(() => { ItemsChanged?.Invoke(); TimesChanged?.Invoke(); });
            SetStatus($"Loaded {Items.Count} items{(ScopeAll ? "" : " (current sprint)")}");
        }
        catch (Exception e) { SetStatus($"Load failed: {e.Message}"); }
    }

    public double HoursFor(long itemId) => Times.Where(t => t.ItemId == itemId).Sum(t => t.Hours);

    /// Log a time entry. Returns null on success, or the error to show - callers
    /// keep their form open until this returns null.
    public async Task<string?> LogTime(long entityId, double hours, string description, DateOnly date)
    {
        if (Client == null) { SetStatus("Not configured"); return "TargetProcess is not configured"; }
        var desc = description.Trim();
        if (Settings.TimeDescriptionRequired && desc.Length == 0) return DescriptionRequiredMessage;
        try
        {
            await Client.LogTime(entityId, hours, desc, date, Settings.TzOffsetMinutes());
            SetStatus($"Logged {hours:0.00}h to #{entityId}");
            await ReloadTimes();
            return null;
        }
        catch (Exception e)
        {
            var m = NoteDescriptionRequirement(e) ? DescriptionRequiredMessage : e.Message;
            SetStatus($"Log failed: {m}");
            return m;
        }
    }

    /// If TP rejected a time entry for a missing description, remember that (so
    /// every form asks for one from now on) and return true.
    public bool NoteDescriptionRequirement(Exception e)
    {
        if (e is not TpException { Status: 400 } tp || tp.Message.IndexOf("description", StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (!Settings.TimeDescriptionRequired) { Settings.TimeDescriptionRequired = true; Settings.Save(); }
        return true;
    }

    public async Task UpdateTime(long timeId, double hours, string description, string day)
    {
        if (Client == null) return;
        DateOnly? date = DateOnly.TryParse(day, out var d) ? d : null;
        try { await Client.UpdateTime(timeId, hours, description, date, Settings.TzOffsetMinutes()); SetStatus("Time entry updated"); await ReloadTimes(); }
        catch (Exception e) { SetStatus($"Save failed: {(NoteDescriptionRequirement(e) ? DescriptionRequiredMessage : e.Message)}"); }
    }

    public async Task DeleteTime(long timeId)
    {
        if (Client == null) return;
        try { await Client.DeleteTime(timeId); SetStatus("Time entry deleted"); await ReloadTimes(); }
        catch (Exception e) { SetStatus($"Delete failed: {e.Message}"); }
    }

    private async Task ReloadTimes()
    {
        if (Client == null) return;
        try { Times = await Client.FetchMyTimes(); Dispatch(() => TimesChanged?.Invoke()); } catch { }
    }

    public async Task<List<WorkflowState>> StatesFor(WorkItem item)
    {
        var key = item.StateKey;
        if (States.TryGetValue(item.ProcessId, out var byproc) && byproc.TryGetValue(key, out var v)) return v;
        if (Client == null) return new();
        try
        {
            var map = await Client.FetchProcessStates(item.ProcessId);
            States[item.ProcessId] = map;
            return map.TryGetValue(key, out var list) ? list : new();
        }
        catch { return new(); }
    }

    public async Task ChangeState(WorkItem item, WorkflowState state)
    {
        if (Client == null) return;
        try
        {
            var (name, isFinal) = await Client.SetEntityState(item.EntityType, item.Id, state.Id);
            var it = Items.FirstOrDefault(i => i.Id == item.Id);
            if (it != null) { it.StateId = state.Id; it.StateName = name; it.IsFinal = isFinal; }
            Dispatch(() => ItemsChanged?.Invoke());
            SetStatus($"#{item.Id} → {name}");
        }
        catch (Exception e) { SetStatus($"Status change failed: {e.Message}"); }
    }

    /// Begin (or switch) the manual stopwatch for a task. Starting a different
    /// task while one runs stops + logs the previous one first.
    public async Task StartTaskTimer(long taskId)
    {
        if (ActiveTimerStart != null && ActiveTimerTaskId != taskId) await StopTaskTimer();
        ActiveTimerTaskId = taskId;
        ActiveTimerStart = DateTime.Now;
        Dispatch(() => TimerChanged?.Invoke());
    }

    /// Stop the manual stopwatch and log the elapsed time to its task.
    public async Task StopTaskTimer()
    {
        if (ActiveTimerStart is not { } start || ActiveTimerTaskId == 0) return;
        var taskId = ActiveTimerTaskId;
        var hours = Math.Round((DateTime.Now - start).TotalHours, 2);
        ActiveTimerTaskId = 0; ActiveTimerStart = null;
        Dispatch(() => TimerChanged?.Invoke());
        if (hours < 0.01) hours = 0.01;                 // never log a zero
        var date = DateOnly.FromDateTime(start);
        // When a description is required, or the save fails, a dialog (prefilled
        // with the task name) stays up until it saves or is explicitly discarded.
        string? firstError = null;
        if (!Settings.TimeDescriptionRequired)
        {
            firstError = await LogTime(taskId, hours, "", date);
            if (firstError == null) return;
        }
        var name = Items.FirstOrDefault(i => i.Id == taskId)?.Name ?? $"#{taskId}";
        LogPrompt.Run(this, $"Log {hours:0.00}h to “{name}”", hours, name, firstError,
            (h, d) => LogTime(taskId, h, d, date));
    }

    public double BillableHours(double raw)
    {
        var step = Math.Max(1, Settings.MeetingStepMinutes) / 60.0;
        var minH = Math.Max(0, Settings.MeetingMinMinutes) / 60.0;
        return Math.Max(minH, Math.Ceiling(raw / step) * step);
    }

    public void OpenInTp(long id)
    {
        var url = $"{Settings.TpUrl.TrimEnd('/')}/entity/{id}";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ---- recurring auto-log (skips days off) ----
    public async Task LogRecurringIfDue()
    {
        if (Client == null) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (Holidays.IsDayOff(Settings, today)) return;
        var marker = Path.Combine(Settings.Dir, "recurring_logged.json");
        Dictionary<string, bool> logged = new();
        try { if (File.Exists(marker)) logged = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(marker)) ?? new(); } catch { }
        var dayKey = today.ToString("yyyy-MM-dd");
        bool changed = false;
        foreach (var r in Settings.Recurring)
        {
            if (r.TaskId == 0) continue;
            var k = $"{dayKey}|{r.Id}";
            if (logged.TryGetValue(k, out var done) && done) continue;
            logged[k] = true; changed = true;
            // TP may require a description: use the label, else the task name.
            var desc = !string.IsNullOrWhiteSpace(r.Label) ? r.Label
                : Items.FirstOrDefault(i => i.Id == r.TaskId)?.Name ?? "Recurring time";
            try { await Client.LogTime(r.TaskId, r.Hours, desc, today, Settings.TzOffsetMinutes()); }
            catch (Exception e) { NoteDescriptionRequirement(e); }
        }
        if (changed) { try { File.WriteAllText(marker, JsonSerializer.Serialize(logged)); } catch { } await ReloadTimes(); }
    }
}
