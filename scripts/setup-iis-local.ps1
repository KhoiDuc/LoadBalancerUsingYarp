#Requires -RunAsAdministrator
# Setup IIS LOCAL: 3 site (yarp-lb:5266, node1:5001, node2:5002), YARP lam LB.
# Env = Development -> backend doc appsettings.json (Redis 127.0.0.1:6380 neu la ban redis).
[CmdletBinding()]
param(
    [ValidateSet("redis","inmemory","login-redis","login-inmemory")]
    [string]$BackendVariant = "redis",
    [string]$EnvName = "Development",
    [switch]$Https,
    [int]$HttpsPort = 5443
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$pub  = Join-Path $root "publish"
$lbDir = Join-Path $pub "lb"
$backendDir = switch ($BackendVariant) {
    "redis"          { Join-Path $pub "backend-redis" }
    "inmemory"       { Join-Path $pub "backend-inmemory" }
    "login-redis"    { Join-Path $pub "backend-login-redis" }
    "login-inmemory" { Join-Path $pub "backend-login-inmemory" }
}

if (-not (Test-Path $lbDir) -or -not (Test-Path $backendDir)) {
    throw "Chua publish (thieu '$lbDir' hoac '$backendDir'). Chay menu 1 (publish.ps1) truoc."
}

# Cap quyen doc cho IIS_IUSRS len ca thu muc publish (fix HTTP 500.19 / 0x80070005
# khi publish nam trong thu muc user nhu C:\Users\...\Downloads). SID *S-1-5-32-568 = IIS_IUSRS.
Write-Host ">> Cap quyen doc cho IIS_IUSRS tren: $pub"
icacls $pub /grant "*S-1-5-32-568:(OI)(CI)RX" /T /C | Out-Null

function New-AppSite {
    param($Name,$Ip,$Port,$PhysicalPath,[hashtable]$EnvVars)
    if (Get-Website -Name $Name -ErrorAction SilentlyContinue) { Remove-Website -Name $Name }
    if (Test-Path "IIS:\AppPools\$Name") { Remove-WebAppPool -Name $Name }

    New-WebAppPool -Name $Name | Out-Null
    Set-ItemProperty "IIS:\AppPools\$Name" -Name managedRuntimeVersion -Value ""      # No Managed Code
    Set-ItemProperty "IIS:\AppPools\$Name" -Name startMode -Value "AlwaysRunning"
    Set-ItemProperty "IIS:\AppPools\$Name" -Name processModel.idleTimeout -Value "00:00:00"

    New-Website -Name $Name -IPAddress $Ip -Port $Port -PhysicalPath $PhysicalPath -ApplicationPool $Name -Force | Out-Null

    foreach ($k in $EnvVars.Keys) {
        Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' `
            -filter "system.applicationHost/applicationPools/add[@name='$Name']/environmentVariables" `
            -name "." -value @{ name = $k; value = $EnvVars[$k] }
    }
    Write-Host "   [ok] $Name -> $Ip`:$Port  ($PhysicalPath)"
}

Write-Host ">> Backend variant: $BackendVariant  ($backendDir)"
Write-Host ">> Tao cac site..."
New-AppSite -Name "node1"   -Ip "*" -Port 5001 -PhysicalPath $backendDir -EnvVars @{ NodeName="node1"; ASPNETCORE_ENVIRONMENT=$EnvName }
New-AppSite -Name "node2"   -Ip "*" -Port 5002 -PhysicalPath $backendDir -EnvVars @{ NodeName="node2"; ASPNETCORE_ENVIRONMENT=$EnvName }
New-AppSite -Name "yarp-lb" -Ip "*" -Port 5266 -PhysicalPath $lbDir       -EnvVars @{ ASPNETCORE_ENVIRONMENT=$EnvName }

if ($Https) {
    Write-Host ">> Tao self-signed cert cho localhost..."
    $cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "Cert:\LocalMachine\My"
    New-WebBinding -Name "yarp-lb" -Protocol https -Port $HttpsPort -HostHeader "" | Out-Null
    (Get-WebBinding -Name "yarp-lb" -Protocol https).AddSslCertificate($cert.Thumbprint, "My")
    Write-Host "   [ok] HTTPS -> https://localhost:$HttpsPort (self-signed, browser se canh bao)"
}

iisreset /noforce | Out-Null
Write-Host ""
Write-Host ">> Xong. Mo:  http://localhost:5266"
Write-Host "   Backend variant: $BackendVariant | Env: $EnvName"
if ($BackendVariant -like "*redis*") { Write-Host "   Nho bat Redis o 127.0.0.1:6380 (docker compose up -d hoac Redis local)." }
if ($BackendVariant -like "login*") { Write-Host "   Demo login: mo /login, dang nhap user bat ky / password 1234." }
