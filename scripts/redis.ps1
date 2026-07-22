# Redis tools cho yarp-lb-lab: start / stop / status / view / flush.
# Mac dinh: Docker (docker-compose.yml, port 6380). Fallback: Redis native Windows.
#
# Vi du:
#   .\scripts\redis.ps1                 # menu
#   .\scripts\redis.ps1 start
#   .\scripts\redis.ps1 status
#   .\scripts\redis.ps1 view            # xem key lab (LbDemo*, SimpleLogin*)
#   .\scripts\redis.ps1 flush -Force    # FLUSHDB 0
#   .\scripts\redis.ps1 stop
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("menu", "start", "stop", "status", "view", "flush", "")]
    [string]$Action = "menu",

    [string]$HostName = "127.0.0.1",
    [int]$Port = 6380,
    [int]$Database = 0,
    [switch]$AllKeys,   # view: hien tat ca key, khong chi prefix lab
    [switch]$Force,     # flush: khong hoi
    [switch]$Native,    # start: ep dung redis-server.exe (bo qua Docker)
    [switch]$NoDocker   # start: khong dung Docker (giong -Native neu co native)
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $RepoRoot

$LabPatterns = @("LbDemo*", "SimpleLogin*", "*DP-Keys*")
$ContainerName = "yarp-lb-lab-redis"
$DefaultNativeDir = "C:\Program Files\Redis"

function Test-PortOpen {
    param([string]$TargetHost, [int]$TargetPort)
    try {
        $tcp = Test-NetConnection -ComputerName $TargetHost -Port $TargetPort -WarningAction SilentlyContinue
        return [bool]$tcp.TcpTestSucceeded
    } catch {
        return $false
    }
}

function Find-NativeRedisExe {
    param([string]$Name) # redis-server.exe | redis-cli.exe
    $candidates = @(
        (Join-Path $DefaultNativeDir $Name),
        (Join-Path $env:ProgramFiles "Redis\$Name"),
        (Join-Path ${env:ProgramFiles(x86)} "Redis\$Name")
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    $cmd = Get-Command ($Name -replace '\.exe$', '') -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Test-DockerAvailable {
    return [bool](Get-Command docker -ErrorAction SilentlyContinue)
}

function Test-DockerRedisRunning {
    if (-not (Test-DockerAvailable)) { return $false }
    $names = docker ps --filter "name=$ContainerName" --format "{{.Names}}" 2>$null
    return ($names -split "`n" | Where-Object { $_.Trim() -eq $ContainerName }).Count -gt 0
}

function Invoke-RedisCli {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$CliArgs
    )
    $output = $null
    if (Test-DockerRedisRunning) {
        $output = & docker exec $ContainerName redis-cli -n $Database @CliArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "redis-cli (docker) that bai ($LASTEXITCODE): $output"
        }
        return $output
    }
    $cli = Find-NativeRedisExe "redis-cli.exe"
    if (-not $cli) {
        throw "Khong tim thay redis-cli (Docker container '$ContainerName' chua chay, va khong co redis-cli native)."
    }
    $output = & $cli -h $HostName -p $Port -n $Database @CliArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "redis-cli that bai ($LASTEXITCODE): $output"
    }
    return $output
}

function Get-RedisMode {
    if (Test-DockerRedisRunning) { return "docker" }
    if (Test-PortOpen $HostName $Port) { return "native-or-other" }
    return "down"
}

function Start-LabRedis {
    if (Test-PortOpen $HostName $Port) {
        Write-Host ">> Redis da lang nghe ${HostName}:${Port} ($((Get-RedisMode)))." -ForegroundColor Green
        return
    }

    $useNative = $Native -or $NoDocker -or -not (Test-DockerAvailable)
    if (-not $useNative) {
        Write-Host ">> Start Redis (docker compose) -> ${HostName}:${Port}..."
        Push-Location $RepoRoot
        try {
            docker compose up -d
            if ($LASTEXITCODE -ne 0) { throw "docker compose up that bai (exit $LASTEXITCODE)." }
        } finally {
            Pop-Location
        }
        Start-Sleep -Seconds 1
        if (Test-PortOpen $HostName $Port) {
            Write-Host ">> OK: Redis Docker dang chay." -ForegroundColor Green
            return
        }
        Write-Host ">> Docker da start nhung port $Port chua mo. Thu native..." -ForegroundColor Yellow
        $useNative = $true
    }

    $server = Find-NativeRedisExe "redis-server.exe"
    if (-not $server) {
        throw "Khong start duoc Redis. Cai Docker Desktop (khuyen nghi) hoac Redis native (C:\Program Files\Redis)."
    }
    Write-Host ">> Start Redis native: $server --port $Port"
    Start-Process -FilePath $server -ArgumentList "--port", "$Port" -WindowStyle Minimized
    Start-Sleep -Seconds 1
    if (Test-PortOpen $HostName $Port) {
        Write-Host ">> OK: Redis native dang chay tren port $Port." -ForegroundColor Green
    } else {
        throw "Da goi redis-server nhung port $Port van chua mo."
    }
}

