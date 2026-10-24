; Inno Setup script for GateLink (Windows installer).
; Produces GateLink-Setup.exe that installs the self-contained app, creates
; shortcuts, and registers an uninstaller.
;
; The app still needs Administrator rights to create the VPN adapter at connect
; time; the shortcut is marked to request elevation so the UAC prompt appears
; automatically when launched.

#define AppName "GateLink"
#define AppPublisher "GateLink"
#define AppExeName "GateLink.exe"
; AppVersion is passed in from CI with /DAppVersion=x.y.z; default for local runs.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{B8F2B4E7-7C2A-4E9B-9C3D-A1B2C3D4E5F6}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=output
OutputBaseFilename=GateLink-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Per-machine install needs admin; this is the install step, done once.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupicon"; Description: "Start GateLink when I sign in"; GroupDescription: "Startup:"

[Files]
; Payload is the published self-contained app (copied into this folder by CI).
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startupicon

[Run]
Description: "Launch {#AppName}"; Filename: "{app}\{#AppExeName}"; Flags: nowait postinstall skipifsilent runascurrentuser
