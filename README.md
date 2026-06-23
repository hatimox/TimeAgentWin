# TimeAgent (Windows native)

A **native Windows (C# / .NET 8 / WPF)** rewrite of TimeAgent — a system-tray app
that logs meeting and task time to **TargetProcess**. Native to avoid the Electron
fragility: in particular the original spawned a **PowerShell subprocess every poll**
to read mic state, which is exactly the kind of thing that wedged. Here mic
detection is an **in-process WASAPI/MMDevice query** (via NAudio).

Reuses the same config location as the Electron app
(`%APPDATA%\TimeAgent\settings.json`); the API token is stored encrypted with
**DPAPI** (per-user) rather than the Electron Credential-Vault entry, so on first
run you re-enter the token once.

## Build (on Windows)

Requires the **.NET 8 SDK** (Windows). Then:

```powershell
dotnet build -c Release
dotnet run                # or run the built exe in bin\Release\net8.0-windows\
```

The tray icon appears in the notification area. Left-click opens Tasks;
right-click for the menu (Split/Stop during a meeting, Open tasks, Settings,
Refresh, Quit).

## Module map — all features written

| File | Role |
|------|------|
| `src/Models.cs`         | data types |
| `src/Settings.cs`       | JSON config + DPAPI-encrypted token |
| `src/TpClient.cs`       | async TP REST client; noon-anchored dates, pagination, status change, time edit/delete |
| `src/MicMonitor.cs`     | in-process WASAPI mic detection (no PowerShell spawn) |
| `src/MeetingWatcher.cs` | async meeting loop + Split / Stop-tracking / bounded suppression |
| `src/Holidays.cs`       | Morocco civil holidays + day-off check |
| `src/AppStore.cs`       | shared state, all async TP ops, recurring auto-log, dispatcher marshalling |
| `src/TasksWindow.cs`    | tasks/bugs window: search, filter, scope, status combo, US link, hours, direct log, edit/delete entries |
| `src/SettingsWindow.cs` | settings tabs: account, meetings, recurring, days off |
| `src/MeetingPrompt.cs`  | end-of-meeting dialog + task picker + defined-meeting picker |
| `src/TrayPopup.cs`      | rich left-click tray popover (avatar, live meeting timer, Split/Stop, Today/Week cards, month navigator) — parity with the macOS/Linux ports |
| `src/App.cs`            | WinForms NotifyIcon tray + WPF app host |

## IMPORTANT: unverified — build on Windows first

Every feature is written, but **none of this was compiled** — it was authored on
macOS, which has no Windows .NET/WPF toolchain. Expect `dotnet build` to surface
errors to fix. Highest-risk spots, in order:

1. **`MicMonitor.cs`** — the "active capture session = mic in use" heuristic via
   NAudio's `AudioSessionManager` needs validation against a real call. It may
   over- or under-report depending on how sessions are exposed; tune by also
   checking the session's process / peak meter if needed. Most likely first
   thing to adjust.
2. **WPF + WinForms NotifyIcon mix** — `UseWindowsForms` + `UseWPF` together
   compiles, but confirm the tray `ContextMenuStrip` + dispatcher marshalling
   behaves. (NotifyIcon is WinForms; the windows are WPF.)
3. **Timezone IDs** — settings store IANA names (e.g. `Africa/Casablanca`);
   .NET 6+ accepts these on Windows via ICU, with a fallback to local. Verify on
   the target machine.
4. **Async-void event handlers** — the UI uses `async (_, _) =>` handlers; fine
   for fire-and-forget but exceptions surface only via Status. OK for v1.

Validate after it builds: `MicMonitor.InUse()` true in a real call; logging
round-trips to TP; status change and time edit/delete work; the meeting prompt
appears; Split/Stop show in the tray menu during a call.

Siblings: `../TimeAgentMac` (compiles), `../TimeAgentLinux` (unverified Rust),
and the original `../TimeAgentElectron` — reference for exact parity.
