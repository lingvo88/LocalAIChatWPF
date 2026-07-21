#define MyAppName "LocalAIChat"
#define MyAppVersion "2.0"
#define MyAppPublisher "Mussa"
#define MyAppExeName "LocalAIChatWPF.exe"
#define MySourceDir "C:\Users\Mussa\Documents\LocalAIChatWPF"
#define MyServerDir "C:\Users\Mussa\Documents\LocalAIChat"

[Setup]
AppId={{B2C3D4E5-F6A7-8901-BCDE-F12345678901}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir={#MySourceDir}\..\installer
OutputBaseFilename=LocalAIChatSetup_v2
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Desktop app - single exe, no Python needed
Source: "{#MySourceDir}\bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Server brain files
Source: "{#MyServerDir}\app.py"; DestDir: "{app}\server"; Flags: ignoreversion
Source: "{#MyServerDir}\chat_store.py"; DestDir: "{app}\server"; Flags: ignoreversion
Source: "{#MyServerDir}\templates\*"; DestDir: "{app}\server\templates"; Flags: ignoreversion recursesubdirs
Source: "{#MyServerDir}\static\*"; DestDir: "{app}\server\static"; Flags: ignoreversion recursesubdirs
Source: "{#MyServerDir}\Toggle-ServerAutoStart.ps1"; DestDir: "{app}\server"; Flags: ignoreversion
Source: "{#MyServerDir}\Toggle-AutoStart.ps1"; DestDir: "{app}\server"; Flags: ignoreversion
Source: "{#MyServerDir}\Start-OpenWebUI.bat"; DestDir: "{app}\server"; Flags: ignoreversion
Source: "{#MyServerDir}\INSTALL_NOTES.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\LocalAIChat"; Filename: "{app}\{#MyAppExeName}"; Comment: "Open Local AI Chat"
Name: "{group}\Start AI Server"; Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\server\Toggle-ServerAutoStart.ps1"" -Enable"; WorkingDir: "{app}\server"
Name: "{group}\Uninstall LocalAIChat"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LocalAIChat"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Install pip dependencies for the server (Python must already be installed)
Filename: "cmd.exe"; Parameters: "/c python -m pip install --quiet flask requests ddgs 2>&1"; StatusMsg: "Installing server dependencies..."; Flags: runhidden waituntilterminated

; Unblock PowerShell scripts
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""Unblock-File '{app}\server\Toggle-ServerAutoStart.ps1'; Unblock-File '{app}\server\Toggle-AutoStart.ps1'"""; Flags: runhidden waituntilterminated

; Open install folder after setup
Filename: "{app}"; Description: "Open installation folder"; Flags: postinstall shellexec skipifsilent unchecked

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""Unregister-ScheduledTask -TaskName 'LocalAIChat-Server' -Confirm:$false -ErrorAction SilentlyContinue"""; Flags: runhidden waituntilterminated