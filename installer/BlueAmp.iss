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
AppPublisherURL=https://github.com/TanatipS3/BlueAmp
DefaultDirName={autopf}\BlueAmp
DefaultGroupName=BlueAmp
DisableProgramGroupPage=yes
; Per-user install: lands in %LOCALAPPDATA%\Programs\BlueAmp, never touches
; system directories and never prompts for UAC elevation.
PrivilegesRequired=lowest
OutputBaseFilename=BlueAmp-Setup-{#AppVersion}
OutputDir=Output
SetupIconFile=..\src\BaudioApp\Assets\tray-idle.ico
UninstallDisplayIcon={app}\BaudioApp.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Matches the mutex in Program.cs — prompts to close a running BlueAmp first.
AppMutex=BlueAmp_SingleInstance
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BlueAmp"; Filename: "{app}\BaudioApp.exe"
Name: "{autodesktop}\BlueAmp"; Filename: "{app}\BaudioApp.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BaudioApp.exe"; Description: "{cm:LaunchProgram,BlueAmp}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The app registers itself under HKCU Run when "Launch at startup" is enabled;
; clean that up so an uninstalled app is never launched again.
Filename: "reg.exe"; Parameters: "delete ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"" /v BlueAmp /f"; Flags: runhidden; RunOnceId: "RemoveAutostart"
