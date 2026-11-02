; Build after scripts/publish_windows.ps1 using Inno Setup 6.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\Hibike-win-x64"
#endif
#define AppName "Hibike"
#define AppExeName "Hibike.Desktop.exe"

[Setup]
AppId={{AE7FDF94-83CF-4E27-BEE7-11986EBE06F3}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=flugeru
AppPublisherURL=https://github.com/flugeru/Hibike
AppSupportURL=https://github.com/flugeru/Hibike/issues
AppUpdatesURL=https://github.com/flugeru/Hibike/actions
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\artifacts\installer
OutputBaseFilename=Hibike-Setup-x64
SetupIconFile=..\..\src\Hibike.App\Assets\hibike-desktop.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\..\LICENSE
UsePreviousAppDir=yes
UsePreviousGroup=yes
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Executar {#AppName}"; Flags: nowait postinstall skipifsilent
