#define MyAppName "AoE Overlay"
#define MyAppVersion "3.0.0"
#define MyAppExeName "AoEOverlay.exe"
#define PublishDir "D:\bak\tttttt\AoE4_Overlay_CS\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{8F4C2A61-7B19-4E5D-9C33-1A6D0E8B42F5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppName}
DefaultDirName={autopf}\AoE Overlay
DefaultGroupName=AoE Overlay
DisableProgramGroupPage=yes
OutputDir=D:\bak\tttttt\AoE4_Overlay_CS\bin\Release
OutputBaseFilename=AoEOverlay-{#MyAppVersion}-Setup
SetupIconFile=D:\bak\tttttt\AoE4_Overlay_CS\img\aoe4_sword_shield.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "translation_history\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\AoE Overlay"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\AoE Overlay"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,AoE Overlay}"; Flags: nowait postinstall skipifsilent
