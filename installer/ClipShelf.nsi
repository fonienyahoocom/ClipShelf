Unicode true
Target amd64-unicode
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
Name "ClipShelf 剪貼庫"
!ifndef OUTPUT_FILE
!define OUTPUT_FILE "../../deliverables/ClipShelf-2.15.3-Setup.exe"
!endif
!ifndef PAYLOAD_DIR
!define PAYLOAD_DIR "../release"
!endif
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\ClipShelf"
InstallDirRegKey HKCU "Software\ClipShelf\Installer" "Path"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "ClipShelf · 本機剪貼庫"
VIProductVersion "2.15.3.0"
VIAddVersionKey /LANG=1028 "ProductName" "ClipShelf"
VIAddVersionKey /LANG=1028 "FileDescription" "ClipShelf Setup"
VIAddVersionKey /LANG=1028 "FileVersion" "2.15.3.0"
VIAddVersionKey /LANG=1028 "ProductVersion" "2.15.3"
VIAddVersionKey /LANG=1028 "LegalCopyright" "ClipShelf"
!define MUI_ICON "../ClipShelf/clipshelf.ico"
!define MUI_UNICON "../ClipShelf/clipshelf.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "安裝 ClipShelf 剪貼庫"
!define MUI_WELCOMEPAGE_TEXT "搜尋、整理與再次使用你的文字和檔案。$\r$\n$\r$\n本安裝包包含執行環境，不需另行編譯。$\r$\n$\r$\n更新前請從系統匣退出舊版 ClipShelf。既有收藏、分類與歷史資料會保留。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\ClipShelf.exe"
!define MUI_FINISHPAGE_RUN_TEXT "開啟 ClipShelf"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "TradChinese"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "此版本需要 64 位元 Windows 10 或更新版本。"
    Abort
  ${EndIf}
  SetShellVarContext current
  SetRegView 64
  ReadRegStr $0 HKCU "Software\ClipShelf\Installer" "Version"
  ${If} $0 != ""
    ; Existing choice is preserved when upgrading.
    IfFileExists "$SMSTARTUP\ClipShelf.lnk" 0 +2
      SectionSetFlags 2 1
  ${EndIf}
FunctionEnd

!macro CheckClosed PREFIX
Function ${PREFIX}CheckClosed
retry:
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\ClipShelf.Core.SingleInstance") p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    IfSilent blocked
    MessageBox MB_RETRYCANCEL|MB_ICONINFORMATION "ClipShelf 還在執行。請在右下角系統匣的 ClipShelf 圖示選擇「退出」，再按重試。$\r$\n$\r$\n安裝程式不會強制結束有未儲存資料的程式。" IDRETRY retry
blocked:
    SetErrorLevel 2
    Abort
  ${EndIf}
FunctionEnd
!macroend
!insertmacro CheckClosed ""
!insertmacro CheckClosed "un."

Section "ClipShelf 主程式（必要）" SEC_MAIN
  SectionIn RO
  Call CheckClosed
  SetOutPath "$INSTDIR"
  SetOverwrite on
  ClearErrors
  File /r "${PAYLOAD_DIR}\*"
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "有程式檔無法更新。請確認程式已退出、磁碟空間足夠，再重新執行安裝。個人資料未被移除。"
    SetErrorLevel 3
    Abort
  ${EndIf}
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\ClipShelf"
  CreateShortcut "$SMPROGRAMS\ClipShelf\ClipShelf.lnk" "$INSTDIR\ClipShelf.exe"
  CreateShortcut "$SMPROGRAMS\ClipShelf\解除安裝.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\ClipShelf\Installer" "Path" "$INSTDIR"
  WriteRegStr HKCU "Software\ClipShelf\Installer" "Version" "2.15.3"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf" "DisplayName" "ClipShelf 剪貼庫"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf" "DisplayVersion" "2.15.3"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf" "DisplayIcon" "$INSTDIR\ClipShelf.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf" "NoRepair" 1
SectionEnd
Section "桌面捷徑" SEC_DESKTOP
  CreateShortcut "$DESKTOP\ClipShelf.lnk" "$INSTDIR\ClipShelf.exe"
SectionEnd
Section /o "登入 Windows 時自動啟動" SEC_STARTUP
  CreateShortcut "$SMSTARTUP\ClipShelf.lnk" "$INSTDIR\ClipShelf.exe" "--background"
SectionEnd
Section "Uninstall"
  SetShellVarContext current
  SetRegView 64
  Call un.CheckClosed
  !include "uninstall-files.nsh"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$DESKTOP\ClipShelf.lnk"
  Delete "$SMSTARTUP\ClipShelf.lnk"
  Delete "$SMPROGRAMS\ClipShelf\ClipShelf.lnk"
  Delete "$SMPROGRAMS\ClipShelf\解除安裝.lnk"
  RMDir "$SMPROGRAMS\ClipShelf"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipShelf"
  DeleteRegKey HKCU "Software\ClipShelf\Installer"
  ; Deliberately no removal of LOCALAPPDATA\ClipShelf or custom storage.
SectionEnd
