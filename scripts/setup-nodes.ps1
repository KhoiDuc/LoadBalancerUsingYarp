#Requires -RunAsAdministrator
# ============================================================================
# Deploy CHI node1 + node2 (KHONG co LB) len IIS.
# Dung cho SCENARIO 3: node1/node2 nam o server 199.30.30.91, con LB o may khac.
#   - Chay script nay TREN SERVER 199.30.30.91.
#   - Mac dinh bind '*' + -OpenFirewall de LB o may ngoai goi toi duoc.
# (Neu muon tat ca chung 1 may = scenario 1, cu dung menu 3 setup-iis-server.ps1.)
# ============================================================================
[CmdletBinding()]
param(
    [ValidateSet("redis","inmemory","login-redis","login-inmemory")]
    [string]$BackendVariant = "redis",
    [string]$EnvName = "Production",
    [string]$Bind = "*",          # "*" = cho may khac goi toi (scenario 3). "127.0.0.1" = chi noi bo.
    [int]$Port1 = 5001,
    [int]$Port2 = 5002,
    [switch]$OpenFirewall
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$pub  = Join-Path $root "publish"
$backendDir = switch ($BackendVariant) {
    "redis"          { Join-Path $pub "backend-redis" }
    "inmemory"       { Join-Path $pub "backend-inmemory" }
    "login-redis"    { Join-Path $pub "backend-login-redis" }
    "login-inmemory" { Join-Path $pub "backend-login-inmemory" }
}
if (-not (Test-Path $backendDir)) {
    throw "Chua publish (thieu '$backendDir'). Chay menu 1 (publish.ps1) truoc."
}

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
    icacls $PhysicalPath /grant "IIS AppPool\$Name`:(OI)(CI)RX" /T /C | Out-Null
    Write-Host "   [ok] $Name -> $Ip`:$Port  ($PhysicalPath)"
}

Write-Host ">> Backend variant: $BackendVariant  ($backendDir) | Bind: $Bind"
New-AppSite -Name "node1" -Ip $Bind -Port $Port1 -PhysicalPath $backendDir -EnvVars @{ NodeName="node1"; ASPNETCORE_ENVIRONMENT=$EnvName }
New-AppSite -Name "node2" -Ip $Bind -Port $Port2 -PhysicalPath $backendDir -EnvVars @{ NodeName="node2"; ASPNETCORE_ENVIRONMENT=$EnvName }

if ($OpenFirewall) {
    New-NetFirewallRule -DisplayName "yarp node1 $Port1" -Direction Inbound -Protocol TCP -LocalPort $Port1 -Action Allow -ErrorAction SilentlyContinue | Out-Null
    New-NetFirewallRule -DisplayName "yarp node2 $Port2" -Direction Inbound -Protocol TCP -LocalPort $Port2 -Action Allow -ErrorAction SilentlyContinue | Out-Null
    Write-Host "   [ok] Da mo firewall cho node ($Port1, $Port2)."
}

iisreset /noforce | Out-Null
$ipHint = if ($Bind -eq "*") { "<IP-cua-may-nay>" } else { $Bind }
Write-Host ""
Write-Host ">> Xong. Node da san sang cho LB (o may khac) goi toi:"
Write-Host "   node1 -> http://$ipHint`:$Port1/"
Write-Host "   node2 -> http://$ipHint`:$Port2/"
Write-Host ""
Write-Host "   >> Buoc tiep theo (TREN MAY DAT LB), chay:"
Write-Host "      setup-lb.ps1 -Node1 http://$ipHint`:$Port1/ -Node2 http://$ipHint`:$Port2/"
if ($BackendVariant -like "*redis*") { Write-Host "   Redis: theo appsettings.$EnvName.json (vd 199.30.30.91:6380). Dam bao node toi duoc." }
if ($Bind -eq "*") { Write-Host "   Luu y: neu server sau NAT/cloud, mo them Security Group/inbound cho port $Port1, $Port2." }
