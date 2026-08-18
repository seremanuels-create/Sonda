; Installer di Sonda (Windows 64 bit)
;
; Si compila da ..\build.ps1, che prima produce dist\portable\Sonda.exe (single-file,
; self-contained: non serve .NET sul computer di destinazione) e poi chiama ISCC con
; /DAppVersion=<versione>. ISCC sta in %LOCALAPPDATA%\Programs\Inno Setup 6\.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName        "Sonda"
#define AppPublisher   "StarVerb Audio"
#define AppCopyright   "Copyright (C) 2026 StarVerb Audio"
#define AppExe         "Sonda.exe"
#define PortableDir    "..\dist\portable"

[Setup]
; GUID proprio di Sonda: non riusare quelli dei plugin.
AppId={{7C1E2B4A-5D63-4F0B-9A7E-2B4C6D8E1F30}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright={#AppCopyright}

; Risorsa di versione completa: i classificatori euristici di Defender pesano anche questa.
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} {#AppVersion} Setup
VersionInfoCopyright={#AppCopyright}
VersionInfoOriginalFileName={#AppName}-{#AppVersion}-Setup.exe
SetupIconFile={#SourcePath}\..\Resources\icon.ico
UninstallDisplayIcon={app}\{#AppExe}

; Installazione per utente (nessun UAC): Sonda chiede lui l'elevazione quando serve.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0

OutputDir={#SourcePath}\..\dist
OutputBaseFilename={#AppName}-{#AppVersion}-Setup
; Non solida, come per i plugin: meno sospetta per gli antivirus euristici.
Compression=lzma2/normal
SolidCompression=no
WizardStyle=modern
UninstallDisplayName={#AppName}
CloseApplications=yes
RestartApplications=no
; La pagina di benvenuto (con la spiegazione di cosa fa Sonda) in Inno 6 e' spenta di default
DisableWelcomePage=no

; Firma: attiva con /DSign e /Sstarverb=... (lo fa build.ps1 -Firma). Anche il disinstallatore.
#ifdef Sign
SignTool=starverb
SignedUninstaller=yes
#endif

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
italian.WelcomeLabel1=Installazione di [name/ver]
italian.WelcomeLabel2=Sonda analizza lo spazio occupato sul disco: ti dice cosa lo consuma, dove sta e come liberarlo.%n%nNon serve altro software: tutto il necessario e' dentro l'installer.
english.WelcomeLabel1=Installing [name/ver]
english.WelcomeLabel2=Sonda analyses disk usage: it tells you what takes up space, where it lives and how to free it.%n%nNo other software is needed.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PortableDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourcePath}\..\LEGGIMI.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
