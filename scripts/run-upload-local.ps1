# Chay demo FileUpload LOCAL STORAGE (Luu o dia rieng tung node)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host ">> dotnet build..." -ForegroundColor Cyan
dotnet build -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ">> Start node1 (5001) & node2 (5002)" -ForegroundColor Green
$n1 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.FileUpload.Local','--no-launch-profile','--','--NodeName','node1','--urls','http://localhost:5001' -PassThru
$n2 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.FileUpload.Local','--no-launch-profile','--','--NodeName','node2','--urls','http://localhost:5002' -PassThru

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
    Write-Host "==============================================================================" -ForegroundColor Yellow
    Write-Host "  DEMO FILE UPLOAD LOCAL STORAGE: http://localhost:5266" -ForegroundColor Yellow
    Write-Host "  Huong dan test:" -ForegroundColor Cyan
    Write-Host "  1. Mo http://localhost:5266 va bam chon 1 anh de Upload" -ForegroundColor Cyan
    Write-Host "  2. Khi upload tai node1 -> 2 giay sau LB doi sang node2 -> ANH BI LOI / 404!" -ForegroundColor Red
    Write-Host "==============================================================================" -ForegroundColor Yellow
    Write-Host ""

    dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
} finally {
    Stop-Backends
}
