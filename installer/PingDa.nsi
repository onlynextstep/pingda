Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "FileFunc.nsh"
!ifndef PAYLOAD
!error "PAYLOAD is required"
!endif
!ifndef OUTPUT
!error "OUTPUT is required"
!endif
!define VERSION "0.6.1"
!ifdef TEST_BUILD
!define PRODUCT "屏搭安装验收"
!define KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\PingDa-InstallerTest"
!define FOLDER "PingDa-InstallerTest"
!define MUTEX_BASE "PingDa.InstallerTest"
!else
!define PRODUCT "屏搭"
!define KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\PingDa"
!define FOLDER "PingDa"
!define MUTEX_BASE "PingXu"
!endif
Name "${PRODUCT} ${VERSION} 内测版"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\${FOLDER}"
InstallDirRegKey HKCU "${KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
CRCCheck on
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "0.6.1.0"
VIAddVersionKey /LANG=2052 "ProductName" "屏搭"
VIAddVersionKey /LANG=2052 "FileDescription" "屏搭安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "0.6.1"
VIAddVersionKey /LANG=2052 "LegalCopyright" "屏搭"
Icon "${ICON}"
UninstallIcon "${ICON}"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安装屏搭"
!define MUI_WELCOMEPAGE_TEXT "多屏布局，一键切换。$\r$\n$\r$\n当前用户安装，无需管理员权限。已包含运行时，无需安装 .NET。$\r$\n$\r$\n升级前请从托盘右键退出屏搭。预设与设置会保留。$\r$\n$\r$\n这是未签名的内测版，硬件兼容性仍在验证。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!ifndef TEST_BUILD
!define MUI_FINISHPAGE_RUN "$INSTDIR\app\PingXu.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_FINISHPAGE_RUN_TEXT "打开屏搭"
!endif
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "将卸载屏搭程序。个人预设、屏幕名称和设置全部保留，再次安装后可继续使用。"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Var OldMoved
Var InstallLock

!macro CheckRunning PREFIX
Function ${PREFIX}CheckRunning
  ReadEnvStr $1 USERNAME
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\${MUTEX_BASE}.Desktop.$1") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONINFORMATION "请先从任务栏托盘右键退出屏搭，再继续安装或卸载。未关闭程序，也未修改文件。" /SD IDOK
    SetErrorLevel 10
    Quit
  ${EndIf}
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\${MUTEX_BASE}.DisplayTransaction.$1") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    MessageBox MB_OK|MB_ICONINFORMATION "显示切换尚未结束，请稍后重试。" /SD IDOK
    SetErrorLevel 11
    Quit
  ${EndIf}
FunctionEnd
!macroend
!insertmacro CheckRunning ""
!insertmacro CheckRunning "un."

!macro SafeTree PREFIX
Function ${PREFIX}CheckTree
  Exch $0
  Push $1
  Push $2
  Push $3
  Push $4
  System::Call 'kernel32::GetFileAttributesW(w r0) i.r3'
  IntOp $4 $3 & 0x400
  ${If} $4 != 0
    MessageBox MB_OK "安装目录包含链接或重解析点，已停止操作，未删除文件。" /SD IDOK
    SetErrorLevel 14
    Quit
  ${EndIf}
  FindFirst $1 $2 "$0\*"
tree_loop:
  StrCmp $2 "" tree_end
  StrCmp $2 "." tree_next
  StrCmp $2 ".." tree_next
  System::Call 'kernel32::GetFileAttributesW(w "$0\$2") i.r3'
  IntOp $4 $3 & 0x400
  ${If} $4 != 0
    MessageBox MB_OK "安装目录包含链接或重解析点，已停止操作，未删除文件。" /SD IDOK
    SetErrorLevel 14
    Quit
  ${EndIf}
  IntOp $4 $3 & 0x10
  ${If} $4 != 0
    Push "$0\$2"
    Call ${PREFIX}CheckTree
  ${EndIf}
tree_next:
  FindNext $1 $2
  Goto tree_loop
tree_end:
  FindClose $1
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Pop $0
FunctionEnd

Function ${PREFIX}CheckParents
  StrCpy $0 "$INSTDIR"
