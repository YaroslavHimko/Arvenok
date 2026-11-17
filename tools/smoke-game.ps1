#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Godot = 'godot',
    [ValidateRange(5, 120)] [int] $TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'apps/game'
$engine = (Get-Command $Godot -CommandType Application -ErrorAction Stop).Source
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$logs = Join-Path $repo "artifacts/smoke/$runId"
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$started = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'

# Select a free loopback port. If another process claims it before startup, the check fails clearly.
$socket = New-Object Net.Sockets.UdpClient
try {
    $socket.Client.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback, 0)))
    $port = $socket.Client.LocalEndPoint.Port
}
finally { $socket.Dispose() }

function Start-Game([string] $name, [bool] $server) {
    $arguments = @('--headless', '--max-fps', '60', '--path', ('"' + $project + '"'),
        '--log-file', ('"' + (Join-Path $logs "$name.engine.log") + '"'), '--',
        '--port', $port, '--host', '127.0.0.1', '--bind', '127.0.0.1', '--region', 'local', '--shard', 'smoke')
    if ($server) { $arguments += '--server' }
    $parameters = @{
        FilePath = $engine
        ArgumentList = $arguments
        WorkingDirectory = $repo
        RedirectStandardOutput = (Join-Path $logs "$name.stdout.log")
        RedirectStandardError = (Join-Path $logs "$name.stderr.log")
        PassThru = $true
    }
    if ($env:OS -eq 'Windows_NT') { $parameters.WindowStyle = 'Hidden' }
    $process = Start-Process @parameters
    $started.Add($process)
}

function Wait-ForLog([string] $name, [string] $pattern) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        foreach ($process in $started) {
            if ($process.HasExited) { throw "Godot exited early (PID $($process.Id)); inspect $logs" }
        }
        $path = Join-Path $logs "$name.stdout.log"
        if ((Test-Path -LiteralPath $path) -and (Select-String -LiteralPath $path -Pattern $pattern -Quiet)) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $name / $pattern. Inspect $logs"
}

try {
    Start-Game 'server' $true
    Wait-ForLog 'server' 'Arvenok server ready'
    Start-Game 'client-1' $false
    Start-Game 'client-2' $false
    Wait-ForLog 'server' 'joined\. Total: 2'
    Wait-ForLog 'client-1' 'Arena state received: 2 fighters\.'
    Wait-ForLog 'client-2' 'Arena state received: 2 fighters\.'
    foreach ($log in (Get-ChildItem -LiteralPath $logs -Filter '*.log' -File)) {
        if (Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Unhandled exception|Parse Error|Failed to instantiate|RPC.*mismatch' -Quiet) {
            throw "Engine/script error in $($log.FullName)"
        }
    }
    $engineErrors = Get-ChildItem -LiteralPath $logs -Filter '*.stderr.log' -File | Select-String -Pattern '^ERROR:'
    if ($engineErrors) { Write-Warning "Engine diagnostics were emitted; inspect stderr logs in $logs" }
    Write-Host "Multiplayer smoke passed: server and two clients exchanged state on loopback UDP $port."
    Write-Host "Logs: $logs"
}
finally {
    foreach ($process in $started) {
        try {
            if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(5000) }
        }
        finally { $process.Dispose() }
    }
}
