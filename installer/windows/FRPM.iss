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
  LegacyDirectoryPage: TInputQueryWizardPage;
  LegacyDataStoppedPage: TInputOptionWizardPage;
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
  LegacyDirectoryPage := CreateInputQueryPage(wpSelectDir,
    '迁移现有 FRPM 数据',
    '选择旧版 FRPM 目录（可选）',
    '若要从压缩包版升级，请选择包含 data 目录的旧 FRPM 文件夹。' + #13#10 +
    '留空会创建新的 FRPM 数据目录。');
  LegacyDirectoryPage.Add('旧版 FRPM 目录：', False);
  LegacyDirectoryPage.Values[0] := '';

  LegacyDataStoppedPage := CreateInputOptionPage(LegacyDirectoryPage.ID,
    '确认迁移',
    '旧版 FRPM 已停止',
    '迁移会复制旧版 data 目录。请先关闭旧版 FRPM，避免复制到不完整的数据库或密钥。',
    True, False);
  LegacyDataStoppedPage.Add('我已停止旧版 FRPM');
  LegacyDataStoppedPage.SelectedValueIndex := 0;

  PortPage := CreateInputQueryPage(LegacyDataStoppedPage.ID,
    '配置管理端口',
    '选择本机管理页面端口',
    'FRPM 默认只监听本机回环地址。可按需修改端口，避免与本地开发服务冲突。');
  PortPage.Add('管理端口：', False);
  PortPage.Values[0] := '8180';
end;

function HasLegacyDirectory(): Boolean;
begin
  Result := Trim(LegacyDirectoryPage.Values[0]) <> '';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ((PageID = LegacyDataStoppedPage.ID) and not HasLegacyDirectory())
    or ((PageID = PortPage.ID) and FileExists(ExpandConstant('{commonappdata}\FRPM\appsettings.json')));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = LegacyDirectoryPage.ID) and HasLegacyDirectory() then
  begin
    if not DirExists(AddBackslash(LegacyDirectoryPage.Values[0]) + 'data') then
    begin
      MsgBox('所选目录中未找到 data 文件夹。请选择旧版 FRPM 根目录，或留空继续。', mbError, MB_OK);
      Result := False;
    end;
  end;

  if (CurPageID = LegacyDataStoppedPage.ID) and HasLegacyDirectory()
    and (LegacyDataStoppedPage.SelectedValueIndex <> 0) then
  begin
    MsgBox('请确认旧版 FRPM 已停止后再继续迁移。', mbError, MB_OK);
    Result := False;
  end;

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
  Configuration: String;
begin
  ConfigurationPath := ExpandConstant('{commonappdata}\FRPM\appsettings.json');
  if FileExists(ConfigurationPath) then
    Exit;

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
end;

procedure CopyLegacyData();
var
  SourceDirectory: String;
  TargetDirectory: String;
  ResultCode: Integer;
begin
  if not HasLegacyDirectory() then
    Exit;

  TargetDirectory := ExpandConstant('{commonappdata}\FRPM\data');
  if FileExists(AddBackslash(TargetDirectory) + 'frpm.db') then
    Exit;

  SourceDirectory := AddBackslash(LegacyDirectoryPage.Values[0]) + 'data';
  if not Exec(ExpandConstant('{cmd}'), '/C xcopy /E /I /H /Y ' + AddQuotes(SourceDirectory) + ' ' + AddQuotes(TargetDirectory), '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    RaiseException('无法迁移旧版 data 目录。安装已取消，原有数据未被删除。');
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
begin
  if CurStep = ssInstall then
  begin
    ServiceAlreadyInstalled := ServiceExists();
    StopService();
  end;

  if CurStep = ssPostInstall then
  begin
    CopyLegacyData();
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
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete FRPM', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