parent_loop:
  System::Call 'kernel32::GetFileAttributesW(w r0) i.r1'
  ${If} $1 != -1
    IntOp $1 $1 & 0x400
    ${If} $1 != 0
      SetErrorLevel 14
      Quit
    ${EndIf}
  ${EndIf}
  System::Call 'kernel32::GetFullPathNameW(w "$0\..", i ${NSIS_MAX_STRLEN}, w .r2, p 0) i.r1'
  StrCmp $2 $0 parents_end
  StrCmp $2 "" parents_end
  StrCpy $0 $2
  Goto parent_loop
parents_end:
FunctionEnd
!macroend
!insertmacro SafeTree ""
!insertmacro SafeTree "un."

Function .onInit
  SetShellVarContext current
  ; An in-app upgrade gives the parent time to release its single-instance mutex.
  ${GetParameters} $0
  ${GetOptions} $0 "/UPDATE" $1
  ${IfNot} ${Errors}
    StrCpy $2 0
    ReadEnvStr $3 USERNAME
    update_wait:
      System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\${MUTEX_BASE}.Desktop.$3") p.r0'
      ${If} $0 != 0
        System::Call 'kernel32::CloseHandle(p r0)'
        Sleep 200
        IntOp $2 $2 + 1
        ${If} $2 < 50
          Goto update_wait
        ${EndIf}
      ${EndIf}
  ${EndIf}
  ${IfNot} ${RunningX64}
    MessageBox MB_OK "此安装包仅支持 Windows x64。" /SD IDOK
    SetErrorLevel 12
    Quit
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK "需要 Windows 10 或更高版本。" /SD IDOK
    SetErrorLevel 12
    Quit
  ${EndIf}
  Call CheckRunning
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\PingDa.Setup") p.r0 ?e'
  Pop $1
  StrCpy $InstallLock $0
  ${If} $0 == 0
    SetErrorLevel 13
    Quit
  ${EndIf}
  ${If} $1 == 183
    SetErrorLevel 13
    Quit
  ${EndIf}
FunctionEnd

Section "屏搭" SEC_MAIN
  Call CheckRunning
  ; Never adopt an existing foreign directory or follow reparse points.
  System::Call 'kernel32::GetFullPathNameW(w "$INSTDIR", i ${NSIS_MAX_STRLEN}, w .r0, p 0) i.r1'
  StrCpy $INSTDIR $0
  ${If} $INSTDIR == ""
    Abort
  ${EndIf}
  IfFileExists "$INSTDIR\*.*" 0 empty_dir
  ReadINIStr $0 "$INSTDIR\pingda-install.ini" "Install" "Product"
  StrCmp $0 "PingDa" empty_dir
  MessageBox MB_OK "所选目录已有其他文件，请选择新的空目录。" /SD IDOK
  SetErrorLevel 14
  Quit
empty_dir:
  System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i.r0'
  IntOp $0 $0 & 0x400
  ${If} $0 != 0
    IfFileExists "$INSTDIR\*.*" 0 safe_root
    SetErrorLevel 14
    Quit
  ${EndIf}
safe_root:
  Call CheckParents
  IfFileExists "$INSTDIR\*.*" 0 checked_tree
  Push "$INSTDIR"
  Call CheckTree
checked_tree:
  IfFileExists "$INSTDIR\app-next\*.*" blocked_stage
  IfFileExists "$INSTDIR\app-previous\*.*" blocked_stage
  StrCpy $OldMoved "0"
  SetOutPath "$INSTDIR\app-next"
  ClearErrors
  File /r "${PAYLOAD}\*.*"
  IfErrors failed_extract
  WriteUninstaller "$INSTDIR\app-next\Uninstall.exe"
  IfErrors failed_extract
  ClearErrors
  ExecWait '"$INSTDIR\app-next\PingXu.exe" --verify-install' $0
  IfErrors failed_extract
  ${If} $0 != 0
    Goto failed_extract
  ${EndIf}
  Call CheckRunning
  SetOutPath "$INSTDIR"
  IfFileExists "$INSTDIR\app\PingXu.exe" 0 activate
  ; Refuse upgrades over modified/foreign payloads rather than delete unknown user files.
  ClearErrors
  ExecWait '"$INSTDIR\app\PingXu.exe" --verify-install' $0
  IfErrors failed_extract
  ${If} $0 != 0
    Goto failed_extract
  ${EndIf}
  ClearErrors
  Rename "$INSTDIR\app" "$INSTDIR\app-previous"
  IfErrors failed_extract
  StrCpy $OldMoved "1"
activate:
  ClearErrors
  Rename "$INSTDIR\app-next" "$INSTDIR\app"
  IfErrors rollback
