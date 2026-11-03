Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!define MUI_ICON "../ClassroomBoard/Assets/AppIcon.ico"
!define MUI_UNICON "../ClassroomBoard/Assets/AppIcon.ico"

!macro CheckDirectory PATH
  ; 目录联接可能指向其他资料，不能沿联接覆盖或删除文件。
  System::Call 'kernel32::GetFileAttributesW(w "${PATH}") i.r0'
  ${If} $0 != -1
    IntOp $0 $0 & 0x400
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONSTOP "检测到目录联接，操作已停止，请先核对：${PATH}" /SD IDOK
      SetErrorLevel 1
      Abort
    ${EndIf}
  ${EndIf}
!macroend

!ifdef QA_BUILD
  !define PRODUCT_ID "ClassroomBoard.QA"
  !define FOLDER "ClassGlow.QA"
  !define TITLE "班级看板 · 卸载验证"
!else
  !define PRODUCT_ID "ClassroomBoard"
  !define FOLDER "ClassGlow"
  !define TITLE "ClassGlow 班级看板"
!endif
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_ID}"
Name "${TITLE}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${FOLDER}"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
Var PreviousInstall
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName" "${TITLE}"
VIAddVersionKey /LANG=2052 "FileDescription" "当前用户安装与卸载程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "ClassGlow contributors"

!define MUI_ABORTWARNING
!define MUI_LICENSEPAGE_CHECKBOX
!define MUI_LICENSEPAGE_TEXT_TOP "请了解安装位置、本机变更和卸载范围。首次运行还会单独请求联网与本机数据处理同意。"
!define MUI_LICENSEPAGE_CHECKBOX_TEXT "我已阅读上述安装与本机变更说明"
!define MUI_WELCOMEPAGE_TEXT "本向导将安装班级看板。您可以选择安装位置和桌面快捷方式。$\r$\n$\r$\n更新前，请从系统托盘退出正在运行的班级看板。安装完成后不会自动启动程序。$\r$\n$\r$\n点击“下一步”继续。"
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_HEADER_TEXT "安装与隐私说明"
!define MUI_PAGE_HEADER_SUBTEXT "了解安装位置、本机变更及卸载范围。"
!define MUI_LICENSEPAGE_TEXT_BOTTOM "阅读后勾选下方选框，点击“下一步”继续。联网与展示权限将在首次运行时单独确认。"
!insertmacro MUI_PAGE_LICENSE "${LICENSE_FILE}"
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE VerifyInstallDirectory
!insertmacro MUI_PAGE_DIRECTORY
!define MUI_COMPONENTSPAGE_TEXT_TOP "选择是否在桌面创建快捷方式。开始菜单与卸载入口会自动添加。"
!define MUI_COMPONENTSPAGE_NODESC
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "安装完成。您可以从桌面或开始菜单打开班级看板。卸载程序位于安装目录，也可以在 Windows“已安装的应用”中卸载。"
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "将停止同步、恢复本程序仍占用的壁纸，删除本程序文件、本机凭证、缓存、日志、启动项、快捷方式及卸载登记。服务器教学内容和目录中另外放置的文件保留。"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "本程序已卸载。您另放的文件、其他目录中的安装包，以及 Windows 自行管理的系统缓存会保留。"
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  SetShellVarContext current
  ReadRegStr $PreviousInstall HKCU "${UNINSTALL_KEY}" "InstallLocation"
  !insertmacro CheckDirectory "$INSTDIR"
  !insertmacro CheckDirectory "$SMPROGRAMS\${FOLDER}"
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\${PRODUCT_ID}.CurrentUser") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONEXCLAMATION "请先从托盘退出正在运行的班级看板，再安装或更新。" /SD IDOK
    SetErrorLevel 2
    Abort
  ${EndIf}
FunctionEnd

