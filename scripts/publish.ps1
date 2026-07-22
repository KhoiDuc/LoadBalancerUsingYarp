# Publish TAT CA project ra ./publish (dung cho IIS). Dung ngay neu 1 buoc that bai.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$out = Join-Path (Get-Location) "publish"

function Invoke-Publish($proj, $dir) {
    Write-Host ">> Publish $proj -> $dir"
    dotnet publish $proj -c Release -o $dir
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host ">> PUBLISH FAILED cho $proj (exit $LASTEXITCODE). Dung lai." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host ">> Clean $out"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

Invoke-Publish "src/LoadBalancer"              "$out/lb"                    # YARP reverse proxy (LB)
Invoke-Publish "src/Backend.InMemory"          "$out/backend-inmemory"      # state in-memory tung node
Invoke-Publish "src/Backend.Redis"             "$out/backend-redis"         # session + DataProtection tren Redis
Invoke-Publish "src/Backend.SimpleLogin.InMemory" "$out/backend-login-inmemory" # demo login: key rieng tung node
Invoke-Publish "src/Backend.SimpleLogin.Redis"    "$out/backend-login-redis"    # demo login: key dung chung Redis

Write-Host ""
Write-Host ">> Xong. Output: $out"
Write-Host "   Tiep theo: chon backend variant (menu 6) roi Setup IIS LOCAL/SERVER."
