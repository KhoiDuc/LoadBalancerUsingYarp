# lib-appdeploy.ps1 - Module cau noi giua yarp-lb-lab va AppDeploy toolkit
# Ho tro tu dong nhan dien app, lay thu muc publish va trigger publish tu dong.

function Get-AppDeployRoot {
    if ($env:APPDEPLOY_ROOT -and (Test-Path (Join-Path $env:APPDEPLOY_ROOT 'Deploy-App.ps1'))) {
        return (Resolve-Path $env:APPDEPLOY_ROOT).Path
    }
    $candidates = @(
        'D:\Projects\AppDeploy',
        (Join-Path $PSScriptRoot '..\..\AppDeploy'),
        (Join-Path $PSScriptRoot '..\AppDeploy')
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) {
            $deployScript = Join-Path $c 'Deploy-App.ps1'
            if (Test-Path -LiteralPath $deployScript) {
                return (Resolve-Path -LiteralPath $c).Path
            }
        }
    }
    return $null
}

function Get-AppDeployTargets {
    $deployRoot = Get-AppDeployRoot

    # Danh sach mac dinh cac Web Project tuong thich Load Balancer
    $targets = @(
        [pscustomobject]@{
            Id          = '1'
            Key         = 'portal-webclient'
            AppId       = 'portal'
            TargetId    = '1'
            Display     = 'Payment Portal - WebClient'
            PublishPath = 'D:\Publish\ClientPaymentPortal'
            AppPool     = '_pp_WebClient'
            DefaultPort = 8081
            Aliases     = @('portal', 'pp', 'webclient', '1')
        },
        [pscustomobject]@{
            Id          = '2'
            Key         = 'portal-hostedpayment'
            AppId       = 'portal'
            TargetId    = '3'
            Display     = 'Payment Portal - HostedPaymentPage'
            PublishPath = 'D:\Publish\HostedPaymentPage'
            AppPool     = '_pp_hostedPaymentLink'
            DefaultPort = 8083
            Aliases     = @('hostedpayment', 'hpp', '2')
        },
        [pscustomobject]@{
            Id          = '3'
            Key         = 'portal-webhook'
            AppId       = 'portal'
            TargetId    = '2'
            Display     = 'Payment Portal - WebHook'
            PublishPath = 'D:\Publish\WebHookPaymentPortal'
            AppPool     = '_pp_Webhook'
            DefaultPort = 8082
            Aliases     = @('webhook', '3')
        },
        [pscustomobject]@{
            Id          = '4'
            Key         = 'gateway-webclient'
            AppId       = 'gateway'
            TargetId    = '1'
            Display     = 'Secure Form Gateway - WebClient'
            PublishPath = 'D:\SecureFormGateway\Publish\SecureFormGateway.WebClient'
            AppPool     = '_secureFormGateway'
            DefaultPort = 8080
            Aliases     = @('gateway', 'sfg', '4')
        }
    )

    # Kiem tra thu muc publish thuc te co ton tai hay khong
    foreach ($t in $targets) {
        $hasFolder = Test-Path -LiteralPath $t.PublishPath
        $hasDll = $false
        if ($hasFolder) {
            $dlls = @(Get-ChildItem -LiteralPath $t.PublishPath -Filter "*.dll" -File -ErrorAction SilentlyContinue)
            $hasDll = ($dlls.Count -gt 0)
        }
        $t | Add-Member -NotePropertyName "IsPublished" -NotePropertyValue ($hasFolder -and $hasDll) -Force
    }

    return $targets
}

