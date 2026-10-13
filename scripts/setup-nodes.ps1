#Requires -RunAsAdministrator
# ============================================================================
# Deploy CHI node1 + node2 (KHONG co LB) len IIS.
# Dung cho SCENARIO 3: node1/node2 nam o server 199.30.30.91, con LB o may khac.
#   - Chay script nay TREN SERVER 199.30.30.91.
#   - Mac dinh bind '*' + -OpenFirewall de LB o may ngoai goi toi duoc.
# (Neu muon tat ca chung 1 may = scenario 1, cu dung menu 3 setup-iis-server.ps1.)
#
# -AppPath: tro ca 2 node vao 1 folder publish NGOAI lab (vd D:\Publish\ClientPaymentPortal).
#   Khi co AppPath thi bo qua -BackendVariant (khong lien quan Redis demo).
# ============================================================================
[CmdletBinding()]
param(
    [ValidateSet("redis","inmemory","login-redis","login-inmemory","signalr-inmemory","signalr-redis","upload-local","upload-shared")]
    [string]$BackendVariant = "redis",
    [string]$AppPath = "",           # Folder publish app that (dung chung hoac clone cho node1+node2)
    [string]$App = "",               # Alias app tu AppDeploy (portal, gateway, portal-webclient...)
    [switch]$CloneFolders,           # Clone ra 2 folder node1 va node2 rieng de tranh xung dot lock file/log
    [string]$EnvName = "Production",
    [string]$Bind = "*",          # "*" = cho may khac goi toi (scenario 3). "127.0.0.1" = chi noi bo.
    [int]$Port1 = 5001,
    [int]$Port2 = 5002,
    [switch]$OpenFirewall
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

. (Join-Path $PSScriptRoot "lib-appdeploy.ps1")
if (-not [string]::IsNullOrWhiteSpace($App)) {
    $matchedTarget = Resolve-AppDeployTarget -NameOrKey $App
    if ($matchedTarget) {
        $AppPath = $matchedTarget.PublishPath
        if (-not $matchedTarget.IsPublished) {
            Write-Host ">> [AppDeploy] App '$($matchedTarget.Display)' chua duoc publish. Bat dau publish..." -ForegroundColor Yellow
            $pubOk = Invoke-AppDeployPublishTarget -Target $matchedTarget
            if (-not $pubOk) { throw "Publish that bai!" }
        }
    }
} elseif (-not [string]::IsNullOrWhiteSpace($AppPath)) {
    $matchedTarget = Resolve-AppDeployTarget -NameOrKey $AppPath
    if ($matchedTarget) {
        $AppPath = $matchedTarget.PublishPath
        if (-not $matchedTarget.IsPublished) {
            Write-Host ">> [AppDeploy] App '$($matchedTarget.Display)' chua duoc publish. Bat dau publish..." -ForegroundColor Yellow
            $pubOk = Invoke-AppDeployPublishTarget -Target $matchedTarget
            if (-not $pubOk) { throw "Publish that bai!" }
        }
    }
}

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$pub  = Join-Path $root "publish"
$useExternal = -not [string]::IsNullOrWhiteSpace($AppPath)

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

    if ($CloneFolders) {
        $trimmedPath = $backendDir.TrimEnd('\', '/')
        $dir1 = "$trimmedPath-node1"
        $dir2 = "$trimmedPath-node2"
        Write-Host ">> [CloneFolders] Sao chep thu muc sang 2 node rieng biet de tranh xung dot lock file/log:"
        Write-Host "   Node 1: $dir1"
        Write-Host "   Node 2: $dir2"
        if (-not (Test-Path $dir1)) { New-Item -ItemType Directory -Path $dir1 | Out-Null }
        if (-not (Test-Path $dir2)) { New-Item -ItemType Directory -Path $dir2 | Out-Null }
        robocopy $backendDir $dir1 /E /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
        robocopy $backendDir $dir2 /E /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
    } else {
        $dir1 = $backendDir
        $dir2 = $backendDir
    }
} else {
    $backendDir = switch ($BackendVariant) {
        "redis"            { Join-Path $pub "backend-redis" }
        "inmemory"         { Join-Path $pub "backend-inmemory" }
        "login-redis"      { Join-Path $pub "backend-login-redis" }
        "login-inmemory"   { Join-Path $pub "backend-login-inmemory" }
        "signalr-inmemory" { Join-Path $pub "backend-signalr-inmemory" }
        "signalr-redis"    { Join-Path $pub "backend-signalr-redis" }
        "upload-local"     { Join-Path $pub "backend-upload-local" }
        "upload-shared"    { Join-Path $pub "backend-upload-shared" }
    }
    if (-not (Test-Path $backendDir)) {
        throw "Chua publish (thieu '$backendDir'). Chay menu 1 (publish.ps1) truoc."
    }
    $dir1 = $backendDir
    $dir2 = $backendDir
}

# Cap quyen IIS_IUSRS tren thu muc can doc (lab publish hoac AppPath ngoai).
if ($useExternal) {
    Write-Host ">> Cap quyen doc cho IIS_IUSRS tren: $dir1"
    icacls $dir1 /grant "*S-1-5-32-568:(OI)(CI)RX" /T /C | Out-Null
    if ($dir2 -ne $dir1) {
        Write-Host ">> Cap quyen doc cho IIS_IUSRS tren: $dir2"
        icacls $dir2 /grant "*S-1-5-32-568:(OI)(CI)RX" /T /C | Out-Null
    }
} else {
    Write-Host ">> Cap quyen doc cho IIS_IUSRS tren: $pub"
    icacls $pub /grant "*S-1-5-32-568:(OI)(CI)RX" /T /C | Out-Null
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
    icacls $PhysicalPath /grant "IIS AppPool\$Name`:(OI)(CI)RX" /T /C | Out-Null
    Write-Host "   [ok] $Name -> $Ip`:$Port  ($PhysicalPath)"
}

if ($useExternal) {
    Write-Host ">> Mode=external (Node1: $dir1 | Node2: $dir2) | Bind: $Bind"
} else {
    Write-Host ">> Mode=demo BackendVariant=$BackendVariant ($backendDir) | Bind: $Bind"
}
New-AppSite -Name "node1" -Ip $Bind -Port $Port1 -PhysicalPath $dir1 -EnvVars @{ NodeName="node1"; ASPNETCORE_ENVIRONMENT=$EnvName }
New-AppSite -Name "node2" -Ip $Bind -Port $Port2 -PhysicalPath $dir2 -EnvVars @{ NodeName="node2"; ASPNETCORE_ENVIRONMENT=$EnvName }

if ($OpenFirewall) {
    New-NetFirewallRule -DisplayName "yarp node1 $Port1" -Direction Inbound -Protocol TCP -LocalPort $Port1 -Action Allow -ErrorAction SilentlyContinue | Out-Null
    New-NetFirewallRule -DisplayName "yarp node2 $Port2" -Direction Inbound -Protocol TCP -LocalPort $Port2 -Action Allow -ErrorAction SilentlyContinue | Out-Null
    Write-Host "   [ok] Da mo firewall cho node ($Port1, $Port2)."
}

iisreset /noforce | Out-Null
$ipHint = if ($Bind -eq "*") { "<IP-cua-may-nay>" } else { $Bind }
Write-Host ""
Write-Host ">> Xong. Node da san sang cho LB (o may khac) goi toi:"
Write-Host "   node1 -> http://$ipHint`:$Port1/  ($dir1)"
Write-Host "   node2 -> http://$ipHint`:$Port2/  ($dir2)"
Write-Host ""
Write-Host "   >> Buoc tiep theo (TREN MAY DAT LB), chay:"
if ($useExternal) {
    Write-Host "      setup-lb.ps1 -Node1 http://$ipHint`:$Port1/ -Node2 http://$ipHint`:$Port2/ -Mode Affinity"
} else {
    Write-Host "      setup-lb.ps1 -Node1 http://$ipHint`:$Port1/ -Node2 http://$ipHint`:$Port2/"
}
if (-not $useExternal -and $BackendVariant -like "*redis*") {
    Write-Host "   Redis: theo appsettings.$EnvName.json (vd 199.30.30.91:6380). Dam bao node toi duoc."
}
if ($Bind -eq "*") { Write-Host "   Luu y: neu server sau NAT/cloud, mo them Security Group/inbound cho port $Port1, $Port2." }
