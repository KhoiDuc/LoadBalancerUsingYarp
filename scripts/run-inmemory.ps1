# Luong IN-MEMORY tren Windows: 2 backend (node1:5001, node2:5002) + load balancer (5266). Khong can Redis.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host ">> dotnet build..."
dotnet build -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host ">> BUILD FAILED (exit $LASTEXITCODE). Dung lai, khong chay backend/LB." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ">> Start node1 (5001) & node2 (5002)"
$n1 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.InMemory','--no-launch-profile','--','--NodeName','node1','--urls','http://localhost:5001' -PassThru
$n2 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.InMemory','--no-launch-profile','--','--NodeName','node2','--urls','http://localhost:5002' -PassThru

function Stop-Backends {
    Write-Host ""
    Write-Host ">> Stopping backends..."
    foreach ($p in @($n1, $n2)) {
        if ($null -ne $p -and -not $p.HasExited) {
            & taskkill.exe /PID $p.Id /T /F 2>$null | Out-Null
        }
    }
}

. (Join-Path $PSScriptRoot "lib-ngrok.ps1")
$ngrokInfo = Ask-NgrokOption -Port 5266

try {
    Start-Sleep -Seconds 4
    Write-Host ">> Load Balancer -> http://localhost:5266  (Ctrl+C de dung LB + backends)"
    if ($ngrokInfo -and $ngrokInfo.Url) {
        Write-Host ">> Public Ngrok   -> $($ngrokInfo.Url)" -ForegroundColor Green
    }
    dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
} finally {
    Stop-Backends
    Stop-NgrokTunnel $ngrokInfo
}
