#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

#ifndef SourceDir
#define SourceDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif

#ifndef AppArch
#define AppArch "x64"
#endif

#ifndef InnoArch
#define InnoArch "x64os"
#endif

[Setup]
AppId={{E56E6D3E-9F44-4CB7-9E3A-47AAE72829E8}
AppName=UsageBar
AppVersion={#AppVersion}
AppPublisher=UsageBar
DefaultDirName={autopf}\UsageBar
DefaultGroupName=UsageBar
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=UsageBar-{#AppVersion}_{#AppArch}-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed={#InnoArch}
ArchitecturesInstallIn64BitMode={#InnoArch}
PrivilegesRequired=lowest
SetupLogging=yes
UninstallDisplayIcon={app}\UsageBar.exe
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\UsageBar"; Filename: "{app}\UsageBar.exe"
Name: "{group}\Uninstall UsageBar"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\UsageBar.exe"; Description: "Launch UsageBar"; Flags: nowait postinstall skipifsilent
