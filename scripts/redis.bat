@echo off
setlocal EnableExtensions
REM Wrapper goi scripts\redis.ps1 (menu Redis: start/stop/status/view/flush).
REM Vi du: redis.bat
REM        redis.bat start
REM        redis.bat view
REM        redis.bat flush -Force

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%redis.ps1"

if not exist "%PS1%" (
  echo [ERROR] Khong tim thay %PS1%
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
set "EC=%ERRORLEVEL%"
if not "%~1"=="" exit /b %EC%
pause
exit /b %EC%
