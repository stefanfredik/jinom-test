; ============================================================
; Inno Setup Script — FO Testing & Commissioning
; Jinom AI | v1.0.0 | 2026
; ============================================================

#define MyAppName      "FO Testing & Commissioning"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "Jinom AI"
#define MyAppExeName   "FoTestingApp.exe"
#define MyAppId        "{6A8F3B2C-1D4E-4F5A-9B7C-2E3D8F0A1B2C}"

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\JinomAI\FoTesting
DefaultGroupName={#MyAppPublisher}\FO Testing
OutputDir=..\Installer
OutputBaseFilename=FoTestingApp-v{#MyAppVersion}-Setup
SetupIconFile=FoTestingApp\Resources\jinom.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.17763
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=JinomFoTestingAppMutex

[Languages]
Name: "indonesian"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat shortcut di Desktop"; GroupDescription: "Shortcut tambahan:"

[Files]
; Self-contained publish output — sesuaikan path ke output folder dotnet publish
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Jalankan {#MyAppName} sekarang"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

; ============================================================
; Build Instructions:
; 1. Publish app terlebih dahulu:
;    dotnet publish FoTestingApp\FoTestingApp.csproj -c Release -r win-x64 --self-contained true -o publish
; 2. Buka installer\FoTestingApp.iss di Inno Setup Compiler
; 3. Klik Build > Compile
; 4. Installer akan tersimpan di installer\FoTestingApp-v1.0.0-Setup.exe
; ============================================================
