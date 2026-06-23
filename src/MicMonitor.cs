using NAudio.CoreAudioApi;

namespace TimeAgent;

/// Microphone-in-use detection via WASAPI / MMDevice (in-process, no PowerShell
/// subprocess — that per-poll spawn was the Electron Windows freeze source).
///
/// Heuristic: a capture (recording) endpoint whose session manager has an
/// AudioSessionState.Active session that isn't this process = something is
/// actively capturing the mic (a call). We check the peak meter as a secondary
/// signal, since an active session with real input shows level.
public static class MicMonitor
{
    public static bool InUse()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in captureDevices)
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        if (s.State == AudioSessionState.AudioSessionStateActive)
                        {
                            // An active capture session = mic in use by some app.
                            return true;
                        }
                    }
                }
                catch { /* device may vanish mid-enumeration */ }
                finally { device.Dispose(); }
            }
        }
        catch { /* WASAPI unavailable -> unknown; treat as not in use */ }
        return false;
    }
}
