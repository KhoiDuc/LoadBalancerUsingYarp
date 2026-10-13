# Chay demo FileUpload SHARED STORAGE (Dung chung thu muc giua 2 node)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host ">> dotnet build..." -ForegroundColor Cyan
dotnet build -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ">> Start node1 (5001) & node2 (5002)" -ForegroundColor Green
$n1 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.FileUpload.Shared','--no-launch-profile','--','--NodeName','node1','--urls','http://localhost:5001' -PassThru
$n2 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.FileUpload.Shared','--no-launch-profile','--','--NodeName','node2','--urls','http://localhost:5002' -PassThru

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
    Write-Host "  DEMO FILE UPLOAD SHARED STORAGE: http://localhost:5266" -ForegroundColor Green
    Write-Host "  Huong dan test:" -ForegroundColor Cyan
    Write-Host "  1. Mo http://localhost:5266 va bam chon 1 anh de Upload" -ForegroundColor Cyan
    Write-Host "  2. Sau khi upload -> du trang refresh doi sang node nao thi ANH VAN HIEN THI CHINH XAC!" -ForegroundColor Green
    Write-Host "==============================================================================" -ForegroundColor Green
    Write-Host ""

    dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
} finally {
    Stop-Backends
}
