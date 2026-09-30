#define MyAppName "DeepSeek 余额小组件"
#define MyAppVersion "1.6.0"
#define MyAppPublisher "DeepSeekWidget"
#define MyAppExeName "DeepSeekWidget.exe"
#define MyAppId "57390486-F16D-4965-B668-71F6B10D6E47"

[Setup]
AppId={{57390486-F16D-4965-B668-71F6B10D6E47}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DeepSeekWidget
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=DeepSeekWidget-Setup-1.6.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UsedUserAreasWarning=no
LicenseFile=..\installer\License.rtf

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "startmenu"; Description: "创建开始菜单快捷方式"; GroupDescription: "附加任务:"; Flags: checkedonce
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"; Flags: unchecked
Name: "autostart"; Description: "开机自动启动"; GroupDescription: "附加任务:"; Flags: unchecked

[Files]
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeepSeekWidget"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
  WebView2Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

function URLDownloadToFile(Caller: Longint; URL, FileName: PAnsiChar; Reserved, Status: Longint): Longint;
  external 'URLDownloadToFileA@urlmon.dll stdcall';

function HasDotNet8Desktop(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

function HasWebView2(): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '');
end;

function DownloadAndRun(const Url, FileName, Parameters: String; var ErrorText: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if URLDownloadToFile(0, PAnsiChar(AnsiString(Url)), PAnsiChar(AnsiString(FileName)), 0, 0) <> 0 then
  begin
    ErrorText := '下载失败：' + Url;
    Exit;
  end;
  if not Exec(FileName, Parameters, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    ErrorText := '无法运行安装程序：' + FileName;
    Exit;
  end;
  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    ErrorText := '安装程序返回错误代码：' + IntToStr(ResultCode);
    Exit;
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorText: String;
  RuntimeFile: String;
  WebView2File: String;
begin
  Result := '';
  NeedsRestart := False;

  if not HasDotNet8Desktop() then
  begin
    RuntimeFile := ExpandConstant('{tmp}\windowsdesktop-runtime-8-x64.exe');
    if not DownloadAndRun(DotNetUrl, RuntimeFile, '/install /quiet /norestart', ErrorText) then
    begin
      Result := ErrorText;
      Exit;
    end;
  end;

  if not HasWebView2() then
  begin
    WebView2File := ExpandConstant('{tmp}\webview2-bootstrapper.exe');
    if not DownloadAndRun(WebView2Url, WebView2File, '/silent /install', ErrorText) then
    begin
      Result := ErrorText;
      Exit;
    end;
  end;
end;

