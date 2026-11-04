; Wizard de instalación de SackSpace (Inno Setup 6). Lo compila publish.ps1:
;   ISCC.exe installer\SackSpace.iss   ->  dist\SackSpace-Setup-<versión>.exe
; Por usuario y sin admin, en la misma carpeta que ya usaba la instalación a mano,
; así que instalar encima actualiza y conserva los datos (%LOCALAPPDATA%\SackSpace).

#define AppExe "SackSpace.exe"
#define SourceExe AddBackslash(SourcePath) + "..\dist\" + AppExe
#define FullVersion GetVersionNumbersString(SourceExe)
#define AppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)

[Setup]
AppId=SackSpace
AppName=SackSpace
AppVersion={#AppVersion}
AppPublisher=Sackito
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\SackSpace
DefaultGroupName=SackSpace
DisableProgramGroupPage=yes
DisableWelcomePage=no
; Condiciones de uso y privacidad: hay que aceptarlas para seguir.
LicenseFile=LICENCIA.txt
WizardStyle=modern
; Solo pregunta el idioma si Windows no está en español ni en inglés.
ShowLanguageDialog=auto
SetupIconFile=..\src\SackSpace\Assets\sackspace.ico
UninstallDisplayName=SackSpace
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=SackSpace-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Cierra SackSpace si está abierto antes de reemplazar el exe; al final se vuelve a abrir desde [Run].
CloseApplications=force
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Iniciar SackSpace con Windows"; GroupDescription: "Opciones:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\SackSpace"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\SackSpace"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; El mismo valor que escribe Ajustes › General de la app (Services/Autostart.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SackSpace"; ValueData: """{app}\{#AppExe}"" --autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,SackSpace}"; Flags: nowait postinstall skipifsilent
