#Requires -Version 5.1
[CmdletBinding()]
param([string] $Godot = 'godot', [int] $TimeoutSeconds = 60, [switch] $Capture)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$engine = (Get-Command $Godot -CommandType Application).Source
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$logs = Join-Path $repo "artifacts/inventory-smoke/$runId"
$null = New-Item -ItemType Directory -Path $logs -Force
$started = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$socket = New-Object Net.Sockets.UdpClient
try { $socket.Client.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback, 0))); $port = $socket.Client.LocalEndPoint.Port }
finally { $socket.Dispose() }
function Launch([string] $name, [string] $profile, [bool] $exercise = $false, [string] $foreign = '', [string] $grant = '') {
    $arguments = @('--max-fps','60','--path',('"' + (Join-Path $repo 'apps/game') + '"'),'--log-file',('"' + (Join-Path $logs "$name.engine.log") + '"'))
    if ($Capture -and $name -eq 'items-a') { $arguments += @('--position','-10000,-10000','--resolution','1280x720') }
    else { $arguments += '--headless' }
    if ($profile -ne '') {
        $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/inventory-client.gd')).Replace('__EXERCISE__', $exercise.ToString().ToLowerInvariant()).Replace('__CAPTURE__', ($Capture -and $name -eq 'items-a').ToString().ToLowerInvariant()).Replace('__FOREIGN_STACK__', $foreign)
        $script = Join-Path $logs "$name.gd"; [IO.File]::WriteAllText($script, $source, (New-Object Text.UTF8Encoding($false)))
        $arguments += @('--script',('"' + $script + '"'))
    }
    $arguments += @('--','--port',$port)
    if ($profile -eq '') {
        $arguments += @('--server','--data-dir',('"' + (Join-Path $logs 'characters') + '"'))
        if ($grant -ne '') { $arguments += @('--grant-inventory',$grant) }
    } else { $arguments += @('--profile',"$profile-$runId",'--smoke-role','inventory') }
    $parameters = @{ FilePath=$engine; ArgumentList=$arguments; WorkingDirectory=$repo; PassThru=$true;
        RedirectStandardOutput=(Join-Path $logs "$name.stdout.log"); RedirectStandardError=(Join-Path $logs "$name.stderr.log") }
    if ($env:OS -eq 'Windows_NT') { $parameters.WindowStyle = 'Hidden' }
    $process = Start-Process @parameters; $null = $process.Handle; $started.Add($process); return $process
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
    throw "Timeout $name / $pattern; inspect $logs"
}
function State([string] $name) {
    return ((Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern '^INVENTORY state=').Line -replace '^INVENTORY state=', '')
}
function Saved([string] $id) { return Get-Content -LiteralPath (Join-Path $logs ('characters/characters/' + ([Guid]$id).ToString('N') + '.json')) -Raw | ConvertFrom-Json }
try {
    $server = Launch 'create-server' ''; Wait-ForLog 'create-server' 'Arvenok server ready'
    $a = Launch 'empty-a' 'a'; $b = Launch 'empty-b' 'b'
    Wait-ForLog 'empty-a' 'INVENTORY complete'; Wait-ForLog 'empty-b' 'INVENTORY complete'
    $emptyA = State 'empty-a' | ConvertFrom-Json; $emptyB = State 'empty-b' | ConvertFrom-Json
    if ($emptyA.Slots.Count -ne 0 -or $emptyB.Slots.Count -ne 0 -or $emptyA.Capacity -ne 24 -or $emptyB.Capacity -ne 24 -or $emptyA.CharacterId -eq $emptyB.CharacterId) { throw 'New inventories must be independent and empty.' }
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'create-server' 'left\. Total: 0'; Stop-Game $server
    foreach ($id in @($emptyA.CharacterId,$emptyB.CharacterId)) {
        foreach ($attempt in 1..2) {
            $name = 'grant-' + $id.Substring(0,8) + '-' + $attempt
            $process = Launch $name '' $false '' $id
            if (-not $process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw "Offline development grant failed; inspect $logs" }
            $expected = if ($attempt -eq 1) { 'duplicate=False' } else { 'duplicate=True' }
            if (-not (Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern $expected -Quiet)) { throw 'Development grant replay was not idempotent.' }
            Stop-Game $process
        }
    }
    $kitA = Saved $emptyA.CharacterId; $kitB = Saved $emptyB.CharacterId
    $foreignA = $kitB.Inventory.Slots[0].Stack.Id; $foreignB = $kitA.Inventory.Slots[0].Stack.Id
    $server = Launch 'items-server' ''; Wait-ForLog 'items-server' 'Arvenok server ready'
    $a = Launch 'items-a' 'a' $true $foreignA; $b = Launch 'items-b' 'b' $true $foreignB
    Wait-ForLog 'items-a' 'INVENTORY complete'; Wait-ForLog 'items-b' 'INVENTORY complete'
    $states = @((State 'items-a'), (State 'items-b'))
    foreach ($name in @('items-a','items-b')) { Wait-ForLog $name 'INVENTORY UI passed' }
    foreach ($state in $states) {
        $read = $state | ConvertFrom-Json; $saved = Saved $read.CharacterId
        if ($saved.Inventory.Revision -ne $read.Revision) { throw 'Client success preceded durable inventory commit.' }
    }
    $allA = @((($states[0] | ConvertFrom-Json).Slots).Stack.Id)
    foreach ($slot in ($states[1] | ConvertFrom-Json).Slots) { if ($slot.Stack.Id -in $allA) { throw 'Two characters share an owned item identity.' } }
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'items-server' 'left\. Total: 0'
    $a = Launch 'reconnect-a' 'a'; $b = Launch 'reconnect-b' 'b'
    Wait-ForLog 'reconnect-a' 'INVENTORY complete'; Wait-ForLog 'reconnect-b' 'INVENTORY complete'
    if ((State 'reconnect-a') -ne $states[0] -or (State 'reconnect-b') -ne $states[1]) { throw 'Reconnect changed inventory.' }
    Stop-Game $a; Stop-Game $b; Stop-Game $server
    $server = Launch 'restart-server' ''; Wait-ForLog 'restart-server' 'Arvenok server ready'
    $a = Launch 'restart-a' 'a'; $b = Launch 'restart-b' 'b'
    Wait-ForLog 'restart-a' 'INVENTORY complete'; Wait-ForLog 'restart-b' 'INVENTORY complete'
    if ((State 'restart-a') -ne $states[0] -or (State 'restart-b') -ne $states[1]) { throw 'Server restart changed inventory.' }
    foreach ($log in (Get-ChildItem -LiteralPath $logs -Filter '*.log')) {
        if (Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Parse Error|INVENTORY CHECK FAILED|Unhandled exception|Failed to instantiate|RPC.*mismatch|System\..*Exception:|Error loading resource' -Quiet) { throw "Inventory engine failure: $($log.FullName)" }
    }
    Write-Host 'Inventory smoke passed: two empty backpacks, offline one-time grants, native drag/swap/split/merge/sort, private ownership, durable acknowledgements, duplicate/stale rejection, reconnect and restart.'
    Write-Host "Logs: $logs"
} finally { foreach ($process in @($started.ToArray())) { Stop-Game $process } }
