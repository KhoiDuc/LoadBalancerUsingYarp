# Luong REDIS tren Windows: Redis + 2 backend share state + load balancer (5266).
#
# Docker KHONG bat buoc:
#   - Neu may co 'docker' -> tu chay redis container (docker-compose.yml).
#   - Neu KHONG co docker -> bo qua, dung Redis LOCAL cua ban o 127.0.0.1:6380.
# Co the ep bang tham so: -UseDocker (bat buoc docker) hoac -NoDocker (bo qua du co docker).
[CmdletBinding()]
param(
    [switch]$UseDocker,
    [switch]$NoDocker,
    [string]$RedisHost = "127.0.0.1",
    [int]$RedisPort = 6380
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

if ($UseDocker -and -not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host ">> Ban truyen -UseDocker nhung khong tim thay 'docker'." -ForegroundColor Red
    exit 1
}

# Start Redis qua helper chung (Docker uu tien, fallback native).
$redisStart = @(
    "start",
    "-HostName", $RedisHost,
    "-Port", "$RedisPort"
)
if ($NoDocker) { $redisStart += "-NoDocker" }
& (Join-Path $PSScriptRoot "redis.ps1") @redisStart
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ">> dotnet build..."
dotnet build -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host ">> BUILD FAILED (exit $LASTEXITCODE). Dung lai, khong chay backend/LB." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ">> Start node1 (5001) & node2 (5002)"
$n1 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.Redis','--no-launch-profile','--','--NodeName','node1','--urls','http://localhost:5001' -PassThru
$n2 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.Redis','--no-launch-profile','--','--NodeName','node2','--urls','http://localhost:5002' -PassThru

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
    Write-Host ">> Load Balancer -> http://localhost:5266  (Ctrl+C de dung LB + backends; Redis van chay)"
    if ($ngrokInfo -and $ngrokInfo.Url) {
        Write-Host ">> Public Ngrok   -> $($ngrokInfo.Url)" -ForegroundColor Green
    }
    dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
} finally {
    Stop-Backends
    Stop-NgrokTunnel $ngrokInfo
}
