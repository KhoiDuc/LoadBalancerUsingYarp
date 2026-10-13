# pick-app.ps1 - Giao dien tuong tac chon ung dung cho iis-setup.bat
[CmdletBinding()]
param(
    [string]$OutputFile = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "lib-appdeploy.ps1")

if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    $OutputFile = Join-Path $PSScriptRoot ".selected_app.tmp"
}

$selectedPath = Select-AppDeployPathInteractive -PromptTitle "CAU HINH UNG DUNG NGOAI (EXTERNAL APPLICATION)" -AllowDemoOption

if ($null -eq $selectedPath) {
    # Nguoi dung huy hoac build loi
    "" | Out-File -FilePath $OutputFile -Encoding utf8
    exit 1
}

$selectedPath | Out-File -FilePath $OutputFile -Encoding utf8
exit 0
