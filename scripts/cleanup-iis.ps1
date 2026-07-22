#Requires -RunAsAdministrator
# Xoa cac site/app pool/farm do lab tao ra, de setup lai tu dau.
$ErrorActionPreference = "SilentlyContinue"
Import-Module WebAdministration

foreach ($s in @("yarp-lb","node1","node2","arr-lb")) {
    if (Get-Website -Name $s) { Remove-Website -Name $s; Write-Host "removed site $s" }
    if (Test-Path "IIS:\AppPools\$s") { Remove-WebAppPool -Name $s; Write-Host "removed pool $s" }
}

$appcmd = Join-Path $env:windir "system32\inetsrv\appcmd.exe"
& $appcmd set config -section:webFarms "/-[name='backend-farm']" /commit:apphost 2>$null | Out-Null
Write-Host "removed farm backend-farm (neu co)"

iisreset /noforce | Out-Null
Write-Host ">> Cleanup xong."
