; Инсталлятор BIFIT Report (Inno Setup 6).
;
; Собирается скриптом scripts\build-release.ps1, который передаёт параметры:
;   /DMyAppVersion=1.2.3
;   /DPublishDir=<абсолютный путь к каталогу публикации>
;   /DOutputDir=<абсолютный путь к каталогу с готовыми файлами>
;
; Ручная сборка:
;   ISCC.exe installer\BifitReport.iss /DMyAppVersion=1.2.3 /DPublishDir=... /DOutputDir=...

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64-selfcontained"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define MyAppName "BIFIT Report"
#define MyAppService "BifitReport"
#define MyAppExe "BifitReport.Agent.exe"
#define MyAppSettingsExe "BifitReport.Settings.exe"
#define MyAppURL "https://github.com/Arseniys1/bifit-report"
#define MyStateDir "{commonappdata}\BifitReport"

[Setup]
AppId={{5D8E1C2A-0F47-4B39-9C61-3E8A7D2B4F10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=BIFIT Report
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\BifitReport
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.14393
OutputDir={#OutputDir}
OutputBaseFilename=BifitReport-{#MyAppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
SetupMutex=Global\BifitReportSetup
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppSettingsExe}
VersionInfoProductName={#MyAppName}
VersionInfoCompany=BIFIT Report
VersionInfoDescription=Установщик BIFIT Report
VersionInfoVersion={#MyAppVersion}.0

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
russian.LaunchSettings=Открыть настройщик BIFIT Report
russian.ShortcutSettings=Настройки BIFIT Report
russian.ShortcutFolder=Папка BIFIT Report
russian.ShortcutUninstall=Удалить BIFIT Report
russian.DesktopIcon=Создать ярлык настройщика на рабочем столе
russian.RegisteringService=Регистрация службы BIFIT Report...
russian.ServiceFailed=Служба BIFIT Report не зарегистрирована (код ошибки %1).

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "appsettings.json,appsettings.local.json,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
; Настройки обновлением не перезаписываются: настройщик и install-service.ps1
; меняют этот файл на месте, и релиз не должен сбрасывать точку, режим и получателей.
Source: "{#PublishDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{cm:ShortcutSettings}"; Filename: "{app}\{#MyAppSettingsExe}"
Name: "{group}\{cm:ShortcutFolder}"; Filename: "{app}"
Name: "{group}\{cm:ShortcutUninstall}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{cm:ShortcutSettings}"; Filename: "{app}\{#MyAppSettingsExe}"; Tasks: desktopicon

[Run]
; Служба регистрируется в [Code] (см. RegisterService), поэтому здесь только
; запуск настройщика после установки.
Filename: "{app}\{#MyAppSettingsExe}"; Description: "{cm:LaunchSettings}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\scripts\uninstall-service.ps1"" -ServiceName ""{#MyAppService}"" -InstallPath ""{app}"" -KeepFiles"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "RemoveBifitReportService"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\scripts"
Type: files; Name: "{app}\appsettings.json"
Type: dirifempty; Name: "{app}"

[Code]
var
  WasInstalled: Boolean;

procedure StopServiceIfRunning();
var
  ResultCode: Integer;
begin
  // Возвращает 2, если службы нет или она уже остановлена, — это не ошибка.
  Exec('net.exe', 'stop {#MyAppService}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure RegisterService();
var
  ResultCode: Integer;
  Parameters: String;
  Message: String;
begin
  if WizardForm <> nil then
    WizardForm.StatusLabel.Caption := ExpandConstant('{cm:RegisteringService}');
  Parameters :=
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\scripts\install-service.ps1') + '"' +
    ' -PublishPath "' + ExpandConstant('{app}') + '"' +
    ' -InstallPath "' + ExpandConstant('{app}') + '"' +
    ' -StateDirectory "' + ExpandConstant('{#MyStateDir}') + '"' +
    ' -SkipFileCopy';
  if WasInstalled then
    Parameters := Parameters + ' -PreserveConfiguration';

  if (not Exec('powershell.exe', Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode))
     or (ResultCode <> 0) then
  begin
    Message := FmtMessage(CustomMessage('ServiceFailed'), [IntToStr(ResultCode)]) + #13#10 + #13#10 +
      'Файлы установлены. Запустите PowerShell от имени администратора и выполните:';
    Message := Message + #13#10 + #13#10 +
      ExpandConstant('{app}') + '\scripts\install-service.ps1 -PublishPath "' +
      ExpandConstant('{app}') + '" -InstallPath "' + ExpandConstant('{app}') + '" -SkipFileCopy';
    MsgBox(Message, mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Признак обновления определяется до копирования файлов: appsettings.json
    // остаётся от предыдущей установки.
    WasInstalled := FileExists(ExpandConstant('{app}\appsettings.json'));
    StopServiceIfRunning();
  end
  else if CurStep = ssPostInstall then
    RegisterService();
end;
