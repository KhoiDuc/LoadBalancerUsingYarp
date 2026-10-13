@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "BACKEND=redis"

:menu
cls
echo ==============================================================================
echo                      YARP LOAD BALANCER LAB - IIS SETUP
echo ==============================================================================
echo  [Trang thai] Kich ban backend demo dang chon: [%BACKEND%]
echo ==============================================================================
echo  --- NHOM 1: MO HINH 1 MAY (LB + CAC NODE CHAY CHUNG) ---
echo.
echo    1. Publish tat ca cac project
echo       ^> Y nghia: Build va dong goi 5 project sang thu muc ./publish
echo       ^> Khi nao dung: Chay lan dau tien hoac sau khi sua code C# trong src.
echo.
echo    2. Setup IIS LOCAL (Lab test nhanh tren may ca nhan)
echo       ^> Y nghia: Tao site node1 (5001), node2 (5002) va LB (5266 kem HTTPS test)
echo       ^> Khi nao dung: Muon xem hoat dong tren IIS may ca nhan.
echo.
echo    3. Setup IIS SERVER (Moi truong chuan - All-in-one) [KHUYEN NGHI]
echo       ^> Y nghia: Tao 2 node (5001, 5002) va YARP LB (5266 mo ra ngoai).
echo       ^> DAC BIET: Ho tro deploy APP NGOAI (nhap AppPath + tuy chon CLONE).
echo.
echo    4. Setup IIS ARR (Application Request Routing de so sanh)
echo       ^> Y nghia: Dung Load Balancer truyen thong cua IIS thay cho YARP.
echo.
echo    5. Cleanup IIS
echo       ^> Y nghia: Xoa sach website va App Pool (node1, node2, yarp-lb) khoi IIS.
echo.
echo    6. Doi kich ban demo (redis / inmemory / login-redis / login-inmemory)
echo       ^> Y nghia: Chuyen giua test Session va test Login trong app demo.
echo.
echo  --- NHOM 2: MO HINH TACH BIET 2 MAY (SCENARIO 3) ---
echo.
echo    7. Setup NODES tren Server (Chi tao 2 node, khong tao LB)
echo       ^> Y nghia: Chay tren may chu Backend, mo port 5001 va 5002 ra ngoai.
echo       ^> DAC BIET: Ho tro deploy APP NGOAI (nhap AppPath + tuy chon CLONE).
echo.
echo    8. Setup LB tren may Gateway / May local
echo       ^> Y nghia: Chay tren may dat Load Balancer, tro IP ve may server o muc 7.
echo.
echo  --- TIEN ICH KHAC ---
echo.
echo    9. Cleanup logs (Xoa cac file scripts\iis-setup-*.log)
echo    A. AppDeploy Toolkit Hub (Mo menu D:\Projects\AppDeploy de build/publish/deploy)
echo    E. Run External App (Chay app ngoai voi YARP LB bang dotnet CLI - Khong can IIS)
echo    0. Thoat
echo ==============================================================================
echo.
set /p choice="Nhap lua chon cua ban (0-9, A, E): "

if "%choice%"=="1" ( call :run publish.ps1 & goto menu )
if "%choice%"=="2" ( call :runAdmin setup-iis-local.ps1 -BackendVariant %BACKEND% -Https & goto menu )
if "%choice%"=="3" ( call :setupServer & goto menu )
if "%choice%"=="4" ( call :runAdmin setup-iis-arr.ps1 & goto menu )
if "%choice%"=="5" ( call :runAdmin cleanup-iis.ps1 & goto menu )
if "%choice%"=="6" ( call :pickBackend & goto menu )
if "%choice%"=="7" ( call :setupNodes & goto menu )
if "%choice%"=="8" ( call :setupLbRemote & goto menu )
if "%choice%"=="9" ( call :cleanupLogs & goto menu )
if /i "%choice%"=="A" ( call :openAppDeploy & goto menu )
if /i "%choice%"=="E" ( call :runExternal & goto menu )
if "%choice%"=="0" ( exit /b )
goto menu

:openAppDeploy
cls
echo ==============================================================================
echo                      MO APPDEPLOY TOOLKIT HUB
echo ==============================================================================
if exist "%~dp0..\..\AppDeploy\run.bat" (
    call "%~dp0..\..\AppDeploy\run.bat"
) else if exist "D:\Projects\AppDeploy\run.bat" (
    call "D:\Projects\AppDeploy\run.bat"
) else (
    echo [ERROR] Khong tim thay AppDeploy toolkit tai D:\Projects\AppDeploy\run.bat!
    pause
)
exit /b

