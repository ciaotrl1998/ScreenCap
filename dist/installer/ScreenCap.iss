#define MyAppName "ScreenCap"
#define MyAppVersion "1.2.12"
#define MyAppPublisher "ScreenCap"
#define MyAppExeName "ScreenCap.exe"
#define MySourceDir "D:\ScreenCap\dist\ScreenCap-portable"

[Setup]
AppId=ScreenCap.7E1C5A42-9B3D-4C6E-8F2A-1D4B6C8E0A3F
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=D:\ScreenCap\dist\installer
OutputBaseFilename=ScreenCap-Setup-{#MyAppVersion}
SetupIconFile=D:\ScreenCap\src\ScreenCap\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
CloseApplications=yes
AppMutex=Local\ScreenCap.SingleInstance
VersionInfoVersion=1.2.12.0
VersionInfoTextVersion=1.2.12
VersionInfoDescription=ScreenCap 录屏与 GIF 工具
VersionInfoProductName=ScreenCap
VersionInfoCompany=ScreenCap

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked
Name: "autostart"; Description: "开机自动启动 ScreenCap"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autoprograms}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScreenCap"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\libvlc"
Type: filesandordirs; Name: "{app}\tools"
