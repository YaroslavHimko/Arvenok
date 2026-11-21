#Requires -Version 5.1
[CmdletBinding()]
param([string] $Godot = 'godot', [int] $TimeoutSeconds = 180, [switch] $Capture)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$engine = (Get-Command $Godot -CommandType Application).Source
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$logs = Join-Path $repo "artifacts/economy-smoke/$runId"
$null = New-Item -ItemType Directory -Path $logs -Force
$started = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$socket = New-Object Net.Sockets.UdpClient
try { $socket.Client.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback, 0))); $port = $socket.Client.LocalEndPoint.Port }
finally { $socket.Dispose() }
function Launch([string] $name, [string] $role, [string] $mode = 'restore') {
    $arguments = @('--max-fps','60','--path',('"' + (Join-Path $repo 'apps/game') + '"'),'--log-file',('"' + (Join-Path $logs "$name.engine.log") + '"'))
    $render = $Capture -and $name -eq 'exercise-a'
    if ($render) { $arguments += @('--position','-10000,-10000','--resolution','1280x720') } else { $arguments += '--headless' }
    if ($role -ne '') {
        $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/economy-client.gd')).Replace('__ROLE__',$role).Replace('__MODE__',$mode).Replace('__CAPTURE__',$render.ToString().ToLowerInvariant())
        $script = Join-Path $logs "$name.gd"; [IO.File]::WriteAllText($script,$source,(New-Object Text.UTF8Encoding($false)))
        $arguments += @('--script',('"' + $script + '"'))
    }
    $arguments += @('--','--port',$port)
    if ($role -eq '') { $arguments += @('--server','--data-dir',('"' + (Join-Path $logs 'characters') + '"')) }
    else { $arguments += @('--profile',"economy-$role-$runId") }
    $parameters = @{FilePath=$engine;ArgumentList=$arguments;WorkingDirectory=$repo;PassThru=$true;RedirectStandardOutput=(Join-Path $logs "$name.stdout.log");RedirectStandardError=(Join-Path $logs "$name.stderr.log")}
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
function State([string] $name) { return ((Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern '^ECONOMY state=').Line -replace '^ECONOMY state=', '') }
function Save-Path([string] $id) { return Join-Path $logs ('characters/characters/' + ([Guid]$id).ToString('N') + '.json') }
function Item([int] $index, [string] $id, [int] $quantity = 1) {
    return @{Index=$index;Stack=@{Id=[Guid]::NewGuid().ToString();ItemId=$id;Quantity=$quantity;Properties=@()}}
}
try {
    $server = Launch 'create-server' ''; Wait-ForLog 'create-server' 'Arvenok server ready'
    $a = Launch 'create-a' 'a' 'create'; $b = Launch 'create-b' 'b' 'create'
    Wait-ForLog 'create-a' 'ECONOMY complete'; Wait-ForLog 'create-b' 'ECONOMY complete'
    $aId = (State 'create-a' | ConvertFrom-Json).CharacterId; $bId = (State 'create-b' | ConvertFrom-Json).CharacterId
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'create-server' 'left\. Total: 0'; Stop-Game $server
    # Test fixtures only: the isolated server is stopped and releases its save-directory lease.
    $aSave = Get-Content -LiteralPath (Save-Path $aId) -Raw | ConvertFrom-Json
    $aSave.Level=5; $aSave.SkillPoints=4; $aSave.Revision++; $aSave.ClassLocked=$true
    $aSave.Inventory.Revision++
    $aSave.Inventory.Slots = @((Item 0 'equipment.rustleaf_sword'),(Item 1 'equipment.padded_guard_coat'),(Item 2 'recipe.ironbark_sword'),
        (Item 3 'material.stone_shard' 12),(Item 4 'material.wolf_fang' 7),(Item 5 'material.warrior_crest'),(Item 6 'recipe.ironbark_sword'))
    [IO.File]::WriteAllText((Save-Path $aId),($aSave | ConvertTo-Json -Depth 20),(New-Object Text.UTF8Encoding($false)))
    $bSave = Get-Content -LiteralPath (Save-Path $bId) -Raw | ConvertFrom-Json
    $bSave.Class=1; $bSave.ClassLocked=$true; $bSave.Revision++; $bSave.Inventory.Revision++
    $bSave.Inventory.Slots = @((Item 0 'equipment.rustleaf_sword'),(Item 1 'equipment.runestone_staff'),(Item 2 'equipment.willow_staff'))
    foreach ($index in 3..23) { $bSave.Inventory.Slots += (Item $index 'equipment.training_shield') }
    [IO.File]::WriteAllText((Save-Path $bId),($bSave | ConvertTo-Json -Depth 20),(New-Object Text.UTF8Encoding($false)))
    $server = Launch 'exercise-server' ''; Wait-ForLog 'exercise-server' 'Arvenok server ready'
    $a = Launch 'exercise-a' 'a' 'exercise'; $b = Launch 'exercise-b' 'b' 'exercise'
    Wait-ForLog 'exercise-a' 'ECONOMY complete'; Wait-ForLog 'exercise-b' 'ECONOMY complete'
    $states = @((State 'exercise-a'),(State 'exercise-b'))
    $dropA = (Select-String -LiteralPath (Join-Path $logs 'exercise-a.stdout.log') -Pattern '^ECONOMY drop=').Line
    $dropB = (Select-String -LiteralPath (Join-Path $logs 'exercise-b.stdout.log') -Pattern '^ECONOMY drop=').Line
    if ($dropA -ne $dropB) { throw 'Two clients disagree on authoritative drop identity/position/owner/timing.' }
    $wins = 0
    foreach ($name in @('exercise-a','exercise-b')) {
        if (Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern '^ECONOMY race=true$' -Quiet) { $wins++ }
        Wait-ForLog $name 'ECONOMY removed='
    }
    if ($wins -ne 1) { throw "Pickup race requires exactly one winner, got $wins" }
    foreach ($state in $states) {
        $read = $state | ConvertFrom-Json; $saved = Get-Content -LiteralPath (Save-Path $read.CharacterId) -Raw | ConvertFrom-Json
        if ($saved.Inventory.Revision -ne $read.Revision) { throw 'Success preceded durable save.' }
    }
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'exercise-server' 'left\. Total: 0'
    $a = Launch 'reconnect-a' 'a'; $b = Launch 'reconnect-b' 'b'
    Wait-ForLog 'reconnect-a' 'ECONOMY complete'; Wait-ForLog 'reconnect-b' 'ECONOMY complete'
    if ((State 'reconnect-a') -ne $states[0] -or (State 'reconnect-b') -ne $states[1]) { throw 'Reconnect changed item ownership.' }
    Stop-Game $a; Stop-Game $b; Stop-Game $server
    $server = Launch 'restart-server' ''; Wait-ForLog 'restart-server' 'Arvenok server ready'
    $a = Launch 'restart-a' 'a'; $b = Launch 'restart-b' 'b'
    Wait-ForLog 'restart-a' 'ECONOMY complete'; Wait-ForLog 'restart-b' 'ECONOMY complete'
    if ((State 'restart-a') -ne $states[0] -or (State 'restart-b') -ne $states[1]) { throw 'Restart changed item ownership.' }
    foreach ($log in (Get-ChildItem -LiteralPath $logs -Filter '*.log')) {
        if (Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Parse Error|ECONOMY CHECK FAILED|Unhandled exception|Failed to instantiate|RPC.*mismatch|System\..*Exception:|Error loading resource' -Quiet) { throw "Economy engine failure: $($log.FullName)" }
    }
    Write-Host 'Economy smoke passed: native recipe/equipment UI, natural monster loot, shared drop state/removal, protection/full-bag rejection, exactly one race winner, durable replay, reconnect and restart.'
    Write-Host "Logs: $logs"
} finally { foreach ($process in @($started.ToArray())) { Stop-Game $process } }
