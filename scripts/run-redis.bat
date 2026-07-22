@echo off
REM Double-click de chay luong REDIS (Redis + 2 backend share state + YARP LB).
REM Can Redis o 127.0.0.1:6380 (docker compose up -d hoac Redis local).
cd /d "%~dp0.."
title yarp-lb-lab - REDIS
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-redis.ps1"
echo.
echo ===== Da dung. Nhan phim bat ky de dong. =====
pause >nul
