# Chay demo SignalR voi Redis Backplane
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

# Kiem tra / Bat Redis
Write-Host ">> Kiem tra Redis..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "redis.ps1") start
if ($LASTEXITCODE -ne 0) {
    Write-Host ">> Khong the khoi dong Redis. Hay dam bao Docker hoac Redis service dang chay!" -ForegroundColor Red
    exit 1
}

Write-Host ">> dotnet build..." -ForegroundColor Cyan
dotnet build -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ">> Start node1 (5001) & node2 (5002)" -ForegroundColor Green
$n1 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.SignalR.Redis','--no-launch-profile','--','--NodeName','node1','--urls','http://localhost:5001' -PassThru
$n2 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.SignalR.Redis','--no-launch-profile','--','--NodeName','node2','--urls','http://localhost:5002' -PassThru

function Stop-Backends {
    Write-Host ""
    Write-Host ">> Stopping backends..."
    foreach ($p in @($n1, $n2)) {
        if ($null -ne $p -and -not $p.HasExited) {
            & taskkill.exe /PID $p.Id /T /F 2>$null | Out-Null
        }
    }
}

try {
    Start-Sleep -Seconds 3
    Write-Host ""
    Write-Host "==============================================================================" -ForegroundColor Green
    Write-Host "  DEMO SIGNALR REDIS BACKPLANE: http://localhost:5266" -ForegroundColor Green
    Write-Host "  Huong dan test:" -ForegroundColor Cyan
    Write-Host "  1. Mo 1 tab tai: http://localhost:5001 (node1)" -ForegroundColor Cyan
    Write-Host "  2. Mo 1 tab khac tai: http://localhost:5002 (node2)" -ForegroundColor Cyan
    Write-Host "  3. Chat o tab node1 -> Tab node2 NHAN DUOC TUC THI nho Redis Backplane!" -ForegroundColor Green
    Write-Host "==============================================================================" -ForegroundColor Green
    Write-Host ""

    dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
} finally {
    Stop-Backends
}