function Get-LabDemoTargets {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    $pubDir = Join-Path $repoRoot "publish"

    $demos = @(
        [pscustomobject]@{
            Id          = '5'
            Key         = 'redis'
            Display     = 'Demo Redis (Session + DataProtection tren Redis)'
            PublishPath = (Join-Path $pubDir 'backend-redis')
            ProjectPath = (Join-Path $repoRoot 'src\Backend.Redis')
            Aliases     = @('redis', 'd1')
        },
        [pscustomobject]@{
            Id          = '6'
            Key         = 'inmemory'
            Display     = 'Demo InMemory (Session rieng tung node)'
            PublishPath = (Join-Path $pubDir 'backend-inmemory')
            ProjectPath = (Join-Path $repoRoot 'src\Backend.InMemory')
            Aliases     = @('inmemory', 'd2')
        },
        [pscustomobject]@{
            Id          = '7'
            Key         = 'login-redis'
            Display     = 'Demo Login Redis (Shared auth cookie, giu login khi doi node)'
            PublishPath = (Join-Path $pubDir 'backend-login-redis')
            ProjectPath = (Join-Path $repoRoot 'src\Backend.SimpleLogin.Redis')
            Aliases     = @('login-redis', 'loginredis', 'd3')
        },
        [pscustomobject]@{
            Id          = '8'
            Key         = 'login-inmemory'
            Display     = 'Demo Login InMemory (Mat login khi doi node)'
            PublishPath = (Join-Path $pubDir 'backend-login-inmemory')
            ProjectPath = (Join-Path $repoRoot 'src\Backend.SimpleLogin.InMemory')
            Aliases     = @('login-inmemory', 'logininmemory', 'd4')
        },
        [pscustomobject]@{
            Id          = '9'
            Key         = 'signalr-redis'
            Display     = 'Demo SignalR Redis (Chat realtime co Redis Backplane)'
            PublishPath = (Join-Path $pubDir 'backend-signalr-redis')
            ProjectPath = (Join-Path $repoRoot 'src\Backend.SignalR.Redis')
            Aliases     = @('signalr-redis', 'signalrredis', 'd5')
        },
        [pscustomobject]@{
            Id          = '10'
            Key         = 'signalr-inmemory'
            Display     = 'Demo SignalR InMemory (Chat realtime khong backplane)'
            PublishPath = (Join-Path $pubDir 'backend-signalr-inmemory')
            ProjectPath = (Join-Path $repoRoot 'src\Backend.SignalR.InMemory')
            Aliases     = @('signalr-inmemory', 'signalrinmemory', 'd6')
        },
        [pscustomobject]@{
            Id          = '11'
            Key         = 'upload-shared'
            Display     = 'Demo File Upload Shared (Folder anh dung chung)'
            PublishPath = (Join-Path $pubDir 'backend-upload-shared')
            ProjectPath = (Join-Path $repoRoot 'src\Backend.FileUpload.Shared')
            Aliases     = @('upload-shared', 'uploadshared', 'd7')
        },
        [pscustomobject]@{
            Id          = '12'
            Key         = 'upload-local'
            Display     = 'Demo File Upload Local (Folder anh rieng - loi 404)'
            PublishPath = (Join-Path $pubDir 'backend-upload-local')
            ProjectPath = (Join-Path $repoRoot 'src\Backend.FileUpload.Local')
            Aliases     = @('upload-local', 'uploadlocal', 'd8')
        }
    )

    foreach ($d in $demos) {
        $hasFolder = Test-Path -LiteralPath $d.PublishPath
        $hasDll = $false
        if ($hasFolder) {
            $dlls = @(Get-ChildItem -LiteralPath $d.PublishPath -Filter "*.dll" -File -ErrorAction SilentlyContinue)
            $hasDll = ($dlls.Count -gt 0)
        }
        $d | Add-Member -NotePropertyName "IsPublished" -NotePropertyValue ($hasFolder -and $hasDll) -Force
        $d | Add-Member -NotePropertyName "IsLabDemo" -NotePropertyValue $true -Force
    }

    return $demos
}

function Resolve-AppDeployTarget {
    param([string]$NameOrKey)
    if ([string]::IsNullOrWhiteSpace($NameOrKey)) { return $null }
    $q = $NameOrKey.Trim().ToLowerInvariant()
    $allTargets = @(Get-AppDeployTargets) + @(Get-LabDemoTargets)
    foreach ($t in $allTargets) {
        if ($t.Id -eq $q -or $t.Key.ToLowerInvariant() -eq $q -or ($t.Aliases -and $t.Aliases -contains $q) -or $t.Display.ToLowerInvariant() -like "*$q*") {
            return $t
        }
    }
    return $null
}

