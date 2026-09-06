; LiwaPlayer kurulum betiği (Inno Setup 6)
; Derlemek için: Installer\build-setup.ps1

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "LiwaPlayer"
#define AppPublisher "Liwa"
#define AppExe "LiwaPlayer.exe"
#define PublishDir "publish"

[Setup]
AppId={{A7C1F3E2-6B4D-4F8A-9E2C-3D5B7A9C1E42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=LiwaPlayer-Setup-{#AppVersion}
SetupIconFile=..\LiwaPlayer\lwplay.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
; Uygulama tepside gizli çalışıyor olabilir; Restart Manager yerine [Code] içinde kapatılır
CloseApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstünde kısayol oluştur"; GroupDescription: "Kısayollar:"
Name: "autostart"; Description: "Windows açılışında LiwaPlayer'ı otomatik başlat (POS için önerilir)"; GroupDescription: "Başlangıç:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} Kaldır"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "LiwaPlayer'ı şimdi başlat"; Flags: nowait postinstall skipifsilent
; Uygulama içi güncelleyici /RESTARTAPP ile çağırır: sessiz kurulum bitince uygulama geri açılır
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: CmdLineParamExists('/RESTARTAPP')

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function CmdLineParamExists(const Value: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

// Çalışan (tepside gizli olabilecek) kopyayı kurulumdan önce kapat
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  R: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, R);
  Sleep(800);
  Result := '';
end;

// WebView2 Runtime (hesap girişi penceresi için) kurulu mu?
function IsWebView2Installed: Boolean;
var
  V: String;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', V) or
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', V) or
    RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', V);

  if Result then
    Result := (V <> '') and (V <> '0.0.0.0');
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

// Eksikse WebView2 önyükleyicisini Microsoft'tan indir (internet yoksa sessizce atla)
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID = wpReady) and (not IsWebView2Installed) then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('https://go.microsoft.com/fwlink/p/?LinkId=2124703', 'MicrosoftEdgeWebView2Setup.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        Log('WebView2 indirilemedi: ' + GetExceptionMessage);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  R: Integer;
  Bootstrapper: String;
begin
  if CurStep = ssPostInstall then
  begin
    Bootstrapper := ExpandConstant('{tmp}\MicrosoftEdgeWebView2Setup.exe');

    if (not IsWebView2Installed) and FileExists(Bootstrapper) then
      Exec(Bootstrapper, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, R);
  end;
end;
