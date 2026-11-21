#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $Godot = 'godot',
    [ValidateRange(5, 120)] [int] $TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'apps/game'
$engine = (Get-Command $Godot -CommandType Application -ErrorAction Stop).Source
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$logs = Join-Path $repo "artifacts/smoke/$runId"
$monsterCount = (Get-Content -LiteralPath (Join-Path $repo 'content/worlds/woodvale-forest.json') -Raw | ConvertFrom-Json).monsterSpawns.Count
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$started = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'

# Select a free loopback port. If another process claims it before startup, the check fails clearly.
$listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()

function Start-Game([string] $name, [bool] $server, [string] $role = 'none') {
    $arguments = @('--headless', '--max-fps', '60', '--path', ('"' + $project + '"'),
        '--log-file', ('"' + (Join-Path $logs "$name.engine.log") + '"'), '--',
        '--port', $port, '--host', '127.0.0.1', '--bind', '127.0.0.1', '--region', 'local', '--shard', 'smoke')
    if ($server) { $arguments += @('--server', '--data-dir', ('"' + (Join-Path $logs 'characters') + '"')) }
    elseif ($role -ne 'none') { $arguments += @('--smoke-role', $role) }
    if (-not $server) { $arguments += @('--profile', "$name-$runId") }
    if (-not $server) {
        $kind=if($role -eq 'attacker'){1}else{0}
        $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/account-bootstrap.gd')).Replace('__PROFILE__',('s'+$runId.Substring(16,6)+$name.Replace('-',''))).Replace('__KIND__',[string]$kind)
        $script=Join-Path $logs "$name.gd"
        [IO.File]::WriteAllText($script,$source,(New-Object Text.UTF8Encoding($false)))
        $arguments=@('--script',('"'+$script+'"'))+$arguments
    }
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
    $null=$process.Handle; $started.Add($process)
    return $process
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
    $serverProcess = Start-Game 'server' $true
    Wait-ForLog 'server' 'Arvenok server ready'
    $firstClient = Start-Game 'client-1' $false 'attacker'
    $secondClient = Start-Game 'client-2' $false 'defender'
    Wait-ForLog 'server' 'joined\. Total: 2'
    Wait-ForLog 'client-1' 'World state received: 2 players\.'
    Wait-ForLog 'client-2' 'World state received: 2 players\.'
    Wait-ForLog 'client-1' 'Woodvale art ready:'
    Wait-ForLog 'client-2' 'Woodvale art ready:'
    Wait-ForLog 'client-1' "World monsters received: $monsterCount"
    Wait-ForLog 'client-2' "World monsters received: $monsterCount"
    Wait-ForLog 'client-1' 'SMOKE monster motion observed'
    Wait-ForLog 'client-2' 'SMOKE monster motion observed'
    Wait-ForLog 'client-1' 'SMOKE safe-zone rejection observed'
    Wait-ForLog 'client-1' 'SMOKE duplicate rejected'
    Wait-ForLog 'client-1' 'SMOKE combat complete'
    Wait-ForLog 'client-2' 'SMOKE combat complete'
    foreach ($name in @('client-1', 'client-2')) {
        Wait-ForLog $name 'Woodvale animations ready: 134 strips'
        Wait-ForLog $name 'VISUAL body .*clip=characters/mage/cast/'
        Wait-ForLog $name 'VISUAL body .*clip=characters/mage/interrupt/'
        Wait-ForLog $name 'VISUAL body .*clip=characters/warrior/death/'
        Wait-ForLog $name 'VISUAL body .*clip=monsters/.*/move'
        Wait-ForLog $name 'VISUAL aura .*active=True'
        Wait-ForLog $name 'VISUAL aura .*active=False'
        Wait-ForLog $name 'VISUAL projectile travelled .*effect=mage/firebolt$'
        Wait-ForLog $name 'VISUAL effect .*effect=mage/firebolt_impact$'
    }
    $firstEffects = @((Select-String -LiteralPath (Join-Path $logs 'client-1.stdout.log') -Pattern '^VISUAL effect ').Line)
    $secondEffects = @((Select-String -LiteralPath (Join-Path $logs 'client-2.stdout.log') -Pattern '^VISUAL effect ').Line)
    if (($firstEffects -join '|') -ne ($secondEffects -join '|')) { throw 'Clients disagree on confirmed combat visual effects.' }
    if (@($firstEffects | Select-Object -Unique).Count -ne $firstEffects.Count) { throw 'A combat effect was replayed for a duplicate event.' }
    $firstOutcome = (Select-String -LiteralPath (Join-Path $logs 'client-1.stdout.log') -Pattern '^SMOKE outcome').Line
    $secondOutcome = (Select-String -LiteralPath (Join-Path $logs 'client-2.stdout.log') -Pattern '^SMOKE outcome').Line
    if ($firstOutcome -ne $secondOutcome) { throw "Clients disagree on combat outcomes: $firstOutcome / $secondOutcome" }
    $monsterStates = @{}
    foreach ($line in (Get-Content -LiteralPath (Join-Path $logs 'client-1.stdout.log'))) {
        if ($line -match "^SMOKE monsters revision=(\d+) hash=([A-F0-9]+) count=$monsterCount`$") { $monsterStates[$Matches[1]] = $Matches[2] }
    }
    $matchingRevisions = 0
    foreach ($line in (Get-Content -LiteralPath (Join-Path $logs 'client-2.stdout.log'))) {
        if ($line -match "^SMOKE monsters revision=(\d+) hash=([A-F0-9]+) count=$monsterCount`$" -and $monsterStates.ContainsKey($Matches[1])) {
            if ($monsterStates[$Matches[1]] -ne $Matches[2]) { throw "Clients disagree on monster state at revision $($Matches[1])" }
            $matchingRevisions++
        }
    }
    if ($matchingRevisions -lt 3) { throw 'Not enough common monster revisions to verify replication.' }

    # Leave the world empty, then connect fresh clients to the same running process.
    foreach ($client in @($firstClient, $secondClient)) {
        $client.Kill(); $null = $client.WaitForExit(5000)
        $null = $started.Remove($client); $client.Dispose()
    }
    Wait-ForLog 'server' 'left\. Total: 0\. World continues'
    $null = Start-Game 'client-3' $false
    $null = Start-Game 'client-4' $false
    Wait-ForLog 'client-3' 'World state received: 2 players\.'
    Wait-ForLog 'client-4' 'World state received: 2 players\.'
    # Entity IDs are shared with monsters; do not assume that the fourth client has entity ID 4.
    Wait-ForLog 'client-3' 'Local entity assigned:'
    Wait-ForLog 'client-4' 'Local entity assigned:'
    Wait-ForLog 'client-3' "World monsters received: $monsterCount"
    Wait-ForLog 'client-4' "World monsters received: $monsterCount"
    $originalMonsters = (Select-String -LiteralPath (Join-Path $logs 'client-1.stdout.log') -Pattern '^World monsters received:').Line
    foreach ($name in @('client-3', 'client-4')) {
        $reconnectedMonsters = (Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern '^World monsters received:').Line
        if ($reconnectedMonsters -ne $originalMonsters) { throw 'World monster identities changed while the world was empty.' }
    }
    if ($serverProcess.HasExited) { throw 'The world process stopped while empty.' }
    foreach ($log in (Get-ChildItem -LiteralPath $logs -Filter '*.log' -File)) {
        if (Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Unhandled exception|Parse Error|Failed to instantiate|RPC.*mismatch|above the MTU|Missing Woodvale texture|Texture dimensions disagree|Missing animation strip|Animation strip dimensions|System\..*Exception:' -Quiet) {
            throw "Engine/script error in $($log.FullName)"
        }
    }
    $engineErrors = Get-ChildItem -LiteralPath $logs -Filter '*.stderr.log' -File | Select-String -Pattern '^ERROR:'
    if ($engineErrors) { Write-Warning "Engine diagnostics were emitted; inspect stderr logs in $logs" }
    Write-Host "Multiplayer smoke passed: combat, animations, aura expiry, $($firstEffects.Count) matching visual effects, respawn, reconnect and $matchingRevisions matching wandering-monster snapshots over TLS/TCP $port."
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
