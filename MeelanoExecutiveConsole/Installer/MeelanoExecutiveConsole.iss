#define MyAppName "MEELANO Executive Console"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "MEELANO"
#define MyAppExeName "MeelanoExecutiveConsole.exe"

[Setup]
AppId={{4E92AF9E-4107-4D2E-9FE0-7263B0BD7650}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\MEELANO Executive Console
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Resources\meelano.ico
OutputDir=..\artifacts\installer
OutputBaseFilename=MEELANO-Executive-Console-Setup-win-x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
MinVersion=10.0.17763
WizardStyle=modern
DisableProgramGroupPage=yes
Compression=lzma2/ultra64
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MEELANO Executive Console"; Flags: postinstall nowait skipifsilent
