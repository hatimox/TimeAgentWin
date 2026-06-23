; Inno Setup script for TimeAgent — wraps the published self-contained exe into
; a per-user installer (no admin prompt) with Start-Menu shortcut, optional
; desktop shortcut, optional run-at-sign-in, and an uninstaller.
;
; Build (after `dotnet publish ... -o publish`):
;   ISCC.exe /DAppVersion=1.2.3 installer\TimeAgent.iss
; Output: installer\TimeAgent-Setup-<version>.exe

#define AppName "TimeAgent"
#define AppPublisher "TimeAgent"
#define AppExe "TimeAgent.exe"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\TimeAgent.exe"
#endif

[Setup]
; Stable AppId so upgrades replace the previous version instead of installing twice.
AppId={{B64DF37A-A682-4350-A77D-CF8F0A9AB499}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
WizardStyle=modern
; Per-user install — no UAC prompt, lands in %LocalAppData%\Programs\TimeAgent.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
OutputDir=.
OutputBaseFilename=TimeAgent-Setup-{#AppVersion}
SetupIconFile=..\appicon.ico
; Use the Restart Manager to close a running TimeAgent during install/upgrade.
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start {#AppName} automatically when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Run-at-sign-in (per-user). Removed automatically on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
