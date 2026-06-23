using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace TimeAgent;

/// Microphone-in-use detection (in-process — no PowerShell subprocess; that
/// per-poll spawn was the Electron Windows freeze source).
///
/// Primary signal is the Windows CapabilityAccessManager "ConsentStore" — the
/// same per-app usage ledger that drives the OS privacy "mic in use" indicator.
/// While an app is actively capturing, the OS keeps that app's
/// LastUsedTimeStop == 0; when capture stops it stamps a real FILETIME. This is
/// authoritative and avoids the WASAPI false-positives (a capture session can
/// read AudioSessionStateActive merely because an app holds the endpoint open).
/// The Electron app read this exact key — just via a PowerShell subprocess; we
/// read it directly, so behavior matches.
///
/// If the ConsentStore isn't present (pre-1903 Windows), fall back to a WASAPI
/// active-capture-session probe.
public static class MicMonitor
{
    private const string ConsentPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    public static bool InUse()
    {
        var consent = ConsentInUse();
        return consent ?? WasapiInUse();
    }

    /// null = ConsentStore unavailable; otherwise the authoritative answer.
    private static bool? ConsentInUse()
    {
        bool sawStore = false;
        // HKCU covers desktop + Store apps for the current user; HKLM catches
        // apps capturing from another session or running as a service.
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var root = hive.OpenSubKey(ConsentPath);
                if (root == null) continue;
                sawStore = true;
                if (AnyActive(root)) return true;
            }
            catch { /* access denied / missing -> ignore this hive */ }
        }
        return sawStore ? false : null;
    }

    /// Direct subkeys are packaged (Store) apps; the "NonPackaged" subkey holds
    /// desktop apps (Zoom, Teams, browsers). An app is *currently* capturing
    /// when its LastUsedTimeStop == 0.
    private static bool AnyActive(RegistryKey store)
    {
        foreach (var name in store.GetSubKeyNames())
        {
            try
            {
                using var k = store.OpenSubKey(name);
                if (k == null) continue;
                if (string.Equals(name, "NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    if (AnyActive(k)) return true;
                    continue;
                }
                if (k.GetValue("LastUsedTimeStop") is long stop && stop == 0) return true;
            }
            catch { /* key may vanish mid-enumeration */ }
        }
        return false;
    }

    private static bool WasapiInUse()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                        if (sessions[i].State == AudioSessionState.AudioSessionStateActive)
                            return true;
                }
                catch { /* device may vanish mid-enumeration */ }
                finally { device.Dispose(); }
            }
        }
        catch { /* WASAPI unavailable -> treat as not in use */ }
        return false;
    }
}
