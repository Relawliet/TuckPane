#define MyAppName "TuckPane"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{2B7D4C50-0148-4D5C-A097-D8D7E5C64FCB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=ch998244353
AppPublisherURL=https://github.com/ch998244353/TuckPane
AppSupportURL=https://github.com/ch998244353/TuckPane/issues
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=TuckPane-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\src\TuckPane\Assets\TuckPane.ico
UninstallDisplayIcon={app}\TuckPane.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=ch998244353
VersionInfoDescription=TuckPane offline installer
VersionInfoProductName=TuckPane
AppMutex=Local\TuckPane-019d2f2d-0bfb-7ff0-98f5-d93093bb0b5d
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\TuckPane"; Filename: "{app}\TuckPane.exe"
Name: "{autodesktop}\TuckPane"; Filename: "{app}\TuckPane.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "GlassFolder"; Flags: deletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "TuckPane"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\TuckPane.exe"; Description: "{cm:LaunchProgram,TuckPane}"; Flags: nowait postinstall skipifsilent
