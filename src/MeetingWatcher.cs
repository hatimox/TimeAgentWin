namespace TimeAgent;

/// Native meeting detection loop. Uses an async Task loop (not a fragile JS
/// timer); polls WASAPI in-process. Fully guarded so the loop self-heals.
public class MeetingWatcher
{
    public event Action<bool>? MeetingStateChanged;          // in_meeting
    public event Action<DateTime, DateTime>? MeetingEnded;   // start, end (logs needed)

    public bool InMeeting { get; private set; }
    public DateTime? SessionStart { get; private set; }

    private DateTime? _lastSeen;
    private bool _suppressed;
    private DateTime? _suppressedAt;
    private bool _busy;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    private const int MinSeconds = 60;
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Active = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SuppressMax = TimeSpan.FromSeconds(90);

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunLoop(_cts.Token);
    }
    public void Stop() => _cts?.Cancel();

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool inMeeting;
            lock (_lock) inMeeting = SessionStart != null;
            try { await Task.Delay(inMeeting ? Active : Idle, ct); }
            catch (TaskCanceledException) { break; }

            bool active;
            try { active = await Task.Run(MicMonitor.InUse, ct); }
            catch { active = false; }

            try { Poll(active); } catch { /* never let the loop die */ }
        }
    }

    private void Poll(bool active)
    {
        var now = DateTime.UtcNow;
        bool fireEnded = false;
        DateTime endedStart = default, endedEnd = default;
        bool? stateChange = null;

        lock (_lock)
        {
            if (_busy) return;

            if (_suppressed)
            {
                var expired = _suppressedAt is { } a && now - a > SuppressMax;
                if (!active || expired) { _suppressed = false; _suppressedAt = null; }
                else { stateChange = false; goto emit; }
            }

            if (active)
            {
                if (SessionStart == null) { SessionStart = now; InMeeting = true; stateChange = true; }
                _lastSeen = now;
            }
            else if (SessionStart is { } start)
            {
                var seen = _lastSeen ?? start;
                SessionStart = null; _lastSeen = null; InMeeting = false; stateChange = false;
                var end = (now - seen) > Idle * 3 ? seen : now;
                if ((end - start).TotalSeconds >= MinSeconds)
                {
                    _busy = true; fireEnded = true; endedStart = start; endedEnd = end;
                }
            }
        }

    emit:
        if (stateChange is { } st) MeetingStateChanged?.Invoke(st);
        if (fireEnded) MeetingEnded?.Invoke(endedStart, endedEnd);
    }

    public void ClearBusy() { lock (_lock) _busy = false; }

    public void SplitNow() => EndManual(suppress: false);
    public void StopTracking() => EndManual(suppress: true);

    private void EndManual(bool suppress)
    {
        var now = DateTime.UtcNow;
        bool fire = false; DateTime s = default;
        lock (_lock)
        {
            if (SessionStart is { } start && !_busy)
            {
                SessionStart = null; _lastSeen = null; InMeeting = false;
                if (suppress) { _suppressed = true; _suppressedAt = now; }
                if ((now - start).TotalSeconds >= MinSeconds) { _busy = true; fire = true; s = start; }
            }
        }
        MeetingStateChanged?.Invoke(false);
        if (fire) MeetingEnded?.Invoke(s, now);
    }
}