:runExternal
cls
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-external.ps1"
pause
exit /b

:pickBackend
echo.
echo ------------------------------------------------------------------------------
echo  CHON BIEN THE BACKEND DEMO:
echo    a. redis            : Session + DataProtection chung tren Redis (giu state)
echo    b. inmemory         : Session rieng tung node (nhay node bi lech session)
echo    c. login-redis      : Demo Login co Redis (doi node VAN GIU DANG NHAP)
echo    d. login-inmemory   : Demo Login khong Redis (doi node BI VANG DANG NHAP)
echo    e. signalr-redis    : Chat Realtime co Redis Backplane (nhan tin xuyen suot)
echo    f. signalr-inmemory : Chat Realtime khong Backplane (khac node khong nhan duoc)
echo    g. upload-shared    : File Upload dung Shared Folder (anh hien thi tren moi node)
echo    h. upload-local     : File Upload dung o dia local (doi node anh bi loi 404)
echo ------------------------------------------------------------------------------
set /p b="Chon backend (a-h): "
if /i "%b%"=="a" set "BACKEND=redis"
if /i "%b%"=="b" set "BACKEND=inmemory"
if /i "%b%"=="c" set "BACKEND=login-redis"
if /i "%b%"=="d" set "BACKEND=login-inmemory"
if /i "%b%"=="e" set "BACKEND=signalr-redis"
if /i "%b%"=="f" set "BACKEND=signalr-inmemory"
if /i "%b%"=="g" set "BACKEND=upload-shared"
if /i "%b%"=="h" set "BACKEND=upload-local"
exit /b

:askAppPath
set "APPPATH="
set "CLONE_FLAG="
if exist "%~dp0.selected_app.tmp" del "%~dp0.selected_app.tmp"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pick-app.ps1"
if exist "%~dp0.selected_app.tmp" (
    set /p APPPATH=<"%~dp0.selected_app.tmp"
    del "%~dp0.selected_app.tmp"
)
if "!APPPATH!"=="" exit /b

echo.
echo  Ban co muon CLONE ra 2 folder rieng (_node1 va _node2) khong?
echo    [1] CO - Clone ra 2 folder (KHUYEN NGHI: Tranh loi xung dot file log, cache)
echo    [2] KHONG - Dung chung 1 folder goc (Tiet kiem o cung)
set "CLONE_OPT=1"
set /p CLONE_OPT="Chon [1 hoac 2, Enter = 1]: "
if "!CLONE_OPT!"=="2" (
    set "CLONE_FLAG="
) else (
    set "CLONE_FLAG=-CloneFolders"
)
exit /b

:askLbMode
echo.
echo ------------------------------------------------------------------------------
echo  CHON CHE DO LOAD BALANCER:
echo    - Affinity  (Sticky Session): Moi user 'dinh' chat 1 node (AN TOAN cho app ngoai)
echo    - Stateless (Round-Robin)   : Chia deu request lu luan chuyen giua node1 va node2
echo.
set "LBMODE="
set /p LBMODE=">> Nhap Mode [Enter = Affinity / hoac go Stateless]: "
if /i "!LBMODE!"=="Stateless" ( set "LBMODE=Stateless" ) else ( set "LBMODE=Affinity" )
exit /b

:setupServer
call :askAppPath
if "!APPPATH!"=="" (
    call :runAdmin setup-iis-server.ps1 -BackendVariant %BACKEND%
) else (
    call :askLbMode
    call :runAdmin setup-iis-server.ps1 -AppPath "!APPPATH!" !CLONE_FLAG! -Mode !LBMODE!
)
exit /b

:setupNodes
call :askAppPath
if "!APPPATH!"=="" (
    call :runAdmin setup-nodes.ps1 -BackendVariant %BACKEND% -OpenFirewall
) else (
    call :askLbMode
    call :runAdmin setup-nodes.ps1 -AppPath "!APPPATH!" !CLONE_FLAG! -OpenFirewall
)
exit /b

:setupLbRemote
echo.
echo ------------------------------------------------------------------------------
echo  CAU HINH LOAD BALANCER REMOTE
echo ------------------------------------------------------------------------------
echo  Nhap IP/host cua SERVER dang chay node1/node2 (vd 199.30.30.91).
set /p srv=">> Server host: "
if "%srv%"=="" ( echo Chua nhap host. & exit /b )
call :askLbMode
call :runAdmin setup-lb.ps1 -Node1 http://%srv%:5001/ -Node2 http://%srv%:5002/ -Mode !LBMODE!
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
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0iis-run.ps1" %1 %2 %3 %4 %5 %6 %7 %8 %9
exit /b
