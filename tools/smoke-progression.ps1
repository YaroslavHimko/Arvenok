#Requires -Version 5.1
[CmdletBinding()]
param([string] $Godot = 'godot', [int] $TimeoutSeconds = 240, [switch] $Capture)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$engine = (Get-Command $Godot -CommandType Application).Source
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$logs = Join-Path $repo "artifacts/progression-smoke/$runId"
$null = New-Item -ItemType Directory -Path $logs -Force
$started = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$socket = New-Object Net.Sockets.UdpClient
try { $socket.Client.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback, 0))); $port = $socket.Client.LocalEndPoint.Port }
finally { $socket.Dispose() }
function Launch([string] $name, [string] $profile, [string] $role) {
    $arguments = @('--max-fps', '60', '--path', ('"' + (Join-Path $repo 'apps/game') + '"'), '--log-file', ('"' + (Join-Path $logs "$name.engine.log") + '"'))
    if ($Capture -and $name -eq 'first-a') {
        $script = Join-Path $logs 'capture.gd'
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/capture-progression.gd') -Destination $script
        $arguments += @('--position', '-10000,-10000', '--resolution', '1280x720', '--script', ('"' + $script + '"'))
    } else { $arguments += '--headless' }
    $arguments += @('--', '--port', $port)
    if ($profile -eq '') { $arguments += @('--server', '--data-dir', ('"' + (Join-Path $logs 'characters') + '"')) }
    else { $arguments += @('--profile', "$profile-$runId", '--smoke-role', $role) }
    $parameters = @{ FilePath = $engine; ArgumentList = $arguments; WorkingDirectory = $repo; PassThru = $true;
        RedirectStandardOutput = (Join-Path $logs "$name.stdout.log"); RedirectStandardError = (Join-Path $logs "$name.stderr.log") }
    if ($env:OS -eq 'Windows_NT') { $parameters.WindowStyle = 'Hidden' }
    $process = Start-Process @parameters; $started.Add($process); return $process
}
function Stop-Game($process) {
    if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(5000) }
    $null = $started.Remove($process); $process.Dispose()
}
function Wait-ForLog([string] $name, [string] $pattern) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        foreach ($process in $started) { if ($process.HasExited) { throw "Process exited early; inspect $logs" } }
        $path = Join-Path $logs "$name.stdout.log"
        if ((Test-Path -LiteralPath $path) -and (Select-String -LiteralPath $path -Pattern $pattern -Quiet)) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Timeout: $name / $pattern; inspect $logs"
}
function State([string] $name) {
    return ((Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern '^PROGRESSION state=').Line -replace '^PROGRESSION state=', '')
}
try {
    $server = Launch 'server' '' ''; Wait-ForLog 'server' 'Arvenok server ready'
    $a = Launch 'first-a' 'a' 'progression-a'; $b = Launch 'first-b' 'b' 'progression-b'
    Wait-ForLog 'first-a' 'PROGRESSION complete=earned'; Wait-ForLog 'first-b' 'PROGRESSION complete=earned'
    $states = @((State 'first-a'), (State 'first-b'))
    $deathA = @((Select-String -LiteralPath (Join-Path $logs 'first-a.stdout.log') -Pattern '^PROGRESSION death=').Line)
    $deathB = @((Select-String -LiteralPath (Join-Path $logs 'first-b.stdout.log') -Pattern '^PROGRESSION death=').Line)
    if ($deathA.Count -lt 2 -or ($deathA -join '|') -ne ($deathB -join '|')) { throw 'Clients disagree on shared monster deaths.' }
    $total = 0
    foreach ($name in @('first-a', 'first-b')) {
        foreach ($match in (Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern '^PROGRESSION award=(\d+)')) { $total += [int]$match.Matches[0].Groups[1].Value }
        Wait-ForLog $name 'VISUAL level up'
        $state = State $name | ConvertFrom-Json
        if ($state.Level -ne 2 -or $state.SkillPoints -ne 0 -or $state.Ranks.Primary -ne 2 -or $state.Ranks.Mobility -ne 1) { throw 'Rank or point conservation failed.' }
    }
    if ($total -ne 90 * $deathA.Count) { throw "Shared rewards violate the death budget: $total EXP / $($deathA.Count) deaths." }
    if ($Capture) { Wait-ForLog 'first-a' 'CAPTURE progression complete' }
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'server' 'left\. Total: 0'
    $a = Launch 'reconnect-a' 'a' 'progression-restore'; $b = Launch 'reconnect-b' 'b' 'progression-restore'
    Wait-ForLog 'reconnect-a' 'PROGRESSION complete=restored'; Wait-ForLog 'reconnect-b' 'PROGRESSION complete=restored'
    if ((State 'reconnect-a') -ne $states[0] -or (State 'reconnect-b') -ne $states[1]) { throw 'Reconnect changed progression.' }
    Stop-Game $a; Stop-Game $b
    # Observe durable revision before intentionally terminating the process.
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    foreach ($json in $states) {
        $state = $json | ConvertFrom-Json
        $path = Join-Path $logs ('characters/characters/' + ([Guid]$state.CharacterId).ToString('N') + '.json')
        do {
            $saved = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            if ($saved.Revision -eq $state.Revision) { break }
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Final progression revision was not saved.' }
            Start-Sleep -Milliseconds 100
        } while ($true)
    }
    Stop-Game $server
    $server = Launch 'restarted-server' '' ''; Wait-ForLog 'restarted-server' 'Arvenok server ready'
    $a = Launch 'restart-a' 'a' 'progression-restore'; $b = Launch 'restart-b' 'b' 'progression-restore'
    Wait-ForLog 'restart-a' 'PROGRESSION complete=restored'; Wait-ForLog 'restart-b' 'PROGRESSION complete=restored'
    if ((State 'restart-a') -ne $states[0] -or (State 'restart-b') -ne $states[1]) { throw 'Server restart changed progression.' }
    foreach ($log in (Get-ChildItem -LiteralPath $logs -Filter '*.log')) {
        if (Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Unhandled exception|Parse Error|Failed to instantiate|RPC.*mismatch|above the MTU|System\..*Exception:' -Quiet) { throw "Engine/script failure: $($log.FullName)" }
    }
    Write-Host "Progression smoke passed: two clients, $($deathA.Count) shared kills, $total EXP, level-ups, rank damage, duplicate/stale upgrade rejection, exact reconnect and server restart restoration."
    Write-Host "Logs: $logs"
} finally { foreach ($process in @($started.ToArray())) { Stop-Game $process } }
