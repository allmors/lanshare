#define MyAppName "局域网共享-客户端"
#define MyAppVersion "0.01"
#define MyAppPublisher "Escher1108"
#define MyAppURL "mailto:contact@vcode.online"
#define MyAppExeName "LanShare.Client.exe"
#define MyPublishDir "..\publish\client-win-x64"

[Setup]
AppId={{9A8CC46F-921B-4C9E-A8AE-40D74886BF32}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\LanShare Client
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=LanShare-Client-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\LanShare.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "default"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional tasks:"
Name: "autostart"; Description: "Launch at Windows startup"; GroupDescription: "Additional tasks:"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "LanShareClient"; ValueData: """{app}\{#MyAppExeName}"""; \
    Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
