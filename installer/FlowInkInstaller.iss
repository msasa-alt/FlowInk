#define MyAppName "FlowInk"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "FlowInk"
#define MyAppExeName "FlowInk.exe"

[Setup]
AppId={{A9F6B1D2-6C7E-4E6D-9A52-2D7E7D4C31A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

AllowNoIcons=yes

OutputDir=..\installer-output
OutputBaseFilename=FlowInk-Setup

Compression=lzma
SolidCompression=yes
WizardStyle=modern

PrivilegesRequired=admin

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

UninstallDisplayIcon={app}\{#MyAppExeName}

ShowLanguageDialog=yes

[Languages]
Name: "english";  MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.DesktopIconTask=Create a desktop shortcut
english.AdditionalTasks=Additional tasks:
english.UninstallApp=Uninstall {#MyAppName}
english.LaunchApp=Launch {#MyAppName}

japanese.DesktopIconTask=デスクトップショートカットを作成する
japanese.AdditionalTasks=追加タスク:
japanese.UninstallApp={#MyAppName} をアンインストール
japanese.LaunchApp={#MyAppName} を起動する

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallApp}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