!ifdef TEST_BUILD
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/FAILACTIVATE" $1
  IfErrors +2
  Goto activation_failed
!endif
  ClearErrors
  ExecWait '"$INSTDIR\app\PingXu.exe" --verify-install' $0
  IfErrors activation_failed
  ${If} $0 != 0
activation_failed:
    ClearErrors
    Rename "$INSTDIR\app" "$INSTDIR\app-next"
    IfErrors rollback_failed
    Goto rollback
  ${EndIf}
  WriteINIStr "$INSTDIR\pingda-install.ini" "Install" "Product" "PingDa"
  WriteINIStr "$INSTDIR\pingda-install.ini" "Install" "Version" "${VERSION}"
  CreateDirectory "$SMPROGRAMS\${PRODUCT}"
  CreateShortcut "$SMPROGRAMS\${PRODUCT}\${PRODUCT}.lnk" "$INSTDIR\app\PingXu.exe" "" "$INSTDIR\app\PingXu.exe"
  CreateShortcut "$DESKTOP\${PRODUCT}.lnk" "$INSTDIR\app\PingXu.exe" "" "$INSTDIR\app\PingXu.exe"
  WriteRegStr HKCU "${KEY}" "DisplayName" "${PRODUCT}"
  WriteRegStr HKCU "${KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${KEY}" "DisplayIcon" "$INSTDIR\app\PingXu.exe"
  WriteRegStr HKCU "${KEY}" "UninstallString" '$\"$INSTDIR\app\Uninstall.exe$\"'
  WriteRegStr HKCU "${KEY}" "QuietUninstallString" '$\"$INSTDIR\app\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "${KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${KEY}" "NoRepair" 1
  ; Only installer-owned sibling created by the rename above can be removed here.
  ${If} $OldMoved == "1"
    RMDir /r "$INSTDIR\app-previous"
  ${EndIf}
  SetErrorLevel 0
  Goto done
rollback:
  ${If} $OldMoved == "1"
    ClearErrors
    Rename "$INSTDIR\app-previous" "$INSTDIR\app"
    IfErrors rollback_failed
  ${EndIf}
failed_extract:
  SetOutPath "$TEMP"
  RMDir /r "$INSTDIR\app-next"
  MessageBox MB_OK|MB_ICONSTOP "安装未完成，原程序和个人配置未覆盖。请检查磁盘空间或文件占用后重试。" /SD IDOK
  SetErrorLevel 20
  Quit
rollback_failed:
  MessageBox MB_OK|MB_ICONSTOP "安装失败且自动恢复未完成。旧程序保留在 app-previous，请勿删除安装目录，联系支持恢复。个人配置未修改。" /SD IDOK
  SetErrorLevel 22
  Quit
blocked_stage:
  MessageBox MB_OK "发现上次未完成安装的暂存目录，未删除任何文件。请保留该目录并联系支持。" /SD IDOK
  SetErrorLevel 21
  Quit
done:
SectionEnd

Function un.onInit
  SetShellVarContext current
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\PingDa.Setup") p.r0 ?e'
  Pop $1
  StrCpy $InstallLock $0
  ${If} $0 == 0
    SetErrorLevel 13
    Quit
  ${EndIf}
  ${If} $1 == 183
    SetErrorLevel 13
    Quit
  ${EndIf}
  Call un.CheckRunning
  ; The uninstaller lives in app; obtain and validate its owned parent.
  GetFullPathName $INSTDIR "$INSTDIR\.."
  ReadINIStr $0 "$INSTDIR\pingda-install.ini" "Install" "Product"
  ${If} $0 != "PingDa"
    MessageBox MB_OK "安装目录标记无效，已停止卸载，未删除文件。" /SD IDOK
    SetErrorLevel 14
    Quit
  ${EndIf}
  Call un.CheckParents
  Push "$INSTDIR"
  Call un.CheckTree
FunctionEnd

Section "Uninstall"
  Call un.CheckRunning
  SetOutPath "$TEMP"
  ; Remove only files compiled into this package, not arbitrary files under the directory.
  !include "${DELETE_LIST}"
  Delete "$INSTDIR\app\Uninstall.exe"
  RMDir "$INSTDIR\app"
  Delete "$INSTDIR\pingda-install.ini"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${PRODUCT}\${PRODUCT}.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT}"
  Delete "$DESKTOP\${PRODUCT}.lnk"
  DeleteRegKey HKCU "${KEY}"
  SetErrorLevel 0
SectionEnd
