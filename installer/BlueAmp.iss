; BlueAmp installer — per-user, no admin rights required.
; Build: iscc BlueAmp.iss /DAppVersion=1.0.0 /DPublishDir=..\publish

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{7E1B7A64-4C1F-4A8E-9B0C-2D5385A9B3F1}
AppName=BlueAmp
AppVersion={#AppVersion}
AppPublisher=TanatipS3
AppCopyright=Copyright (c) 2026 TanatipS3 - MIT License
AppPublisherURL=https://github.com/TanatipS3/BlueAmp
DefaultDirName={autopf}\BlueAmp
DefaultGroupName=BlueAmp
DisableProgramGroupPage=yes
; Per-user install: lands in %LOCALAPPDATA%\Programs\BlueAmp, never touches
; system directories and never prompts for UAC elevation.
PrivilegesRequired=lowest
OutputBaseFilename=BlueAmp-Setup-{#AppVersion}
OutputDir=Output
SetupIconFile=..\src\BlueAmp\Assets\tray-idle.ico
; Shows the MIT license (with its "as-is, no warranty" terms) before installing.
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\BlueAmp.exe
Compression=lzma2/max
SolidCompression=yes
; Releases are built on a UTC machine; store times as UTC so installed files (and
; the "built …" time in the popup) show the right local time.
TimeStampsInUTC=yes
WizardStyle=modern
; Matches the mutex in Program.cs — prompts to close a running BlueAmp first.
AppMutex=BlueAmp_SingleInstance
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Runtime natives shipped by older self-contained builds — no longer bundled.
Type: files; Name: "{app}\*_cor3.dll"
; Releases up to 1.0.x shipped the executable as BaudioApp.exe; it is BlueAmp.exe now.
Type: files; Name: "{app}\BaudioApp.exe"
Type: files; Name: "{app}\BaudioApp.pdb"
Type: files; Name: "{app}\BaudioApp.exe.bak"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; MIT requires the license and third-party copyright notices to travel with the app.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\BlueAmp"; Filename: "{app}\BlueAmp.exe"
Name: "{autodesktop}\BlueAmp"; Filename: "{app}\BlueAmp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BlueAmp.exe"; Description: "{cm:LaunchProgram,BlueAmp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The app registers itself under HKCU Run when "Launch at startup" is enabled;
; clean that up so an uninstalled app is never launched again.
Filename: "reg.exe"; Parameters: "delete ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"" /v BlueAmp /f"; Flags: runhidden; RunOnceId: "RemoveAutostart"

[Code]
// The app is published framework-dependent, so the target needs the .NET 8
// Desktop Runtime (x64). Warn with a download link but let the install finish —
// Windows itself also shows a download prompt if the app is launched without it.
function IsDotNet8DesktopInstalled(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64,
      'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
      Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then
      begin
        Result := True;
        Exit;
      end;
end;

function InitializeSetup(): Boolean;
var
  ErrCode: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
    if MsgBox('BlueAmp needs the .NET 8 Desktop Runtime (x64), which is not installed on this PC.'#13#10#13#10
              + 'Open the download page now? (Setup will continue either way.)',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0/runtime',
                '', '', SW_SHOW, ewNoWait, ErrCode);
end;
