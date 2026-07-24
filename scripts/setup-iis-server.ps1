#Requires -RunAsAdministrator
# Setup IIS SERVER (1 server, khac port): yarp-lb:5266 (public) + node1:5001 & node2:5002 (chi 127.0.0.1).
# Env = Production -> backend doc appsettings.Production.json (Redis 199.30.30.91:6380 neu la ban redis).
#
# -AppPath: tro ca 2 node vao 1 folder publish NGOAI lab. Khi co AppPath:
#   - Bo qua -BackendVariant (khong lien quan Redis demo).
#   - LB mac dinh Affinity (tru khi truyen -Mode). Chi can publish\lb + AppPath.
[CmdletBinding()]
param(
    [ValidateSet("redis","inmemory","login-redis","login-inmemory")]
    [string]$BackendVariant = "redis",
    [string]$AppPath = "",             # Folder publish app that (dung chung cho node1+node2)
    [string]$Mode = "",                # De trong = auto: Affinity neu AppPath, else Stateless (appsettings)
    [string]$EnvName = "Production",
    [string]$HostName = "",            # vd: lb.example.com. De trong = binding theo port thuan.
    [string]$CertThumbprint = "",      # cert THAT da import vao Cert:\LocalMachine\My
    [int]$HttpsPort = 443,
    [switch]$OpenFirewall
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$pub  = Join-Path $root "publish"
$lbDir = Join-Path $pub "lb"
$useExternal = -not [string]::IsNullOrWhiteSpace($AppPath)

if (-not (Test-Path $lbDir)) {
    throw "Chua publish LB (thieu '$lbDir'). Chay menu 1 (publish.ps1) truoc."
}

if ($useExternal) {
    if (-not (Test-Path -LiteralPath $AppPath)) {
        throw "AppPath khong ton tai: '$AppPath'."
    }
    $backendDir = (Resolve-Path -LiteralPath $AppPath).Path
    $hasWebConfig = Test-Path -LiteralPath (Join-Path $backendDir "web.config")
    $hasDll = @(Get-ChildItem -LiteralPath $backendDir -Filter "*.dll" -File -ErrorAction SilentlyContinue).Count -gt 0
    if (-not $hasWebConfig -and -not $hasDll) {
        throw "AppPath '$backendDir' khong giong publish ASP.NET Core (thieu web.config hoac *.dll)."
    }
} else {
    $backendDir = switch ($BackendVariant) {
        "redis"          { Join-Path $pub "backend-redis" }
        "inmemory"       { Join-Path $pub "backend-inmemory" }
        "login-redis"    { Join-Path $pub "backend-login-redis" }
        "login-inmemory" { Join-Path $pub "backend-login-inmemory" }
    }
    if (-not (Test-Path $backendDir)) {
        throw "Chua publish (thieu '$backendDir'). Chay menu 1 (publish.ps1) truoc."
    }
}

# Cap quyen doc cho IIS_IUSRS (fix HTTP 500.19 / 0x80070005). SID *S-1-5-32-568 = IIS_IUSRS.
Write-Host ">> Cap quyen doc cho IIS_IUSRS tren: $pub"
icacls $pub /grant "*S-1-5-32-568:(OI)(CI)RX" /T /C | Out-Null
if ($useExternal) {
    Write-Host ">> Cap quyen doc cho IIS_IUSRS tren: $backendDir"
    icacls $backendDir /grant "*S-1-5-32-568:(OI)(CI)RX" /T /C | Out-Null
}

# Mode: AppPath -> Affinity mac dinh; demo -> khong set (giu Stateless trong appsettings) neu Mode trong.
$effectiveMode = $Mode
if (-not [string]::IsNullOrWhiteSpace($effectiveMode)) {
    if ($effectiveMode -notin @("Stateless","Affinity")) {
        throw "Mode khong hop le: '$effectiveMode'. Chi nhan Stateless hoac Affinity."
    }
} elseif ($useExternal) {
    $effectiveMode = "Affinity"
} else {
    $effectiveMode = ""
}

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

    # Cap them quyen doc cho chinh app pool identity tren thu muc site.
    icacls $PhysicalPath /grant "IIS AppPool\$Name`:(OI)(CI)RX" /T /C | Out-Null

    Write-Host "   [ok] $Name -> $Ip`:$Port  ($PhysicalPath)"
}

