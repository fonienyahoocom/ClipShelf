@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_SETUP.ps1"
set "RESULT=%ERRORLEVEL%"
pause
exit /b %RESULT%
