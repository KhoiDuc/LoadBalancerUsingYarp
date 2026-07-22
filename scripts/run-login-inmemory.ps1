# DEMO LOGIN - IN-MEMORY: 2 backend SimpleLogin (key rieng tung node) + LB (5266).
# Ky vong: login o 1 node -> qua node khac MAT dang nhap (key khong chia se).
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host ">> dotnet build..."
dotnet build -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host ">> BUILD FAILED (exit $LASTEXITCODE). Dung lai." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ">> Start node1 (5001) & node2 (5002) - SimpleLogin IN-MEMORY"
$n1 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.SimpleLogin.InMemory','--no-launch-profile','--','--NodeName','node1','--urls','http://localhost:5001' -PassThru
$n2 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.SimpleLogin.InMemory','--no-launch-profile','--','--NodeName','node2','--urls','http://localhost:5002' -PassThru

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
    Start-Sleep -Seconds 4
    Write-Host ">> Load Balancer -> http://localhost:5266  (Ctrl+C de dung LB + backends)"
    Write-Host "   Mo trinh duyet, bam Login, xem status doi theo node"
    dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
} finally {
    Stop-Backends
}
