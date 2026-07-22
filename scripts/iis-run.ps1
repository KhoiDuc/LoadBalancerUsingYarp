# Wrapper chay 1 script IIS setup:
#  - Tu elevate len Administrator neu chua (UAC).
#  - LUON giu cua so mo + hien loi + ghi log (Start-Transcript).
# La SCRIPT DON GIAN (khong CmdletBinding) de moi tham so con lai roi vao $args
# va duoc chuyen tiep nguyen ven cho script dich (vd: -BackendVariant redis -Https).
#
# Vi du goi truc tiep:
#   powershell -ExecutionPolicy Bypass -File scripts\iis-run.ps1 setup-iis-local.ps1 -BackendVariant redis -Https
param(
    [string]$Script
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $here $Script

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin   = $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)

if (-not $isAdmin) {
    Write-Host ">> Can quyen Administrator, dang xin UAC..." -ForegroundColor Yellow
    $selfArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$PSCommandPath,$Script) + $args
    try {
        Start-Process powershell -Verb RunAs -ArgumentList $selfArgs
    } catch {
        Write-Host ">> Khong the elevate (co the ban da huy UAC): $($_.Exception.Message)" -ForegroundColor Red
        Read-Host ">> Nhan Enter de dong"
    }
    return
}

if (-not $Script) {
    Write-Host ">> Thieu ten script." -ForegroundColor Red
    Read-Host ">> Nhan Enter de dong"; return
}
if (-not (Test-Path $target)) {
    Write-Host ">> Khong tim thay script: $target" -ForegroundColor Red
    Read-Host ">> Nhan Enter de dong"; return
}

$log = Join-Path $here ("iis-setup-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
try { Start-Transcript -Path $log -Force | Out-Null } catch {}

try {
    Write-Host ">> Chay: $Script $($args -join ' ')  (Administrator)" -ForegroundColor Cyan
    Write-Host "------------------------------------------------------------"
    & $target @args
    Write-Host "------------------------------------------------------------"
    Write-Host ">> HOAN TAT: $Script" -ForegroundColor Green
} catch {
    Write-Host "------------------------------------------------------------"
    Write-Host ">> LOI khi chay $Script :" -ForegroundColor Red
    Write-Host ($_.Exception.Message) -ForegroundColor Red
    if ($_.ScriptStackTrace) { Write-Host $_.ScriptStackTrace -ForegroundColor DarkYellow }
} finally {
    try { Stop-Transcript | Out-Null } catch {}
}

Write-Host ""
Write-Host ">> Log da luu: $log" -ForegroundColor Yellow
Read-Host ">> Nhan Enter de dong cua so"
