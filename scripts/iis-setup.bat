@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "BACKEND=redis"
:menu
cls
echo ==================================================
echo    YARP LB Lab - IIS setup
echo ==================================================
echo    Backend dang chon: %BACKEND%
echo --------------------------------------------------
echo    1. Publish tat ca project (dotnet publish)
echo    2. Setup IIS LOCAL   (YARP LB, self-signed HTTPS)
echo    3. Setup IIS SERVER  (all-in-one: LB+node1+node2)  [scenario 1]
echo    4. Setup IIS ARR     (LB bang IIS ARR de so sanh)
echo    5. Cleanup (xoa site/pool/farm)
echo    6. Doi backend variant
echo    9. Cleanup logs (xoa scripts\iis-setup-*.log)
echo --------------------------------------------------
echo    Scenario 3 (node o server, LB o may khac):
echo    7. Setup NODES only  (chay TREN SERVER, mo ra ngoai)
echo    8. Setup LB only     (chay TREN MAY LB, tro toi server)
echo --------------------------------------------------
echo    0. Thoat
echo.
set /p choice="Chon (0-9): "

if "%choice%"=="1" ( call :run publish.ps1 & goto menu )
if "%choice%"=="2" ( call :runAdmin setup-iis-local.ps1 -BackendVariant %BACKEND% -Https & goto menu )
if "%choice%"=="3" ( call :setupServer & goto menu )
if "%choice%"=="4" ( call :runAdmin setup-iis-arr.ps1 & goto menu )
if "%choice%"=="5" ( call :runAdmin cleanup-iis.ps1 & goto menu )
if "%choice%"=="6" ( call :pickBackend & goto menu )
if "%choice%"=="7" ( call :setupNodes & goto menu )
if "%choice%"=="8" ( call :setupLbRemote & goto menu )
if "%choice%"=="9" ( call :cleanupLogs & goto menu )
if "%choice%"=="0" ( exit /b )
goto menu

:pickBackend
echo.
echo    a. redis            (session + DataProtection tren Redis)
echo    b. inmemory         (state in-memory tung node)
echo    c. login-redis      (demo login, key dung chung Redis)
echo    d. login-inmemory   (demo login, key rieng tung node)
set /p b="Chon backend (a-d): "
if /i "%b%"=="a" set "BACKEND=redis"
if /i "%b%"=="b" set "BACKEND=inmemory"
if /i "%b%"=="c" set "BACKEND=login-redis"
if /i "%b%"=="d" set "BACKEND=login-inmemory"
exit /b

:askAppPath
echo.
echo    AppPath = folder publish app NGOAI lab (vd D:\Publish\ClientPaymentPortal).
echo    Enter trong = dung demo lab (BackendVariant=%BACKEND%).
set "APPPATH="
set /p APPPATH="AppPath: "
exit /b

:askLbMode
REM Enter = Affinity (an toan cho app ngoai). Go Stateless de test round-robin / nhay node.
echo.
echo    Mode LB: Enter = Affinity ^(sticky, mac dinh app ngoai^). Go Stateless de refresh nhay node.
set "LBMODE="
set /p LBMODE="Mode [Affinity/Stateless]: "
if /i "!LBMODE!"=="Stateless" ( set "LBMODE=Stateless" ) else ( set "LBMODE=Affinity" )
exit /b

:setupServer
call :askAppPath
if "!APPPATH!"=="" (
    call :runAdmin setup-iis-server.ps1 -BackendVariant %BACKEND%
) else (
    call :askLbMode
    call :runAdmin setup-iis-server.ps1 -AppPath "!APPPATH!" -Mode !LBMODE!
)
exit /b

:setupNodes
call :askAppPath
if "!APPPATH!"=="" (
    call :runAdmin setup-nodes.ps1 -BackendVariant %BACKEND% -OpenFirewall
) else (
    call :runAdmin setup-nodes.ps1 -AppPath "!APPPATH!" -OpenFirewall
)
exit /b

:setupLbRemote
echo.
echo    Nhap IP/host cua SERVER dang chay node1/node2 (vd 199.30.30.91).
set /p srv="Server host: "
if "%srv%"=="" ( echo Chua nhap host. & exit /b )
echo.
echo    Mode LB: Enter = Stateless ^(demo^). Go Affinity neu nodes la app publish ngoai.
set "LBMODE="
set /p LBMODE="Mode [Stateless/Affinity]: "
if /i "!LBMODE!"=="Affinity" (
    call :runAdmin setup-lb.ps1 -Node1 http://%srv%:5001/ -Node2 http://%srv%:5002/ -Mode Affinity
) else (
    call :runAdmin setup-lb.ps1 -Node1 http://%srv%:5001/ -Node2 http://%srv%:5002/ -Mode Stateless
)
exit /b

:cleanupLogs
echo.
set "n=0"
for %%F in ("%~dp0iis-setup-*.log") do (
  if exist "%%~F" (
    del /q "%%~F" 2>nul
    if not exist "%%~F" (
      echo    deleted %%~nxF
      set /a n+=1
    ) else (
      echo    khong xoa duoc %%~nxF ^(co the dang mo^)
    )
  )
)
if !n! equ 0 ( echo    Khong co iis-setup-*.log de xoa. ) else ( echo    >> Da xoa !n! file log. )
pause
exit /b

:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0%~1"
pause
exit /b

:runAdmin
REM Goi qua iis-run.ps1: no tu elevate (UAC), giu cua so + hien loi + ghi log.
REM %1 = ten script; %2..%9 = tham so. (Trong subroutine, %%* la args cua .bat goc — KHONG dung.)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0iis-run.ps1" %1 %2 %3 %4 %5 %6 %7 %8 %9
exit /b
