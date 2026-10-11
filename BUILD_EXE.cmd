@echo off
setlocal EnableExtensions
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%BUILD_EXE.ps1"
set "BOOT_FAIL=%SCRIPT_DIR%BUILD_FAILED_SEND_TO_CHATGPT.txt"

cd /d "%SCRIPT_DIR%"

echo ================================================
echo ClipShelf v2.15.2 - BUILD HARNESS 3
echo ================================================
echo.

if not exist "%PS_SCRIPT%" (
  >"%BOOT_FAIL%" echo ClipShelf BUILD LAUNCHER FAILED
  >>"%BOOT_FAIL%" echo ================================================
  >>"%BOOT_FAIL%" echo BUILD_EXE.ps1 is missing from the package.
  >>"%BOOT_FAIL%" echo Script directory: %SCRIPT_DIR%
  echo BUILD FAILED
  echo BUILD_EXE.ps1 is missing.
  start "" notepad.exe "%BOOT_FAIL%"
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%"
set "BUILD_EXIT=%ERRORLEVEL%"

if not "%BUILD_EXIT%"=="0" (
  echo.
  echo BUILD FAILED
  echo The PowerShell build harness reported the diagnostic file.
  echo If Notepad opened a build-failure report, send that file to ChatGPT.
  echo.
  pause
  exit /b %BUILD_EXIT%
)

echo.
echo BUILD SUCCESS
echo.
pause
exit /b 0
