# Chay ung dung ngoai (publish folder) voi 2 node (5001, 5002) + YARP Load Balancer (5266).
# KHONG CAN IIS - Chay truc tiep bang dotnet CLI. Chay lien tuc vo han cho den khi bam dung.
[CmdletBinding()]
param(
    [string]$AppPath = "",
    [string]$App = "",
    [switch]$CloneFolders,
    [ValidateSet("Stateless", "Affinity", "")]
    [string]$Mode = "",
    [int]$RefreshSeconds = 2
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $RepoRoot

. (Join-Path $PSScriptRoot "lib-appdeploy.ps1")

# 1. Xu ly tham so -App hoac hoi AppPath neu chua truyen
if (-not [string]::IsNullOrWhiteSpace($App)) {
    Write-Host "==============================================================================" -ForegroundColor Cyan
    Write-Host "         CHAY LOAD BALANCER CHO UNG DUNG (DOTNET CLI - NO IIS)                " -ForegroundColor Cyan
    Write-Host "==============================================================================" -ForegroundColor Cyan

    $matchedTarget = Resolve-AppDeployTarget -NameOrKey $App
    if ($matchedTarget) {
        $AppPath = $matchedTarget.PublishPath
        if (-not $matchedTarget.IsPublished) {
            Write-Host ">> App '$($matchedTarget.Display)' chua duoc publish. Bat dau publish..." -ForegroundColor Yellow
            $pubOk = Invoke-TargetPublish -Target $matchedTarget
            if (-not $pubOk) { exit 1 }
        }
    }
} elseif (-not [string]::IsNullOrWhiteSpace($AppPath)) {
    Write-Host "==============================================================================" -ForegroundColor Cyan
    Write-Host "         CHAY LOAD BALANCER CHO UNG DUNG (DOTNET CLI - NO IIS)                " -ForegroundColor Cyan
    Write-Host "==============================================================================" -ForegroundColor Cyan

    $matchedTarget = Resolve-AppDeployTarget -NameOrKey $AppPath
    if ($matchedTarget) {
        $AppPath = $matchedTarget.PublishPath
        if (-not $matchedTarget.IsPublished) {
            Write-Host ">> App '$($matchedTarget.Display)' chua duoc publish. Bat dau publish..." -ForegroundColor Yellow
            $pubOk = Invoke-TargetPublish -Target $matchedTarget
            if (-not $pubOk) { exit 1 }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($AppPath)) {
    $AppPath = Select-AppDeployPathInteractive -PromptTitle "CHAY LOAD BALANCER CHO UNG DUNG (DOTNET CLI - NO IIS)"
}

if ([string]::IsNullOrWhiteSpace($AppPath) -or -not (Test-Path -LiteralPath $AppPath)) {
    Write-Host ""
    Write-Host ">> ERROR: Duong dan AppPath '$AppPath' khong ton tai hoac chua duoc chon!" -ForegroundColor Red
    Read-Host "Nhan Enter de thoat..."
    exit 1
}

$backendDir = (Resolve-Path -LiteralPath $AppPath).Path

# 2. Xac dinh file DLL hoac EXE chay chinh (Entry point)
$rtcFiles = @(Get-ChildItem -LiteralPath $backendDir -Filter "*.runtimeconfig.json" -File)
$entryFile = $null
$isExe = $false

if ($rtcFiles.Count -gt 0) {
    $baseName = $rtcFiles[0].BaseName
    if (Test-Path -LiteralPath (Join-Path $backendDir "$baseName.dll")) {
        $entryFile = "$baseName.dll"
    } elseif (Test-Path -LiteralPath (Join-Path $backendDir "$baseName.exe")) {
        $entryFile = "$baseName.exe"
        $isExe = $true
    }
}

if (-not $entryFile) {
    $folderName = Split-Path $backendDir -Leaf
    if (Test-Path (Join-Path $backendDir "$folderName.dll")) {
        $entryFile = "$folderName.dll"
    } elseif (Test-Path (Join-Path $backendDir "$folderName.exe")) {
        $entryFile = "$folderName.exe"
        $isExe = $true
    } else {
        $firstDll = @(Get-ChildItem -LiteralPath $backendDir -Filter "*.dll" -File | Where-Object { $_.Name -notlike "Microsoft.*" -and $_.Name -notlike "System.*" })
        if ($firstDll.Count -gt 0) {
            $entryFile = $firstDll[0].Name
        }
    }
}

if (-not $entryFile) {
    Write-Host ""
    Write-Host ">> ERROR: Khong tim thay file chay (*.dll hoac *.exe) trong thu muc '$backendDir'." -ForegroundColor Red
    Read-Host "Nhan Enter de thoat..."
    exit 1
}

Write-Host ">> Phat hien ung dung: $entryFile" -ForegroundColor Green

# 3. Hoi tuy chon Clone neu chua bat switch
if (-not $PSBoundParameters.ContainsKey("CloneFolders")) {
    Write-Host ""
    Write-Host "Ban co muon CLONE ra 2 folder rieng (_node1 va _node2) khong?" -ForegroundColor Yellow
    Write-Host "  [1] CO - Clone ra 2 folder (KHUYEN NGHI: Tranh loi xung dot ghi log, cache)"
    Write-Host "  [2] KHONG - Dung chung 1 folder goc"
    $c = Read-Host "Chon [1 hoac 2, Enter = 1]"
    if ($c -ne "2") {
        $CloneFolders = $true
    }
}

# 4. Hoi LB Mode neu chua set
if ([string]::IsNullOrWhiteSpace($Mode)) {
    Write-Host ""
    Write-Host "CHON CHE DO LOAD BALANCER:" -ForegroundColor Yellow
    Write-Host "  [1] Stateless (Round-Robin): Chia deu, tu dong NHAY DOI NODE sau moi chu ky refresh"
    Write-Host "  [2] Affinity  (Sticky Session): Moi user 'dinh' chat 1 node (Refresh se GIU CO DINH node)"
    $m = Read-Host "Chon che do [1 hoac 2, Enter = 1 de test nhay node]"
    if ($m -eq "2" -or $m -like "affinity*") {
        $Mode = "Affinity"
    } else {
        $Mode = "Stateless"
    }
}

# 5. Hoi so giay tu dong refresh (mac dinh = 2s)
if (-not $PSBoundParameters.ContainsKey("RefreshSeconds")) {
    Write-Host ""
    Write-Host "Nhap so giay tu dong refresh trang HTML (0 = tat auto-refresh, Enter = 2):" -ForegroundColor Yellow
    $rInput = Read-Host ">> RefreshSeconds [Enter = 2]"
    if (-not [string]::IsNullOrWhiteSpace($rInput)) {
        $parsed = 0
        if ([int]::TryParse($rInput, [ref]$parsed)) {
            $RefreshSeconds = $parsed
        }
    }
}

# 6. Xu ly folder cho 2 node
if ($CloneFolders) {
    $trimmed = $backendDir.TrimEnd('\', '/')
    $dir1 = "$trimmed-node1"
    $dir2 = "$trimmed-node2"
    Write-Host ""
    Write-Host ">> [CloneFolders] Sao chep thu muc sang 2 node rieng..." -ForegroundColor Cyan
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

# 7. Build LoadBalancer truoc khi khoi dong
Write-Host ""
Write-Host ">> Build YARP Load Balancer..." -ForegroundColor Cyan
dotnet build src/LoadBalancer -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host ">> BUILD LOAD BALANCER THAT BAI!" -ForegroundColor Red
    Read-Host "Nhan Enter de thoat..."
    exit $LASTEXITCODE
}

# 8. Khoi dong 2 Node backend
Write-Host ">> Khoi dong node1 (port 5001) tai: $dir1" -ForegroundColor Green
$argList1 = @('--urls','http://127.0.0.1:5001','--NodeName','node1','--Ui:RefreshSeconds',"$RefreshSeconds")
if ($isExe) {
    $n1 = Start-Process (Join-Path $dir1 $entryFile) -ArgumentList $argList1 -WorkingDirectory $dir1 -PassThru
} else {
    $n1 = Start-Process dotnet -ArgumentList (@($entryFile) + $argList1) -WorkingDirectory $dir1 -PassThru
}

Write-Host ">> Khoi dong node2 (port 5002) tai: $dir2" -ForegroundColor Green
$argList2 = @('--urls','http://127.0.0.1:5002','--NodeName','node2','--Ui:RefreshSeconds',"$RefreshSeconds")
if ($isExe) {
    $n2 = Start-Process (Join-Path $dir2 $entryFile) -ArgumentList $argList2 -WorkingDirectory $dir2 -PassThru
} else {
    $n2 = Start-Process dotnet -ArgumentList (@($entryFile) + $argList2) -WorkingDirectory $dir2 -PassThru
}

function Stop-Backends {
    Write-Host ""
    Write-Host ">> Dang dung cac node backend (node1, node2)..." -ForegroundColor Yellow
    foreach ($p in @($n1, $n2)) {
        if ($null -ne $p -and -not $p.HasExited) {
            & taskkill.exe /PID $p.Id /T /F 2>$null | Out-Null
        }
    }
    Write-Host ">> Da dung hoan tat." -ForegroundColor Green
}

. (Join-Path $PSScriptRoot "lib-ngrok.ps1")
$ngrokInfo = Ask-NgrokOption -Port 5266

try {
    Write-Host ">> Cho 3 giay de cac node khoi dong xong..."
    Start-Sleep -Seconds 3

    Write-Host ""
    Write-Host "==============================================================================" -ForegroundColor Green
    Write-Host "                    HE THONG DANG CHAY (VO HAN)                               " -ForegroundColor Green
    Write-Host "==============================================================================" -ForegroundColor Green
    Write-Host "  >> DIA CHI TRUY CAP LOAD BALANCER : http://localhost:5266" -ForegroundColor Yellow
    if ($ngrokInfo -and $ngrokInfo.Url) {
        Write-Host "  >> NGROK PUBLIC URL               : $($ngrokInfo.Url)" -ForegroundColor Green
        Write-Host "  >> DASHBOARD PUBLIC               : $($ngrokInfo.Url)/dashboard" -ForegroundColor Green
    }
    Write-Host "  >> Che do Load Balancer           : $Mode" -ForegroundColor Yellow
    Write-Host "  >> So giay auto-refresh trang     : $RefreshSeconds giay" -ForegroundColor Cyan
    if ($Mode -eq "Stateless") {
        Write-Host "  >> Hanh vi mong doi               : Moi $RefreshSeconds giay se NHAY DOI NODE (node1 <-> node2)" -ForegroundColor Cyan
    } else {
        Write-Host "  >> Hanh vi mong doi               : Moi $RefreshSeconds giay se GIU CO DINH 1 NODE (Sticky Session)" -ForegroundColor Cyan
    }
    Write-Host ""
    Write-Host "  * CHU Y: KHONG mo http://localhost:5001 hay 5002 vi do la di thang vao node," -ForegroundColor Magenta
    Write-Host "           khong qua Load Balancer nen se luon co dinh 1 node!" -ForegroundColor Magenta
    Write-Host "  * Nhan [Ctrl + C] tren cua so nay bat ky luc nao de dung he thong." -ForegroundColor Yellow
    Write-Host "==============================================================================" -ForegroundColor Green
    Write-Host ""

    # Chay Load Balancer o foreground (giu Active Health Check bat cho cac app lab de tu dong failover)
    $activeHealth = "true"
    if ($matchedTarget -and ($matchedTarget.Key -like "*upload*")) {
        $activeHealth = "false"
    }
    dotnet run --project src/LoadBalancer --no-launch-profile -- --urls http://localhost:5266 --LoadBalancer:Mode $Mode --ReverseProxy:Clusters:demo:HealthCheck:Active:Enabled $activeHealth
} catch {
    Write-Host ">> Co loi xay ra: $_" -ForegroundColor Red
} finally {
    Stop-Backends
    Stop-NgrokTunnel $ngrokInfo
}
