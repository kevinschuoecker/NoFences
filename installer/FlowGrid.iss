; FlowGrid installer (Inno Setup 6)
; Per-user install, no admin rights required.
; Build:  iscc /DMyAppVersion=1.0.0 installer\FlowGrid.iss
; The version is normally injected by the release pipeline from the git tag.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "FlowGrid"
#define MyAppExeName "FlowGrid.exe"
#define BinDir "..\FlowGrid\bin\Release"
#define SamplesDir "..\SampleWidgets\bin\Release"

[Setup]
AppId={{E4C7B7D2-5A0E-4F5B-9C1D-8A3F2B61D904}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=FlowGrid
AppPublisherURL=https://github.com/kevinschuoecker/NoFences
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=FlowGrid-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
; Detects a running instance via the app's single-instance mutex and asks the user to close it.
AppMutex=FlowGrid
UninstallDisplayIcon={app}\{#MyAppExeName}
MinVersion=10.0

[Tasks]
Name: "autostart"; Description: "Start {#MyAppName} with Windows"; GroupDescription: "Options:"

[Components]
Name: "core"; Description: "FlowGrid"; Types: full compact custom; Flags: fixed
Name: "samples"; Description: "Sample widgets (weather, stocks, system monitor, Jira, uptime)"; Types: full

[Files]
Source: "{#BinDir}\FlowGrid.exe"; DestDir: "{app}"; Flags: ignoreversion; Components: core
Source: "{#BinDir}\FlowGrid.exe.config"; DestDir: "{app}"; Flags: ignoreversion; Components: core
Source: "{#BinDir}\FlowGrid.Sdk.dll"; DestDir: "{app}"; Flags: ignoreversion; Components: core
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion; Components: core
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion; Components: core
Source: "{#SamplesDir}\SampleWidgets.dll"; DestDir: "{localappdata}\FlowGrid\Plugins"; Flags: ignoreversion; Components: samples

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
; Matches AutostartUtil (value name "FlowGrid", quoted exe path); removed on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "FlowGrid"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; \
  Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only the program directory. User data (fences, settings, plugins, secrets,
; logs under {localappdata}\FlowGrid) survives an uninstall intentionally so
; a reinstall restores the previous desktop layout.
Type: filesandordirs; Name: "{app}"
