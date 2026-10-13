# Helper quan ly tunnel ngrok cho Load Balancer (:5266)

function Ask-NgrokOption {
    param(
        [int]$Port = 5266
    )

    if (-not (Get-Command ngrok -ErrorAction SilentlyContinue)) {
        return $null
    }

    Write-Host ""
    Write-Host "==============================================================================" -ForegroundColor Yellow
    Write-Host "  BAN CO MUON TAO LINK NGROK CONG KHAI CHO http://localhost:$Port/ KHONG?" -ForegroundColor Yellow
    Write-Host "==============================================================================" -ForegroundColor Yellow
    Write-Host "  [Y] = Co, tao link https://...ngrok-free.app (de test dien thoai / share ngoai)" -ForegroundColor Cyan
    Write-Host "  [Enter / N] = Khong, chi dung localhost:$Port (mac dinh)" -ForegroundColor Gray
    Write-Host ""
    $ans = Read-Host ">> Lua chon [y/N, Enter = N]"
    if ($ans -match "^(y|yes|1)$") {
        return (Start-NgrokTunnel -Port $Port)
    }
    return $null
}

function Start-NgrokTunnel {
    param([int]$Port = 5266)

    # Kiem tra xem da co ngrok dang chay san chua
    try {
        $existing = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels" -TimeoutSec 1 -ErrorAction SilentlyContinue
        if ($existing -and $existing.tunnels -and $existing.tunnels.Count -gt 0) {
            $https = $existing.tunnels | Where-Object { $_.public_url -like "https://*" } | Select-Object -First 1
            $url = if ($https) { $https.public_url } else { $existing.tunnels[0].public_url }
            Write-Host ">> [ngrok] Da co tunnel dang chay: $url" -ForegroundColor Green
            return @{ Process = $null; Url = $url }
        }
    } catch {}

    Write-Host ">> [ngrok] Dang khoi dong tunnel ngrok cho port $Port..." -ForegroundColor Cyan
    $proc = Start-Process ngrok -ArgumentList "http $Port" -WindowStyle Minimized -PassThru

    $publicUrl = $null
    for ($i = 0; $i -lt 12; $i++) {
        Start-Sleep -Milliseconds 400
        try {
            $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels" -TimeoutSec 1 -ErrorAction SilentlyContinue
            if ($tunnels -and $tunnels.tunnels -and $tunnels.tunnels.Count -gt 0) {
                $https = $tunnels.tunnels | Where-Object { $_.public_url -like "https://*" } | Select-Object -First 1
                $publicUrl = if ($https) { $https.public_url } else { $tunnels.tunnels[0].public_url }
                break
            }
        } catch {}
    }

    if ($publicUrl) {
        Write-Host ""
        Write-Host "==============================================================================" -ForegroundColor Green
        Write-Host "  >> NGROK PUBLIC URL  : $publicUrl" -ForegroundColor Yellow
        Write-Host "  >> DASHBOARD PUBLIC   : $publicUrl/dashboard" -ForegroundColor Cyan
        Write-Host "==============================================================================" -ForegroundColor Green
        Write-Host ""
    } else {
        Write-Host ">> [ngrok] Chua lay duoc URL tu ngrok (kiem tra auth token hoac cua so ngrok)." -ForegroundColor Yellow
    }

    return @{ Process = $proc; Url = $publicUrl }
}

function Stop-NgrokTunnel {
    param($NgrokInfo)
    if ($NgrokInfo -and $NgrokInfo.Process -and -not $NgrokInfo.Process.HasExited) {
        Write-Host ">> [ngrok] Dang dong tunnel..." -ForegroundColor Yellow
        try {
            Stop-Process -Id $NgrokInfo.Process.Id -Force -ErrorAction SilentlyContinue
        } catch {}
    }
}
