#Requires -RunAsAdministrator
# Setup LB bang IIS ARR (thay cho YARP) de SO SANH.
# Yeu cau da cai: Application Request Routing (ARR) + URL Rewrite.
# Tao 1 site 'arr-lb' (mac dinh :8080) route round-robin toi node1(5001)/node2(5002).
[CmdletBinding()]
param(
    [int]$Port = 8080,
    [string]$FarmName = "backend-farm"
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration
$appcmd = Join-Path $env:windir "system32\inetsrv\appcmd.exe"

# Kiem tra ARR da cai chua (co section proxy khong).
$check = & $appcmd list config -section:system.webServer/proxy 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Khong thay section 'proxy' -> ARR chua duoc cai. Cai ARR + URL Rewrite roi chay lai. Xem docs/iis-arr.md."
}

Write-Host ">> 1) Bat proxy o cap server (ARR)"
& $appcmd set config -section:system.webServer/proxy /enabled:"True" /commit:apphost | Out-Null

Write-Host ">> 2) Tao web farm '$FarmName' + 2 server (localhost:5001, 127.0.0.1:5002)"
# Xoa farm cu neu co (bo qua loi neu chua ton tai).
& $appcmd set config -section:webFarms "/-[name='$FarmName']" /commit:apphost 2>$null | Out-Null
& $appcmd set config -section:webFarms "/+[name='$FarmName']" /commit:apphost | Out-Null
# Server 1 (dung 'localhost') va Server 2 (dung '127.0.0.1') de tach dia chi tren cung 1 may.
& $appcmd set config -section:webFarms "/+[name='$FarmName'].[address='localhost']" /commit:apphost | Out-Null
& $appcmd set config -section:webFarms "/[name='$FarmName'].[address='localhost'].applicationRequestRouting.httpPort:5001" /commit:apphost | Out-Null
& $appcmd set config -section:webFarms "/+[name='$FarmName'].[address='127.0.0.1']" /commit:apphost | Out-Null
& $appcmd set config -section:webFarms "/[name='$FarmName'].[address='127.0.0.1'].applicationRequestRouting.httpPort:5002" /commit:apphost | Out-Null

Write-Host ">> 3) Health check /health cho farm"
& $appcmd set config -section:webFarms "/[name='$FarmName'].applicationRequestRouting.healthCheck.url:http://localhost/health" /commit:apphost | Out-Null
& $appcmd set config -section:webFarms "/[name='$FarmName'].applicationRequestRouting.loadBalancing.algorithm:WeightedRoundRobin" /commit:apphost | Out-Null

Write-Host ">> 4) Tao site 'arr-lb' :$Port + rewrite rule toi farm"
$siteRoot = Join-Path $env:SystemDrive "inetpub\arr-lb"
if (-not (Test-Path $siteRoot)) { New-Item -ItemType Directory -Path $siteRoot | Out-Null }
if (Get-Website -Name "arr-lb" -ErrorAction SilentlyContinue) { Remove-Website -Name "arr-lb" }
if (Test-Path "IIS:\AppPools\arr-lb") { Remove-WebAppPool -Name "arr-lb" }
New-WebAppPool -Name "arr-lb" | Out-Null
Set-ItemProperty "IIS:\AppPools\arr-lb" -Name managedRuntimeVersion -Value ""
New-Website -Name "arr-lb" -Port $Port -PhysicalPath $siteRoot -ApplicationPool "arr-lb" -Force | Out-Null

$psPath = "MACHINE/WEBROOT/APPHOST/arr-lb"
Clear-WebConfiguration -pspath $psPath -filter "system.webServer/rewrite/rules" -ErrorAction SilentlyContinue
Add-WebConfigurationProperty -pspath $psPath -filter "system.webServer/rewrite/rules" -name "." -value @{ name = "arr-to-farm"; stopProcessing = "True" }
Set-WebConfigurationProperty -pspath $psPath -filter "system.webServer/rewrite/rules/rule[@name='arr-to-farm']/match" -name "url" -value "(.*)"
Set-WebConfigurationProperty -pspath $psPath -filter "system.webServer/rewrite/rules/rule[@name='arr-to-farm']/action" -name "type" -value "Rewrite"
Set-WebConfigurationProperty -pspath $psPath -filter "system.webServer/rewrite/rules/rule[@name='arr-to-farm']/action" -name "url" -value ("http://" + $FarmName + "/{R:1}")

iisreset /noforce | Out-Null
Write-Host ""
Write-Host ">> Xong. ARR LB: http://localhost:$Port  (round-robin toi 5001/5002)"
Write-Host "   Luu y: node1/node2 phai dang chay (setup-iis-local.ps1 hoac setup-iis-server.ps1)."
Write-Host "   So sanh voi YARP LB o :5266."
