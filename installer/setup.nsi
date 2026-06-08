!ifndef APP_VERSION
!define APP_VERSION "2.0.0"
!endif

!ifndef SOURCEDIR
!define SOURCEDIR "..\target\release"
!endif

!ifndef OUTDIR
!define OUTDIR "..\artifacts\installer"
!endif

!ifndef ASSETDIR
!define ASSETDIR "assets"
!endif

!define APP_NAME "UsageBar"
!define COMPANY_NAME "UsageBar"
!define EXE_NAME "UsageBar.exe"
!define INSTALL_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\UsageBar"

Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma

Name "${APP_NAME}"
OutFile "${OUTDIR}\UsageBar-${APP_VERSION}_x64-setup.exe"
InstallDir "$LOCALAPPDATA\Programs\UsageBar"
InstallDirRegKey HKCU "${INSTALL_REG_KEY}" "InstallLocation"
BrandingText "${APP_NAME}"

VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${COMPANY_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"

Page directory
Page instfiles

UninstPage uninstConfirm
UninstPage instfiles

Section "Install"
    SetOutPath "$INSTDIR"
    File "${SOURCEDIR}\${EXE_NAME}"

    SetOutPath "$INSTDIR\assets"
    File "${ASSETDIR}\AppIcon.ico"
    File "${ASSETDIR}\AppIcon.png"

    SetOutPath "$INSTDIR"
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateDirectory "$SMPROGRAMS\UsageBar"
    CreateShortcut "$SMPROGRAMS\UsageBar\UsageBar.lnk" "$INSTDIR\${EXE_NAME}"
    CreateShortcut "$SMPROGRAMS\UsageBar\Uninstall UsageBar.lnk" "$INSTDIR\Uninstall.exe"

    WriteRegStr HKCU "${INSTALL_REG_KEY}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "${INSTALL_REG_KEY}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "${INSTALL_REG_KEY}" "Publisher" "${COMPANY_NAME}"
    WriteRegStr HKCU "${INSTALL_REG_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${INSTALL_REG_KEY}" "DisplayIcon" "$INSTDIR\${EXE_NAME}"
    WriteRegStr HKCU "${INSTALL_REG_KEY}" "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegDWORD HKCU "${INSTALL_REG_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${INSTALL_REG_KEY}" "NoRepair" 1

    Exec "$INSTDIR\${EXE_NAME}"
SectionEnd

Section "Uninstall"
    Delete "$SMPROGRAMS\UsageBar\UsageBar.lnk"
    Delete "$SMPROGRAMS\UsageBar\Uninstall UsageBar.lnk"
    RMDir "$SMPROGRAMS\UsageBar"

    Delete "$INSTDIR\assets\AppIcon.ico"
    Delete "$INSTDIR\assets\AppIcon.png"
    RMDir "$INSTDIR\assets"

    Delete "$INSTDIR\${EXE_NAME}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"

    DeleteRegKey HKCU "${INSTALL_REG_KEY}"
SectionEnd