function Invoke-LabDemoPublishTarget {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Target
    )
    Write-Host ""
    Write-Host "==============================================================================" -ForegroundColor Cyan
    Write-Host " [YARP LB LAB] DANG PUBLISH DEMO: $($Target.Display)..." -ForegroundColor Cyan
    Write-Host " Lenh: dotnet publish $($Target.ProjectPath) -c Release -o $($Target.PublishPath)" -ForegroundColor DarkGray
    Write-Host "==============================================================================" -ForegroundColor Cyan

    $proc = Start-Process dotnet -ArgumentList @(
        'publish',
        $Target.ProjectPath,
        '-c', 'Release',
        '-o', $Target.PublishPath
    ) -NoNewWindow -Wait -PassThru

    if ($proc.ExitCode -eq 0 -and (Test-Path -LiteralPath $Target.PublishPath)) {
        Write-Host ">> [SUCCESS] Publish demo thanh cong vao: $($Target.PublishPath)" -ForegroundColor Green
        return $true
    } else {
        Write-Host ">> [ERROR] Publish demo that bai (ExitCode: $($proc.ExitCode))." -ForegroundColor Red
        return $false
    }
}

function Invoke-AppDeployPublishTarget {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Target
    )
    $deployRoot = Get-AppDeployRoot
    if (-not $deployRoot) {
        Write-Host ">> [ERROR] Khong tim thay thu muc AppDeploy toolkit tren he thong!" -ForegroundColor Red
        return $false
    }

    $deployScript = Join-Path $deployRoot 'Deploy-App.ps1'
    Write-Host ""
    Write-Host "==============================================================================" -ForegroundColor Cyan
    Write-Host " [APPDEPLOY] DANG BUILD & PUBLISH: $($Target.Display)..." -ForegroundColor Cyan
    Write-Host " Lenh: Deploy-App.ps1 -App $($Target.AppId) -Mode publish -Targets $($Target.TargetId)" -ForegroundColor DarkGray
    Write-Host "==============================================================================" -ForegroundColor Cyan

    $proc = Start-Process powershell -ArgumentList @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $deployScript,
        '-App', $Target.AppId,
        '-Mode', 'publish',
        '-Targets', $Target.TargetId
    ) -NoNewWindow -Wait -PassThru

    if ($proc.ExitCode -eq 0 -and (Test-Path -LiteralPath $Target.PublishPath)) {
        Write-Host ">> [SUCCESS] Publish thanh cong vao: $($Target.PublishPath)" -ForegroundColor Green
        return $true
    } else {
        Write-Host ">> [ERROR] Publish that bai (ExitCode: $($proc.ExitCode))." -ForegroundColor Red
        return $false
    }
}

function Invoke-TargetPublish {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Target
    )
    if ($Target.IsLabDemo) {
        return (Invoke-LabDemoPublishTarget -Target $Target)
    } else {
        return (Invoke-AppDeployPublishTarget -Target $Target)
    }
}

