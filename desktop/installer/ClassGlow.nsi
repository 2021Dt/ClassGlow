Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
!include "MUI2.nsh"
!include "LogicLib.nsh"

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
VIProductVersion "0.3.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "${TITLE}"
VIAddVersionKey /LANG=2052 "FileDescription" "当前用户安装与卸载程序"
VIAddVersionKey /LANG=2052 "FileVersion" "0.3.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "ClassGlow contributors"

!define MUI_ABORTWARNING
!define MUI_LICENSEPAGE_CHECKBOX
!define MUI_LICENSEPAGE_TEXT_TOP "请了解安装位置、本机变更和卸载范围。首次运行还会单独请求联网与本机数据处理同意。"
!define MUI_LICENSEPAGE_CHECKBOX_TEXT "我已阅读上述安装与本机变更说明"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "privacy.txt"
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "安装完成。请从开始菜单手动打开程序并阅读首次隐私说明。默认不联网、不启用启动项、不更换壁纸。"
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "将停止同步、恢复本程序仍占用的壁纸，删除本程序文件、本机凭证、缓存、日志、启动项、快捷方式及卸载登记。服务器教学内容和目录中另外放置的文件保留。"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!define MUI_FINISHPAGE_TEXT "本程序已卸载。您另放的文件、其他目录中的安装包，以及 Windows 自行管理的系统缓存会保留。"
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  SetShellVarContext current
  ; 固定当前用户专用目录，不允许 /D 将安装目标改成桌面、盘符根目录等位置。
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\${FOLDER}"
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

Section "安装"
  SetShellVarContext current
  !include "${INSTALL_FILES}"
  Delete "$INSTDIR\local-cleanup.complete"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\${FOLDER}"
  CreateShortcut "$SMPROGRAMS\${FOLDER}\班级看板.lnk" "$INSTDIR\ClassroomBoard.exe"
  CreateShortcut "$SMPROGRAMS\${FOLDER}\卸载班级看板.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${TITLE}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "0.3.0"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "ClassGlow"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\ClassroomBoard.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Function un.onInit
  SetShellVarContext current
  ${If} $INSTDIR != "$LOCALAPPDATA\Programs\${FOLDER}"
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
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  Delete "$INSTDIR\local-cleanup.complete"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  SetErrorLevel 0
SectionEnd