function Stop-LabRedis {
    $mode = Get-RedisMode
    if ($mode -eq "down") {
        Write-Host ">> Redis khong chay tren ${HostName}:${Port}." -ForegroundColor Yellow
        return
    }

    if (Test-DockerRedisRunning) {
        Write-Host ">> Stop Redis Docker (compose stop)..."
        Push-Location $RepoRoot
        try {
            docker compose stop
        } finally {
            Pop-Location
        }
        Write-Host ">> OK: Da stop container." -ForegroundColor Green
        return
    }

    Write-Host ">> Gui SHUTDOWN toi ${HostName}:${Port}..."
    try {
        $null = Invoke-RedisCli SHUTDOWN NOSAVE
    } catch {
        # redis-cli thuong bao loi khi connection bi dong sau SHUTDOWN - binh thuong.
    }
    Start-Sleep -Milliseconds 500
    if (-not (Test-PortOpen $HostName $Port)) {
        Write-Host ">> OK: Redis da tat." -ForegroundColor Green
    } else {
        Write-Host ">> WARN: Port $Port van mo sau SHUTDOWN." -ForegroundColor Yellow
    }
}

function Show-LabRedisStatus {
    $open = Test-PortOpen $HostName $Port
    $mode = Get-RedisMode
    Write-Host "============================================"
    Write-Host " REDIS STATUS"
    Write-Host "============================================"
    Write-Host ("Host/Port : {0}:{1}" -f $HostName, $Port)
    Write-Host ("Listening : {0}" -f $(if ($open) { "YES" } else { "NO" }))
    Write-Host ("Mode      : {0}" -f $mode)
    Write-Host ("Docker    : {0}" -f $(if (Test-DockerAvailable) { "available" } else { "missing" }))
    Write-Host ("Native srv: {0}" -f $(if (Find-NativeRedisExe "redis-server.exe") { "found" } else { "missing" }))
    if (-not $open) { return }

    try {
        $pong = (Invoke-RedisCli PING | Out-String).Trim()
        Write-Host ("PING      : {0}" -f $pong)
        $dbsize = (Invoke-RedisCli DBSIZE | Out-String).Trim()
        Write-Host ("DBSIZE($Database): {0}" -f $dbsize)
        $info = (Invoke-RedisCli INFO server | Out-String)
        $ver = ($info -split "`n" | Where-Object { $_ -match '^redis_version:' } | Select-Object -First 1)
        if ($ver) { Write-Host ("Version   : {0}" -f ($ver -replace 'redis_version:', '').Trim()) }
    } catch {
        Write-Host ">> WARN: Port mo nhung redis-cli loi: $_" -ForegroundColor Yellow
    }
}

function Get-LabKeys {
    $keys = New-Object System.Collections.Generic.List[string]
    if ($AllKeys) {
        $raw = Invoke-RedisCli --raw KEYS "*"
        foreach ($k in @($raw)) {
            if ($k -and "$k".Trim()) { [void]$keys.Add("$k".Trim()) }
        }
        return $keys
    }
    foreach ($pat in $LabPatterns) {
        $raw = Invoke-RedisCli --raw KEYS $pat
        foreach ($k in @($raw)) {
            $t = "$k".Trim()
            if ($t -and -not $keys.Contains($t)) { [void]$keys.Add($t) }
        }
    }
    return $keys
}

