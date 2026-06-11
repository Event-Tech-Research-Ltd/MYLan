; MYLan Windows Installer Script
; Build with Inno Setup Compiler: ISCC.exe installer\windows\MYLan.iss
;
; Expected published app:
;   release\win-x64\MYLan.exe

#define MyAppName "MYLan DHCP Field Server"
#define MyAppShortName "MYLan"
#define MyAppVersion "2.1.0"
#define MyAppPublisher "Event Tech Research Ltd"
#define MyAppExeName "MYLan.exe"
#define MyAppURL "https://github.com/Event-Tech-Research-Ltd/MYLan"

[Setup]
AppId={{6A947B88-5A21-456F-9D4F-20260611D67C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Event Tech Research\MYLan
DefaultGroupName=MYLan
DisableProgramGroupPage=yes
OutputDir=..\..\installer-output\windows
OutputBaseFilename=MYLan-Setup-{#MyAppVersion}-win-x64
SetupIconFile=..\..\MYLan\Assets\DHCP.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "firewall"; Description: "Add Windows Firewall rule for MYLan"; GroupDescription: "Network access:"; Flags: checkedonce

[Files]
Source: "..\..\release\win-x64\MYLan.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MYLan"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\MYLan"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=\"MYLan DHCP Server\" dir=in action=allow program=\"{app}\{#MyAppExeName}\" protocol=UDP localport=67 enable=yes"; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MYLan"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=\"MYLan DHCP Server\""; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\MYLan"
