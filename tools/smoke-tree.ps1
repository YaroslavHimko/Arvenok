#Requires -Version 5.1
[CmdletBinding()]
param([string] $Godot = 'godot', [int] $TimeoutSeconds = 60, [switch] $Capture)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$engine = (Get-Command $Godot -CommandType Application).Source
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$logs = Join-Path $repo "artifacts/tree-smoke/$runId"
$null = New-Item -ItemType Directory -Path $logs -Force
$started = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$socket = New-Object Net.Sockets.UdpClient
try { $socket.Client.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback, 0))); $port = $socket.Client.LocalEndPoint.Port }
finally { $socket.Dispose() }
function Launch([string] $name, [string] $profile, [bool] $exercise = $false, [string] $foreign = '', [string] $grant = '') {
    $arguments = @('--max-fps','60','--path',('"' + (Join-Path $repo 'apps/game') + '"'),'--log-file',('"' + (Join-Path $logs "$name.engine.log") + '"'))
    if ($Capture -and $name -eq 'a') { $arguments += @('--position','-10000,-10000','--resolution','1280x720') }
    else { $arguments += '--headless' }
    if ($profile -ne '') {
        $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/tree-client.gd')).Replace('__EXERCISE__', $exercise.ToString().ToLowerInvariant()).Replace('__ROLE__', $profile).Replace('__CAPTURE__', ($Capture -and $name -eq 'a').ToString().ToLowerInvariant()).Replace('__FOREIGN_STACK__', $foreign)
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
    return ((Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern '^TREE state=').Line -replace '^TREE state=', '')
}
function Saved([string] $id) { return Get-Content -LiteralPath (Join-Path $logs ('characters/characters/' + ([Guid]$id).ToString('N') + '.json')) -Raw | ConvertFrom-Json }
try {
    $server = Launch 'create-server' ''; Wait-ForLog 'create-server' 'Arvenok server ready'
    $a = Launch 'create-a' 'a'; $b = Launch 'create-b' 'b'
    Wait-ForLog 'create-a' 'TREE complete'; Wait-ForLog 'create-b' 'TREE complete'
    $initial = @((State 'create-a' | ConvertFrom-Json), (State 'create-b' | ConvertFrom-Json))
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'create-server' 'left\. Total: 0'; Stop-Game $server
    for ($i=0; $i -lt 2; $i++) {
        $path = Join-Path $logs ('characters/characters/' + ([Guid]$initial[$i].Inventory.CharacterId).ToString('N') + '.json')
        $c = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $c.Class = if ($i -eq 0) { 0 } else { 1 }; $c.ClassLocked=$true; $c.Level=6; $c.Experience=0; $c.SkillPoints=4; if ($i -eq 0) { $c.Ranks.Defense=2 } else { $c.Ranks.Primary=2 }; $c.Revision++
        $ids = if ($i -eq 0) { @('equipment.rustleaf_sword','equipment.training_shield','equipment.weapon.weathered_greatsword') } else { @('equipment.willow_staff') }
        $index=0; $c.Inventory.Slots = @($ids | ForEach-Object { @{Index=$index; Stack=@{Id=[Guid]::NewGuid().ToString();ItemId=$_;Quantity=1;Properties=@()}}; $index++ }); $c.Inventory.Revision++
        [IO.File]::WriteAllText($path, ($c | ConvertTo-Json -Depth 30))
    }
    $server = Launch 'server' ''; Wait-ForLog 'server' 'Arvenok server ready'
    $a = Launch 'a' 'a' $true; $b = Launch 'b' 'b' $true
    Wait-ForLog 'a' 'TREE complete'; Wait-ForLog 'b' 'TREE complete'
    $states = @((State 'a'),(State 'b'))
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'server' 'left\. Total: 0'; Stop-Game $server
    $server = Launch 'restart-server' ''; Wait-ForLog 'restart-server' 'Arvenok server ready'
    $a = Launch 'restore-a' 'a'; $b = Launch 'restore-b' 'b'
    Wait-ForLog 'restore-a' 'TREE complete'; Wait-ForLog 'restore-b' 'TREE complete'
    if ((State 'restore-a') -ne $states[0] -or (State 'restore-b') -ne $states[1]) { throw 'Tree or ownership changed after restart.' }
    foreach ($log in Get-ChildItem -LiteralPath $logs -Filter '*.log') {
        if (Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Parse Error|CHECK FAILED|Unhandled exception|Failed to instantiate|System\..*Exception:|Error loading resource' -Quiet) { throw "Weapon engine failure: $($log.FullName)" }
    }
    Write-Host 'Tree smoke passed: two clients, native passive learning, exact point spending, active scroll gate, and cooldown/learning persistence across restart.'
    Write-Host "Logs: $logs"
} finally { foreach ($process in @($started.ToArray())) { Stop-Game $process } }