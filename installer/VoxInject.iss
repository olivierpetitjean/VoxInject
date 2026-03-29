; VoxInject — Inno Setup 6 installer script
; Build via CI:  iscc /DMyAppVersion=1.2.3 installer\VoxInject.iss
; Build locally: iscc /DMyAppVersion=0.0.0-dev installer\VoxInject.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyAppName      "VoxInject"
#define MyAppPublisher "VoxInject Contributors"
#define MyAppURL       "https://github.com/olivierpetitjean/VoxInject"
#define MyAppExeName   "VoxInject.exe"
#define PublishDir     "..\src\VoxInject\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
; Stable GUID — never change after first release
AppId={{B7A4C2E1-5F3D-4A8B-9C6E-2D1F0E3B7A4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

; Install to %LocalAppData%\Programs\VoxInject — no UAC prompt required
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest

; Output
OutputDir=output
; Nom fixe → URL permanente sur GitHub Releases/latest/download/VoxInject-setup.exe
OutputBaseFilename=VoxInject-setup
SetupIconFile=..\src\VoxInject\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}

; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Windows 10 x64 minimum
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Misc
WizardStyle=modern
DisableWelcomePage=no
ShowLanguageDialog=no
CloseApplications=yes

[Languages]
Name: "french";  MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; \
  Description: "Lancer {#MyAppName} automatiquement au démarrage de Windows"; \
  GroupDescription: "Options :"; \
  Flags: unchecked

[Files]
; Main executable (single-file publish)
Source: "{#PublishDir}\{#MyAppExeName}"; \
  DestDir: "{app}"; \
  Flags: ignoreversion

; Provider plugins
Source: "{#PublishDir}\plugins\*"; \
  DestDir: "{app}\plugins"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";              Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; Run at startup (only when the task is selected)
Root: HKCU; \
  Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; \
  ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"""; \
  Flags: uninsdeletevalue; \
  Tasks: startup

[Run]
; Offer to launch the app at the end of setup
Filename: "{app}\{#MyAppExeName}"; \
  Description: "Lancer {#MyAppName}"; \
  Flags: nowait postinstall skipifsilent

[Code]
// ---------------------------------------------------------------------------
// Check that .NET 8 Desktop Runtime is present.
// We look for the shared host entry; Desktop workload implies it's installed.
// ---------------------------------------------------------------------------
function IsDotNet8DesktopInstalled(): Boolean;
var
  RuntimePath: string;
  FindRec: TFindRec;
begin
  // Typical path: C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\8.*
  RuntimePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\');
  Result := FindFirst(RuntimePath + '8.*', FindRec);
  FindClose(FindRec);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    if MsgBox(
      'Le runtime .NET 8 Desktop est requis pour faire fonctionner VoxInject.' + #13#10 + #13#10 +
      'Voulez-vous ouvrir la page de téléchargement maintenant ?' + #13#10 +
      '(Installez ".NET Desktop Runtime 8.x", puis relancez ce setup.)',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/download/dotnet/8.0',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    Result := False;
  end;
end;
