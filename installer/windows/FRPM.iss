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
CloseApplications=no
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "starttray"; Description: "登录 Windows 后启动 FRPM 托盘图标"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\server\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\tray\*"; DestDir: "{app}\tray"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "frpm.ico"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FRPM Tray"; ValueData: """{app}\tray\Frpm.Tray.exe"""; Tasks: starttray; Flags: uninsdeletevalue

[Run]
Filename: "{app}\tray\Frpm.Tray.exe"; Description: "启动 FRPM 托盘图标"; Flags: nowait postinstall skipifsilent; Tasks: starttray

[Code]
var
  PortPage: TInputQueryWizardPage;
  ServiceAlreadyInstalled: Boolean;

function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure StopService();
var
  ResultCode: Integer;
  Attempt: Integer;
begin
  if ServiceExists() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    for Attempt := 1 to 30 do
    begin
      if Exec(ExpandConstant('{cmd}'), '/C sc query FRPM | findstr /C:"STOPPED"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
        Exit;
      Sleep(1000);
    end;
    RaiseException('FRPM 服务未能在 30 秒内停止。请停止该服务后重新运行安装程序。');
  end;
end;

procedure InitializeWizard;
begin
  PortPage := CreateInputQueryPage(wpWelcome,
    '配置管理端口',
    '选择本机管理页面端口',
    'FRPM 默认只监听本机回环地址。可按需修改端口，避免与本地开发服务冲突。');
  PortPage.Add('管理端口：', False);
  PortPage.Values[0] := '8180';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = PortPage.ID) and FileExists(ExpandConstant('{commonappdata}\FRPM\appsettings.json')) and ServiceExists();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = PortPage.ID then
  begin
    if (StrToIntDef(Trim(PortPage.Values[0]), 0) < 1)
      or (StrToIntDef(Trim(PortPage.Values[0]), 0) > 65535) then
    begin
      MsgBox('请输入 1 到 65535 之间的管理端口。', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure WriteDefaultConfiguration();
var
  ConfigurationPath: String;
  OverrideConfigurationPath: String;
  Configuration: String;
begin
  ConfigurationPath := ExpandConstant('{commonappdata}\FRPM\appsettings.json');
  if not FileExists(ConfigurationPath) then
  begin
    Configuration := '{' + #13#10 +
      '  "Urls": "http://127.0.0.1:' + Trim(PortPage.Values[0]) + '",' + #13#10 +
      '  "ConnectionStrings": {' + #13#10 +
      '    "DefaultConnection": "Data Source=data/frpm.db"' + #13#10 +
      '  },' + #13#10 +
      '  "Frpm": {' + #13#10 +
      '    "Storage": {' + #13#10 +
      '      "DataDirectory": "data",' + #13#10 +
      '      "LogRetentionDays": 30,' + #13#10 +
      '      "MaxLogBytes": 536870912,' + #13#10 +
      '      "MaxPackageBytes": 268435456' + #13#10 +
      '    }' + #13#10 +
      '  }' + #13#10 +
      '}' + #13#10;
    if not SaveStringToFile(ConfigurationPath, Configuration, False) then
      RaiseException('无法创建 FRPM 配置文件。');
    Exit;
  end;

  if not ServiceAlreadyInstalled then
  begin
    OverrideConfigurationPath := ExpandConstant('{commonappdata}\FRPM\appsettings.Production.json');
    Configuration := '{' + #13#10 +
      '  "Urls": "http://127.0.0.1:' + Trim(PortPage.Values[0]) + '"' + #13#10 +
      '}' + #13#10;
    if not SaveStringToFile(OverrideConfigurationPath, Configuration, False) then
      RaiseException('无法更新 FRPM 管理端口配置。');
  end;
end;

function ServiceParameters(): String;
begin
  Result := 'binPath= ' + AddQuotes(ExpandConstant('{app}\server\Frpm.exe')) + ' start= auto DisplayName= ' + AddQuotes('FRPM');
end;

procedure RegisterAndStartService();
var
  ResultCode: Integer;
  Command: String;
begin
  if ServiceAlreadyInstalled then
    Command := 'config FRPM ' + ServiceParameters()
  else
    Command := 'create FRPM ' + ServiceParameters();

  if not Exec(ExpandConstant('{sys}\sc.exe'), Command, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    RaiseException(Format('无法注册 FRPM Windows 服务。sc.exe 返回代码：%d。', [ResultCode]));
  end;

  Exec(ExpandConstant('{sys}\sc.exe'), 'start FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Frpm.Tray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    ServiceAlreadyInstalled := ServiceExists();
    StopService();
  end;

  if CurStep = ssPostInstall then
  begin
    WriteDefaultConfiguration();
    RegisterAndStartService();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Frpm.Tray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