Function VerifyInstallDirectory
  ; NSIS 的 GetFullPathName 会将尚不存在的目录变为空串；Win32 接口不要求目录已存在。
  System::Call 'kernel32::GetFullPathNameW(w "$INSTDIR", i ${NSIS_MAX_STRLEN}, w .r1, p 0) i.r0'
  ${If} $0 == 0
  ${OrIf} $0 >= ${NSIS_MAX_STRLEN}
    MessageBox MB_OK|MB_ICONINFORMATION "无法使用这个安装路径，请选择一个较短的文件夹路径。"
    Abort
  ${EndIf}
  StrCpy $INSTDIR $1
  ${GetRoot} "$INSTDIR" $0
  ; 使用专用文件夹，卸载时仍只删除清单内的程序文件。
  ${If} $INSTDIR == "$0\"
  ${OrIf} $INSTDIR == "$0"
  ${OrIf} $INSTDIR == "$DESKTOP"
  ${OrIf} $INSTDIR == "$DOCUMENTS"
    MessageBox MB_OK|MB_ICONINFORMATION "请选择一个专用文件夹，例如 D:\ClassGlow。"
    Abort
  ${EndIf}
  !insertmacro CheckDirectory "$INSTDIR"
  ${If} $PreviousInstall != ""
  ${AndIf} $PreviousInstall != $INSTDIR
    IfFileExists "$PreviousInstall\ClassroomBoard.exe" 0 install_path_ok
    MessageBox MB_OK|MB_ICONINFORMATION "已安装在 $PreviousInstall。升级请保留原位置；如需更换位置，请先卸载旧版，再选择新目录安装。"
    Abort
  ${EndIf}
  install_path_ok:
FunctionEnd

Section "班级看板（必需）"
  SectionIn RO
  SetShellVarContext current
  Call VerifyInstallDirectory
  !include "${INSTALL_FILES}"
  Delete "$INSTDIR\local-cleanup.complete"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\${FOLDER}"
  CreateShortcut "$SMPROGRAMS\${FOLDER}\班级看板.lnk" "$INSTDIR\ClassroomBoard.exe"
  CreateShortcut "$SMPROGRAMS\${FOLDER}\卸载班级看板.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${TITLE}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "ClassGlow"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\ClassroomBoard.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "创建桌面快捷方式"
  CreateShortcut "$DESKTOP\${TITLE}.lnk" "$INSTDIR\ClassroomBoard.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DesktopShortcut" "$DESKTOP\${TITLE}.lnk"
SectionEnd

Function un.onInit
  SetShellVarContext current
  ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
  ${If} $INSTDIR != $0
    MessageBox MB_OK|MB_ICONSTOP "卸载路径与本程序的当前用户安装目录不符，请从已安装的应用重新发起。" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  !insertmacro CheckDirectory "$INSTDIR"
  !insertmacro CheckDirectory "$SMPROGRAMS\${FOLDER}"
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  ; 先恢复与清理，成功后才移除运行文件。断点标记允许文件被占用时安全重试。
  IfFileExists "$INSTDIR\local-cleanup.complete" files
  StrCpy $1 ""
  IfSilent 0 +2
    StrCpy $1 " --silent"
  ClearErrors
  ExecWait '"$INSTDIR\ClassroomBoard.exe" --prepare-uninstall$1' $0
  IfErrors cleanup_failed
  ${If} $0 != 0
    cleanup_failed:
    MessageBox MB_OK|MB_ICONEXCLAMATION "卸载尚未完成，程序文件及卸载入口已保留。请处理壁纸恢复、运行中旧版本或联网提示后重试。" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  ClearErrors
  FileOpen $0 "$INSTDIR\local-cleanup.complete" w
  IfErrors cleanup_failed
  FileWrite $0 "local cleanup completed"
  FileClose $0
  files:
  StrCpy $2 0
  ; 逐个删除构建清单中的文件，不使用 RMDir /r 或通配符删除整个安装目录。
  !include "${UNINSTALL_FILES}"
  ${If} $2 != 0
    MessageBox MB_OK|MB_ICONEXCLAMATION "本机数据已清理，但部分程序文件仍被占用。请关闭占用程序，再次运行卸载。" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  Delete "$SMPROGRAMS\${FOLDER}\班级看板.lnk"
  Delete "$SMPROGRAMS\${FOLDER}\卸载班级看板.lnk"
  RMDir "$SMPROGRAMS\${FOLDER}"
  ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "DesktopShortcut"
  ${If} $0 == "$DESKTOP\${TITLE}.lnk"
    Delete "$0"
  ${EndIf}
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  Delete "$INSTDIR\local-cleanup.complete"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  SetErrorLevel 0
SectionEnd
