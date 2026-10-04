; Inno Setup script for a Dusk installer, set up the way f.lux installs:
; per user, no admin prompt, into %LOCALAPPDATA%, starting with Windows.
;
; Install Inno Setup 6 (https://jrsoftware.org/isdl.php), then build with:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\dusk.iss
; The installer lands in dist\DuskSetup-<version>.exe.

#define AppName "Dusk"
#define AppVersion "1.1.0"
#define AppPublisher "Yousif"
#define AppUrl "https://github.com/YOUR-USERNAME/dusk"

[Setup]
AppId={{8E3C1C2F-6A0E-4C9E-9A1D-6F4E2B0D7A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=DuskSetup-{#AppVersion}
SetupIconFile=..\bin\dusk.ico
UninstallDisplayIcon={app}\Dusk.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=
MinVersion=10.0

[Files]
Source: "..\bin\Dusk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\fonts\InstrumentSerif-OFL.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\fonts\HankenGrotesk-OFL.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Dusk.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; Start with Windows, the way Dusk does it itself after first run.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Dusk"; \
    ValueData: """{app}\Dusk.exe"" --startup"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Dusk.exe"; Description: "Start Dusk"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close the running copy so the files can be removed.
Filename: "{cmd}"; Parameters: "/c taskkill /im Dusk.exe /f"; Flags: runhidden; RunOnceId: "StopDusk"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Dusk"
