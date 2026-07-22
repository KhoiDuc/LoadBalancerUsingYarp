# DEMO LOGIN - REDIS: 2 backend SimpleLogin (DataProtection dung chung qua Redis) + LB (5266).
# Ky vong: login o 1 node -> qua node khac VAN dang nhap (key chia se).
#
# Docker KHONG bat buoc (giong run-redis.ps1): co docker thi tu chay container; khong thi dung Redis local 127.0.0.1:6380.
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
    Write-Host ">> BUILD FAILED (exit $LASTEXITCODE). Dung lai." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ">> Start node1 (5001) & node2 (5002) - SimpleLogin REDIS"
$n1 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.SimpleLogin.Redis','--no-launch-profile','--','--NodeName','node1','--urls','http://localhost:5001' -PassThru
$n2 = Start-Process dotnet -ArgumentList 'run','--project','src/Backend.SimpleLogin.Redis','--no-launch-profile','--','--NodeName','node2','--urls','http://localhost:5002' -PassThru

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
    Write-Host ">> Load Balancer -> http://localhost:5266  (Ctrl+C de dung LB + backends; Redis van chay)"
    Write-Host "   Mo trinh duyet, bam Login, status GIU NGUYEN du doi node"
    dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266
} finally {
    Stop-Backends
}
