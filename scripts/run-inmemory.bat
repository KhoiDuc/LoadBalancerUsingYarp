@echo off
REM Double-click de chay luong IN-MEMORY (2 backend + YARP LB), khong can Redis.
cd /d "%~dp0.."
title yarp-lb-lab - IN-MEMORY
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-inmemory.ps1"
echo.
echo ===== Da dung. Nhan phim bat ky de dong. =====
pause >nul