if ($useExternal) {
    Write-Host ">> Mode=external AppPath=$backendDir"
} else {
    Write-Host ">> Mode=demo BackendVariant=$BackendVariant ($backendDir)"
}
Write-Host ">> Tao cac site (backend chi lang nghe 127.0.0.1)..."
New-AppSite -Name "node1"   -Ip "127.0.0.1" -Port 5001 -PhysicalPath $backendDir -EnvVars @{ NodeName="node1"; ASPNETCORE_ENVIRONMENT=$EnvName }
New-AppSite -Name "node2"   -Ip "127.0.0.1" -Port 5002 -PhysicalPath $backendDir -EnvVars @{ NodeName="node2"; ASPNETCORE_ENVIRONMENT=$EnvName }

$lbEnv = @{ ASPNETCORE_ENVIRONMENT = $EnvName }
if (-not [string]::IsNullOrWhiteSpace($effectiveMode)) {
    $lbEnv["LoadBalancer__Mode"] = $effectiveMode
    Write-Host ">> LB LoadBalancer__Mode=$effectiveMode"
}
New-AppSite -Name "yarp-lb" -Ip "*" -Port 5266 -PhysicalPath $lbDir -EnvVars $lbEnv

# Them binding 127.0.0.1 cho LB de 'localhost' ngay tren server luon match dung site,
# tranh loi http.sys 'HTTP 400 - Invalid Hostname' khi binding chi la wildcard '*'.
New-WebBinding -Name "yarp-lb" -Protocol http -IPAddress "127.0.0.1" -Port 5266 -ErrorAction SilentlyContinue | Out-Null

if ($CertThumbprint) {
    Write-Host ">> Gan HTTPS cho yarp-lb (cert that, SNI)..."
    New-WebBinding -Name "yarp-lb" -Protocol https -Port $HttpsPort -HostHeader $HostName -SslFlags 1 | Out-Null
    (Get-WebBinding -Name "yarp-lb" -Protocol https).AddSslCertificate($CertThumbprint, "My")
    Write-Host "   [ok] https://$HostName`:$HttpsPort"
} else {
    Write-Host ">> Bo qua HTTPS (khong truyen -CertThumbprint)."
}

if ($OpenFirewall) {
    New-NetFirewallRule -DisplayName "yarp-lb 5266" -Direction Inbound -Protocol TCP -LocalPort 5266 -Action Allow -ErrorAction SilentlyContinue | Out-Null
    if ($CertThumbprint) { New-NetFirewallRule -DisplayName "yarp-lb https" -Direction Inbound -Protocol TCP -LocalPort $HttpsPort -Action Allow -ErrorAction SilentlyContinue | Out-Null }
    Write-Host "   [ok] Da mo firewall cho LB (node1/node2 khong mo ra ngoai)."
}

iisreset /noforce | Out-Null
Write-Host ""
Write-Host ">> Xong. LB: http://<server>:5266  (tren chinh server: http://localhost:5266)"
if ($useExternal) {
    Write-Host "   AppPath: $backendDir | Env: $EnvName | LB Mode: $(if ($effectiveMode) { $effectiveMode } else { 'Stateless (appsettings)' })"
} else {
    Write-Host "   Backend variant: $BackendVariant | Env: $EnvName"
    if ($BackendVariant -like "*redis*") { Write-Host "   Redis: 199.30.30.91:6380 (theo appsettings.Production.json). Dam bao server toi duoc." }
    if ($BackendVariant -eq "login-inmemory") { Write-Host "   Luu y: ban nay luu DataProtection key o thu muc TEMP cua tung app pool (co the ghi duoc)." }
}
