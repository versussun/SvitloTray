; Inno Setup script for Svitlo (SvitloTray).
; Build (after `dotnet publish ... -o publish`):
;   ISCC.exe /DMyAppVersion=1.1.0 /DSourceDir=..\publish /DOutputDir=..\dist installer\SvitloTray.iss
;
; Installs per user into %LocalAppData%\Programs\SvitloTray — no administrator rights needed.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define MyAppName "Svitlo"
#define MyAppExeName "SvitloTray.exe"
#define MyAppPublisher "versussun"
#define MyAppUrl "https://github.com/versussun/SvitloTray"

[Setup]
; Never change AppId: it links upgrades and the uninstaller to earlier installs.
AppId={{6F2C8B1E-4A7D-4C39-9E52-3B8D0F7A1C64}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
VersionInfoVersion={#MyAppVersion}

; Per-user install, no UAC prompt.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\SvitloTray
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 or newer (same as the app's minimum).
MinVersion=10.0.17763

OutputDir={#OutputDir}
OutputBaseFilename=SvitloTray-{#MyAppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; The app hides to the tray instead of closing, so on upgrade/uninstall close it forcibly.
CloseApplications=force
RestartApplications=no

; The installer UI language follows the Windows language; the choice is offered on start.
ShowLanguageDialog=auto

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; The app itself writes this autostart value when "Start with Windows" is on;
; remove it on uninstall so Windows does not try to start a missing program.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SvitloTray"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings and the cached schedule in %AppData%\SvitloTray.
Type: filesandordirs; Name: "{userappdata}\SvitloTray"
