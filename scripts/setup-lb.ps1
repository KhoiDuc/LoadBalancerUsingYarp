#Requires -RunAsAdministrator
# ============================================================================
# Deploy CHI LB (yarp-lb) len IIS, tro toi node o DIA CHI BAT KY (co the may khac).
# LB agnostic: khong phu thuoc backend la demo lab hay app publish ngoai.
# Dung cho SCENARIO 3: node1/node2 o server 199.30.30.91, LB o may nay (local).
#   Vd (chay TREN MAY LOCAL):
#     setup-lb.ps1 -Node1 http://199.30.30.91:5001/ -Node2 http://199.30.30.91:5002/
#   App publish ngoai (khong biet co share state): them -Mode Affinity
#
# Dia chi node duoc truyen qua ENV VAR, GHI DE len appsettings.json (KHONG sua file JSONC):
#   .NET config dung '__' lam dau phan cap va env var LUON ghi de gia tri trong appsettings.json.
#   -> Khong can build/publish lai, chi doi tham so la xong.
# (Neu tat ca chung 1 may = scenario 1, cu dung menu 3 setup-iis-server.ps1.)
# ============================================================================
[CmdletBinding()]
param(
    [string]$Node1 = "http://127.0.0.1:5001/",
    [string]$Node2 = "http://127.0.0.1:5002/",
    [ValidateSet("Stateless","Affinity")]
    [string]$Mode = "Stateless",    # App ngoai: khuyen nghi Affinity
    [string]$EnvName = "Production",
    [int]$HttpPort = 5266,
    [switch]$Https,                 # self-signed localhost (test nhanh tren may local)
    [string]$CertThumbprint = "",   # cert THAT da import vao Cert:\LocalMachine\My
    [string]$HostName = "",
    [int]$HttpsPort = 5443,
    [switch]$OpenFirewall
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$pub  = Join-Path $root "publish"
$lbDir = Join-Path $pub "lb"
if (-not (Test-Path $lbDir)) { throw "Chua publish LB (thieu '$lbDir'). Chay menu 1 (publish.ps1) truoc." }

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

# Key ghi de destination cua YARP (khop ten cluster 'demo' + node1/node2 trong appsettings.json).
$env1 = "ReverseProxy__Clusters__demo__Destinations__node1__Address"
$env2 = "ReverseProxy__Clusters__demo__Destinations__node2__Address"
$envVars = @{
    ASPNETCORE_ENVIRONMENT = $EnvName
    LoadBalancer__Mode     = $Mode
}
$envVars[$env1] = $Node1
$envVars[$env2] = $Node2

Write-Host ">> Tao site yarp-lb, tro toi:"
Write-Host "     node1 = $Node1"
Write-Host "     node2 = $Node2"
Write-Host "     Mode  = $Mode"
New-AppSite -Name "yarp-lb" -Ip "*" -Port $HttpPort -PhysicalPath $lbDir -EnvVars $envVars

# Binding 127.0.0.1 cho LB de 'localhost' luon match dung site (tranh http.sys 400 Invalid Hostname).
New-WebBinding -Name "yarp-lb" -Protocol http -IPAddress "127.0.0.1" -Port $HttpPort -ErrorAction SilentlyContinue | Out-Null

if ($Https) {
    Write-Host ">> Tao self-signed cert cho localhost..."
    $cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "Cert:\LocalMachine\My"
    New-WebBinding -Name "yarp-lb" -Protocol https -Port $HttpsPort -HostHeader "" | Out-Null
    (Get-WebBinding -Name "yarp-lb" -Protocol https).AddSslCertificate($cert.Thumbprint, "My")
    Write-Host "   [ok] HTTPS -> https://localhost:$HttpsPort (self-signed, browser se canh bao)"
} elseif ($CertThumbprint) {
    Write-Host ">> Gan HTTPS (cert that, SNI)..."
    New-WebBinding -Name "yarp-lb" -Protocol https -Port $HttpsPort -HostHeader $HostName -SslFlags 1 | Out-Null
    (Get-WebBinding -Name "yarp-lb" -Protocol https).AddSslCertificate($CertThumbprint, "My")
    Write-Host "   [ok] https://$HostName`:$HttpsPort"
}

if ($OpenFirewall) {
    New-NetFirewallRule -DisplayName "yarp-lb $HttpPort" -Direction Inbound -Protocol TCP -LocalPort $HttpPort -Action Allow -ErrorAction SilentlyContinue | Out-Null
    if ($Https -or $CertThumbprint) { New-NetFirewallRule -DisplayName "yarp-lb https $HttpsPort" -Direction Inbound -Protocol TCP -LocalPort $HttpsPort -Action Allow -ErrorAction SilentlyContinue | Out-Null }
    Write-Host "   [ok] Da mo firewall cho LB ($HttpPort)."
}

iisreset /noforce | Out-Null
Write-Host ""
Write-Host ">> Xong. LB: http://localhost:$HttpPort  (hoac http://<IP-may-nay>:$HttpPort tu may khac)"
Write-Host "   Mode: $Mode | Xem header 'X-Served-By' de biet request duoc day vao node nao."
if ($Mode -eq "Stateless") {
    Write-Host "   App publish ngoai (chua share state): nen chay lai voi -Mode Affinity."
}
Write-Host "   Luu y: LB (o may nay) phai 'voi toi' duoc $Node1 / $Node2 (mo firewall/Security Group phia node)."