function Show-KeyDetail {
    param([string]$Key)
    $type = ("$(Invoke-RedisCli --raw TYPE $Key)").Trim()
    $ttl = ("$(Invoke-RedisCli --raw TTL $Key)").Trim()
    Write-Host ""
    Write-Host "--------------------------------------------------"
    Write-Host "KEY  : $Key"
    Write-Host "TYPE : $type"
    Write-Host "TTL  : $ttl"
    Write-Host "VALUE:"
    switch ($type.ToLowerInvariant()) {
        "string" { Invoke-RedisCli --raw GET $Key | Out-Host }
        "hash"   { Invoke-RedisCli HGETALL $Key | Out-Host }
        "list"   { Invoke-RedisCli LRANGE $Key 0 -1 | Out-Host }
        "set"    { Invoke-RedisCli SMEMBERS $Key | Out-Host }
        "zset"   { Invoke-RedisCli ZRANGE $Key 0 -1 WITHSCORES | Out-Host }
        "stream" { Invoke-RedisCli XRANGE $Key - + | Out-Host }
        "none"   { Write-Host "[missing]" }
        default  { Write-Host "[skip type=$type]" }
    }
}

function Show-LabRedisView {
    if (-not (Test-PortOpen $HostName $Port)) {
        throw "Redis khong lang nghe ${HostName}:${Port}. Chay: .\scripts\redis.ps1 start"
    }
    $scope = if ($AllKeys) { "ALL keys" } else { "lab keys ($($LabPatterns -join ', '))" }
    Write-Host "============================================"
    Write-Host " REDIS VIEW ($scope)  db=$Database"
    Write-Host "============================================"
    $keys = @(Get-LabKeys)
    Write-Host ("Tim thay {0} key(s)." -f $keys.Count)
    if ($keys.Count -eq 0) {
        Write-Host ">> Trong. Chay demo redis/login-redis de tao session + DP keys." -ForegroundColor Yellow
        return
    }
    foreach ($k in ($keys | Sort-Object)) {
        Show-KeyDetail -Key $k
    }
}

function Clear-LabRedis {
    if (-not (Test-PortOpen $HostName $Port)) {
        throw "Redis khong lang nghe ${HostName}:${Port}."
    }
    if (-not $Force) {
        $keys = @(Get-LabKeys)
        Write-Host ("Sap FLUSHDB {0} tren {1}:{2} (hien co ~{3} lab key(s) theo pattern)." -f $Database, $HostName, $Port, $keys.Count)
        $ok = Read-Host "Xac nhan? (y/N)"
        if ($ok -notin @("y", "Y", "yes", "YES")) {
            Write-Host ">> Huy flush."
            return
        }
    }
    Invoke-RedisCli FLUSHDB | Out-Null
    Write-Host ">> OK: FLUSHDB $Database." -ForegroundColor Green
}

function Show-Menu {
    while ($true) {
        Write-Host ""
        Write-Host "============================================"
        Write-Host " REDIS TOOLS (yarp-lb-lab)  ${HostName}:${Port}"
        Write-Host "============================================"
        Write-Host "  1) start"
        Write-Host "  2) stop"
        Write-Host "  3) status"
        Write-Host "  4) view lab keys"
        Write-Host "  5) view ALL keys"
        Write-Host "  6) flush DB $Database"
        Write-Host "  0) exit"
        Write-Host ""
        $choice = Read-Host "Chon"
        switch ($choice) {
            "1" { try { Start-LabRedis } catch { Write-Host ">> ERROR: $_" -ForegroundColor Red } }
            "2" { try { Stop-LabRedis } catch { Write-Host ">> ERROR: $_" -ForegroundColor Red } }
            "3" { try { Show-LabRedisStatus } catch { Write-Host ">> ERROR: $_" -ForegroundColor Red } }
            "4" { $script:AllKeys = $false; try { Show-LabRedisView } catch { Write-Host ">> ERROR: $_" -ForegroundColor Red } }
            "5" { $script:AllKeys = $true; try { Show-LabRedisView } catch { Write-Host ">> ERROR: $_" -ForegroundColor Red }; $script:AllKeys = $false }
            "6" { try { Clear-LabRedis } catch { Write-Host ">> ERROR: $_" -ForegroundColor Red } }
            "0" { return }
            default { Write-Host "Lua chon khong hop le." -ForegroundColor Yellow }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Action) -or $Action -eq "menu") {
    Show-Menu
    exit 0
}

switch ($Action) {
    "start"  { Start-LabRedis }
    "stop"   { Stop-LabRedis }
    "status" { Show-LabRedisStatus }
    "view"   { Show-LabRedisView }
    "flush"  { Clear-LabRedis }
}
