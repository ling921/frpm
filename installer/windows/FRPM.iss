#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif
#ifndef SourceDir
  #define SourceDir "..\\..\\publish\\win-x64"
#endif

[Setup]
AppId={{C9D2F3A1-7262-48C1-A33D-8958A0866A8D}
AppName=FRPM
AppVersion={#MyAppVersion}
AppPublisher=FRPM Contributors
DefaultDirName={autopf}\FRPM
DefaultGroupName=FRPM
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\dist
OutputBaseFilename=frpm-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=FRPM
SetupIconFile=frpm.ico
UninstallDisplayIcon={app}\frpm.ico
CloseApplications=yes
CloseApplicationsFilter=Frpm.Desktop.exe
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "startdesktop"; Description: "登录 Windows 后在托盘中运行 FRPM"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\desktop\*"; DestDir: "{app}\desktop"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "frpm.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\FRPM"; Filename: "{app}\desktop\Frpm.Desktop.exe"; IconFilename: "{app}\frpm.ico"; IconIndex: 0

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "FRPM Tray"; Flags: deletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FRPM"; ValueData: """{app}\desktop\Frpm.Desktop.exe"" --background"; Tasks: startdesktop; Flags: uninsdeletevalue

[Run]
Filename: "{app}\desktop\Frpm.Desktop.exe"; Description: "启动 FRPM"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function StopLegacyService(): String;
var
  ResultCode: Integer;
  Attempt: Integer;
begin
  Result := '';
  if not ServiceExists() then Exit;
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  for Attempt := 1 to 30 do
  begin
    if Exec(ExpandConstant('{cmd}'), '/C sc query FRPM | findstr /C:"STOPPED"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    begin
      if not Exec(ExpandConstant('{sys}\sc.exe'), 'delete FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        Result := '无法移除旧版 FRPM 服务，请停止并移除该服务后重试。';
      Exit;
    end;
    Sleep(1000);
  end;
  Result := '旧版 FRPM 服务未能停止，请停止该服务后重新运行安装程序。';
end;

function StopDesktop(OriginalUser: Boolean): String;
var
  ResultCode: Integer;
  ExecutablePath: String;
  Executed: Boolean;
begin
  Result := '';
  ExecutablePath := ExpandConstant('{app}\desktop\Frpm.Desktop.exe');
  if FileExists(ExecutablePath) then
  begin
    if OriginalUser then
      Executed := ExecAsOriginalUser(ExecutablePath, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    else
      Executed := Exec(ExecutablePath, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if not Executed or (ResultCode <> 0) then
      Result := 'FRPM 未能完成退出，请从托盘菜单退出 FRPM 后重试。';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := StopDesktop(True);
  if Result <> '' then Exit;
  Result := StopLegacyService();
  if Result <> '' then Exit;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Frpm.Tray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeUninstall(): Boolean;
var
  ErrorMessage: String;
begin
  ErrorMessage := StopDesktop(False);
  Result := ErrorMessage = '';
  if not Result then MsgBox(ErrorMessage, mbError, MB_OK);
end;

// Data is stored outside Program Files. Neither upgrades nor uninstall remove it.
