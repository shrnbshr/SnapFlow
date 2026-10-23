; GPL-3.0-or-later. Adapted from ShareX 21.0.0 ShareX-setup.iss.
; Build with build-installer.ps1; PayloadDir is a clean self-contained publish.
#ifndef PayloadDir
  #error PayloadDir must point to a clean publish directory
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#define AppName "SnapFlow"
#define AppVersion GetVersionNumbersString(PayloadDir + "\SnapFlow.exe")
#define ReleaseVersion Copy(AppVersion, 1, RPos(".", AppVersion) - 1)

[Setup]
AppId={{5C42C19A-441D-477A-AB91-E67E065F14DA}
AppName={#AppName}
AppVersion={#ReleaseVersion}
AppVerName={#AppName} {#ReleaseVersion}
AppPublisher=SnapFlow contributors
AppPublisherURL=https://github.com/shrnbshr/SnapFlow
AppSupportURL=https://github.com/shrnbshr/SnapFlow/issues
AppUpdatesURL=https://github.com/shrnbshr/SnapFlow/releases
AppCopyright=SnapFlow contributors; ShareX Team (upstream)
AppMutex=Local\SnapFlow
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DefaultDirName={localappdata}\Programs\SnapFlow
DefaultGroupName=SnapFlow
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=no
PrivilegesRequired=lowest
LicenseFile=..\LICENSE.txt
SetupIconFile=..\src\ShareX.Roll\Assets\SnapFlow.ico
UninstallDisplayIcon={app}\SnapFlow.exe
UninstallDisplayName=SnapFlow
OutputDir={#OutputDir}
OutputBaseFilename=SnapFlow-{#ReleaseVersion}-setup-x64
VersionInfoVersion={#AppVersion}
VersionInfoProductName=SnapFlow
VersionInfoCompany=SnapFlow contributors
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "chinesetrad"; MessagesFile: "Languages\ChineseTraditional.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Defensive exclusions in addition to build-installer.ps1's clean-directory check.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "SnapFlow.settings.json,SnapFlow.preferences.json,ShareX - roll.settings.json,*.pdb,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SnapFlow"; Filename: "{app}\SnapFlow.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,SnapFlow}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\SnapFlow"; Filename: "{app}\SnapFlow.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\SnapFlow.exe"; Description: "{cm:LaunchProgram,SnapFlow}"; Flags: nowait postinstall skipifsilent unchecked