function Select-AppDeployPathInteractive {
    param(
        [string]$PromptTitle = "CHON UNG DUNG CHO LOAD BALANCER",
        [switch]$AllowDemoOption
    )

    $targets = Get-AppDeployTargets
    $labDemos = Get-LabDemoTargets
    $deployRoot = Get-AppDeployRoot

    Write-Host ""
    Write-Host "==============================================================================" -ForegroundColor Cyan
    Write-Host "         $PromptTitle" -ForegroundColor Cyan
    Write-Host "==============================================================================" -ForegroundColor Cyan

    if ($deployRoot) {
        Write-Host " (Phat hien AppDeploy tai: $deployRoot)" -ForegroundColor DarkGray
    }

    Write-Host ""
    Write-Host " CAC UNG DUNG SAN CO TU APPDEPLOY:" -ForegroundColor Yellow
    foreach ($t in $targets) {
        $status = if ($t.IsPublished) { "[DA PUBLISH]" } else { "[CHUA BUILD/PUBLISH]" }
        $color = if ($t.IsPublished) { "Green" } else { "DarkYellow" }
        Write-Host "   [$($t.Id)] $($t.Display) " -NoNewline
        Write-Host $status -ForegroundColor $color
        Write-Host "       Path: $($t.PublishPath)" -ForegroundColor DarkGray
    }

    Write-Host ""
    Write-Host " CAC UNG DUNG DEMO TRONG YARP LB LAB:" -ForegroundColor Yellow
    foreach ($d in $labDemos) {
        $status = if ($d.IsPublished) { "[DA PUBLISH]" } else { "[CHUA PUBLISH]" }
        $color = if ($d.IsPublished) { "Green" } else { "DarkYellow" }
        $pad = if ([int]$d.Id -lt 10) { " " } else { "" }
        Write-Host "   [$($d.Id)]$pad $($d.Display) " -NoNewline
        Write-Host $status -ForegroundColor $color
        Write-Host "       Path: $($d.PublishPath)" -ForegroundColor DarkGray
    }

    Write-Host ""
    Write-Host " TUY CHON KHAC:" -ForegroundColor Yellow
    Write-Host "   [13] Nhap duong dan thu muc tuy chinh (Custom AppPath) [hoac go 'c']"
    if ($AllowDemoOption) {
        Write-Host "   [0]  Bo qua / Su dung ung dung DEMO mac dinh cua lab"
    }

    Write-Host ""
    $promptHint = if ($AllowDemoOption) { ">> Nhap lua chon cua ban (1-13, 0 = bo qua, C = Custom): " } else { ">> Nhap lua chon cua ban (1-13, C = Custom): " }
    $choice = Read-Host $promptHint
    $choice = $choice.Trim()

    if ($AllowDemoOption -and ($choice -eq '0' -or [string]::IsNullOrWhiteSpace($choice))) {
        return ""
    }

    $all = @($targets) + @($labDemos)
    $matched = $all | Where-Object { 
        $_.Id -eq $choice -or 
        ($_.Aliases -and $_.Aliases -contains $choice.ToLowerInvariant()) -or 
        $_.Key.ToLowerInvariant() -eq $choice.ToLowerInvariant() 
    }

    if ($matched) {
        if (-not $matched.IsPublished) {
            Write-Host ""
            Write-Host ">> Ung dung '$($matched.Display)' chua duoc publish tai '$($matched.PublishPath)'." -ForegroundColor Yellow
            $wantBuild = Read-Host "Ban co muon build & publish ngay bay gio khong? (Y/N, Enter = Y)"
            if ([string]::IsNullOrWhiteSpace($wantBuild) -or $wantBuild.Trim().ToUpper().StartsWith('Y')) {
                $ok = Invoke-TargetPublish -Target $matched
                if (-not $ok) {
                    Write-Host ">> Khong the tiep tuc vi publish that bai." -ForegroundColor Red
                    return $null
                }
            } else {
                return $null
            }
        } else {
            # Da publish, hoi xem co muon build lai ban moi nhat khong
            $wantRebuild = Read-Host "Ban co muon build & publish lai ban moi nhat khong? (Y/N, Enter = N)"
            if ($wantRebuild -and $wantRebuild.Trim().ToUpper().StartsWith('Y')) {
                Invoke-TargetPublish -Target $matched | Out-Null
            }
        }
        return $matched.PublishPath
    }

    # Neu chon 13 hoac c hoac custom hoac nhap thang path
    if ($choice -eq '13' -or $choice.ToLowerInvariant() -eq 'c' -or $choice.ToLowerInvariant() -eq 'custom' -or (-not $matched -and -not [string]::IsNullOrWhiteSpace($choice))) {
        if ($choice -ne '13' -and $choice.ToLowerInvariant() -ne 'c' -and $choice.ToLowerInvariant() -ne 'custom') {
            # Neu nguoi dung paste thang duong dan ton tai
            if (Test-Path -LiteralPath $choice) { return (Resolve-Path -LiteralPath $choice).Path }
        }
        $custom = Read-Host ">> Nhap duong dan folder publish (vd: D:\Publish\MyApp)"
        if ([string]::IsNullOrWhiteSpace($custom)) { return "" }
        if (Test-Path -LiteralPath $custom) {
            return (Resolve-Path -LiteralPath $custom).Path
        } else {
            Write-Host ">> [ERROR] Duong dan '$custom' khong ton tai!" -ForegroundColor Red
            return $null
        }
    }

    return ""
}
