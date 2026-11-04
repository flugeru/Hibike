; Build after scripts/publish_windows.ps1 using Inno Setup 6.
#ifndef AppVersion
  #define AppVersion "0.2.0"
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
WizardStyle=modern dark
WizardImageFile=hibike-wizard.bmp
WizardSmallImageFile=hibike-wizard-small.bmp
LicenseFile=..\..\LICENSE
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=no
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"
Name: "cleardata"; Description: "Limpar dados do Hibike (mover dados atuais para backup local)"; GroupDescription: "Dados do aplicativo:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Executar {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  ClearDataConfirmed: Boolean;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectTasks then
  begin
    ClearDataConfirmed := False;
    if WizardIsTaskSelected('cleardata') then
    begin
      Result := MsgBox('O Hibike iniciará sem biblioteca, favoritos, playlists locais, histórico, fila e preferências atuais.' + #13#10 + #13#10 +
        'As pastas Hibike e Hibiki deste usuário serão movidas para um backup em ' + ExpandConstant('{localappdata}\Hibike-data-backups') +
        '. Arquivos de música dentro dessas pastas também serão movidos. Arquivos em outras pastas não serão alterados.' + #13#10 + #13#10 +
        'Feche o Hibike antes de continuar. Deseja limpar os dados?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
      ClearDataConfirmed := Result;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  CurrentData, LegacyData, BackupRoot, BackupDir: String;
  Suffix: Integer;
  MovedCurrent: Boolean;
begin
  if (CurStep <> ssInstall) or not WizardIsTaskSelected('cleardata') then Exit;
  { Never clear data in silent installs or without interactive confirmation. }
  if WizardSilent or not ClearDataConfirmed then Exit;
  CurrentData := ExpandConstant('{localappdata}\Hibike');
  LegacyData := ExpandConstant('{localappdata}\Hibiki');
  if not DirExists(CurrentData) and not DirExists(LegacyData) then Exit;
  BackupRoot := ExpandConstant('{localappdata}\Hibike-data-backups');
  BackupDir := BackupRoot + '\' + GetDateTimeString('yyyymmdd-hhnnss', '-', ':');
  Suffix := 0;
  while DirExists(BackupDir) do
  begin
    Suffix := Suffix + 1;
    BackupDir := BackupRoot + '\' + GetDateTimeString('yyyymmdd-hhnnss', '-', ':') + '-' + IntToStr(Suffix);
  end;
  if not ForceDirectories(BackupDir) then
    RaiseException('Não foi possível criar backup. Dados não foram limpos.');
  MovedCurrent := False;
  if DirExists(CurrentData) then
  begin
    if not RenameFile(CurrentData, BackupDir + '\Hibike') then
      RaiseException('Não foi possível mover dados do Hibike. Feche o aplicativo. Dados não foram apagados.');
    MovedCurrent := True;
  end;
  if DirExists(LegacyData) and not RenameFile(LegacyData, BackupDir + '\Hibiki') then
  begin
    if MovedCurrent then
      if not RenameFile(BackupDir + '\Hibike', CurrentData) then
        RaiseException('Falha ao restaurar dados. Seus arquivos estão preservados em: ' + BackupDir);
    RaiseException('Não foi possível mover dados antigos do Hibiki. Feche o aplicativo. Dados não foram apagados.');
  end;
  MsgBox('Dados anteriores preservados em:' + #13#10 + BackupDir + #13#10 + #13#10 + 'O Hibike iniciará com dados novos.', mbInformation, MB_OK);
end;
