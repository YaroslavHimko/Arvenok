#Requires -Version 5.1
[CmdletBinding()]
param([string]$Godot='godot', [ValidateSet('warrior','mage','archer')][string]$Scenario='warrior', [int]$TimeoutSeconds=120, [switch]$Capture)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$engine=(Get-Command $Godot -CommandType Application).Source
$runId=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)
$logs=Join-Path $repo "artifacts/skills-v02-smoke/$runId-$Scenario"
$null=New-Item -ItemType Directory -Path $logs -Force
$started=New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$socket=New-Object Net.Sockets.UdpClient
try { $socket.Client.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback,0))); $port=$socket.Client.LocalEndPoint.Port } finally { $socket.Dispose() }
function Launch([string]$name,[string]$profile,[string]$mode='create') {
    $arguments=@('--max-fps','60','--path',('"'+(Join-Path $repo 'apps/game')+'"'),'--log-file',('"'+(Join-Path $logs "$name.engine.log")+'"'))
    $captureThis=$Capture -and $name -eq 'a'
    if ($captureThis) { $arguments+=@('--position','-10000,-10000','--resolution','1280x720') } else { $arguments+='--headless' }
    if ($profile -ne '') {
        $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/skills-v02-client.gd')).Replace('__ROLE__',$profile).Replace('__MODE__',$mode).Replace('__SCENARIO__',$Scenario).Replace('__CAPTURE__',$captureThis.ToString().ToLowerInvariant())
        $script=Join-Path $logs "$name.gd"; [IO.File]::WriteAllText($script,$source,(New-Object Text.UTF8Encoding($false)))
        $arguments+=@('--script',('"'+$script+'"'))
    }
    $arguments+=@('--','--port',$port)
    if ($profile -eq '') { $arguments+=@('--server','--data-dir',('"'+(Join-Path $logs 'characters')+'"')) }
    else { $arguments+=@('--profile',"$profile-$runId-$Scenario",'--smoke-role','inventory') }
    $parameters=@{FilePath=$engine; ArgumentList=$arguments; WorkingDirectory=$repo; PassThru=$true; RedirectStandardOutput=(Join-Path $logs "$name.stdout.log"); RedirectStandardError=(Join-Path $logs "$name.stderr.log")}
    if ($env:OS -eq 'Windows_NT') { $parameters.WindowStyle='Hidden' }
    $process=Start-Process @parameters; $null=$process.Handle; $started.Add($process); return $process
}
function Stop-Game($process) { if(-not $process.HasExited) { $process.Kill(); $null=$process.WaitForExit(5000) }; $null=$started.Remove($process); $process.Dispose() }
function Wait-ForLog([string]$name,[string]$pattern) {
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        foreach($p in $started) { if($p.HasExited) { throw "Process exited early; inspect $logs" } }
        $path=Join-Path $logs "$name.stdout.log"
        if ((Test-Path -LiteralPath $path) -and (Select-String -LiteralPath $path -Pattern $pattern -Quiet)) { return }
        Start-Sleep -Milliseconds 100
    }
    throw "Timeout $name / $pattern; inspect $logs"
}
function Read-Line([string]$name,[string]$prefix) { return ((Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern ('^'+$prefix)).Line -replace ('^'+$prefix),'') }
function Save-Path([string]$id) { return Join-Path $logs ('characters/characters/'+([Guid]$id).ToString('N')+'.json') }
try {
    $server=Launch 'create-server' ''; Wait-ForLog 'create-server' 'Arvenok server ready'
    $a=Launch 'create-a' 'a'; $b=Launch 'create-b' 'b'
    Wait-ForLog 'create-a' 'SKILLS complete'; Wait-ForLog 'create-b' 'SKILLS complete'
    $initial=@((Read-Line 'create-a' 'SKILLS state=' | ConvertFrom-Json),(Read-Line 'create-b' 'SKILLS state=' | ConvertFrom-Json))
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'create-server' 'left\. Total: 0'; Stop-Game $server
    $classA=@{warrior=0; mage=1; archer=2}[$Scenario]; $classB=@{warrior=2; mage=0; archer=1}[$Scenario]
    $scrolls=@{warrior=@('scroll.skill.warrior.perfect_block','scroll.skill.warrior.sunder_strike'); mage=@('scroll.skill.mage.flame_lance','scroll.skill.mage.arcane_shell'); archer=@('scroll.skill.archer.piercing_shot','scroll.skill.archer.evasive_cut')}[$Scenario]
    for($i=0;$i -lt 2;$i++) {
        $path=Save-Path $initial[$i].Inventory.CharacterId; $c=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $c.Class=if($i -eq 0){$classA}else{$classB}; $c.ClassLocked=$true; $c.Level=10; $c.Experience=0; $c.Revision++; $c.Inventory.Revision++
        if($i -eq 0) {
            $c.Ranks.Primary=2; if($Scenario -eq 'archer'){$c.Ranks.Mobility=2}else{$c.Ranks.Defense=2}; $c.SkillPoints=7
            $ids=@{warrior=@('equipment.rustleaf_sword','equipment.training_shield','equipment.weapon.weathered_greatsword'); mage=@('equipment.willow_staff'); archer=@('equipment.elm_shortbow','equipment.weapon.hunters_dagger')}[$Scenario]
            $ids+=@($scrolls[0],$scrolls[0],$scrolls[1],$scrolls[1],$(if($Scenario -eq 'mage'){'scroll.skill.warrior.perfect_block'}else{'scroll.skill.mage.flame_lance'}),'recipe.ironbark_sword')
        } else {
            $c.Ranks.Primary=4; $c.Ranks.Mobility=4; $c.Ranks.Defense=4; $c.SkillPoints=0
            $ids=@(@{warrior='equipment.elm_shortbow';mage='equipment.rustleaf_sword';archer='equipment.willow_staff'}[$Scenario])
        }
        $index=0; $c.Inventory.Slots=@($ids | ForEach-Object { @{Index=$index;Stack=@{Id=[Guid]::NewGuid().ToString();ItemId=$_;Quantity=1;Properties=@()}}; $index++ })
        [IO.File]::WriteAllText($path,($c | ConvertTo-Json -Depth 30),(New-Object Text.UTF8Encoding($false)))
    }
    $server=Launch 'server' ''; Wait-ForLog 'server' 'Arvenok server ready'
    $a=Launch 'a' 'a' 'exercise'; $b=Launch 'b' 'b' 'exercise'
    Wait-ForLog 'a' 'SKILLS complete'; Wait-ForLog 'b' 'SKILLS complete'
    $states=@((Read-Line 'a' 'SKILLS state='),(Read-Line 'b' 'SKILLS state='))
    if((Read-Line 'a' 'SKILLS events=') -ne (Read-Line 'b' 'SKILLS events=')) { throw 'Clients disagree on authoritative combat outcomes.' }
    if((Read-Line 'a' 'SKILLS hp=') -ne (Read-Line 'b' 'SKILLS hp=')) { throw 'Clients disagree on final health.' }
    $effects=@{warrior=@('Parried','PerfectBlocked','Healed'); mage=@('Absorbed'); archer=@('Dodged')}[$Scenario]
    foreach($effect in $effects) { foreach($name in @('a','b')) { Wait-ForLog $name "VISUAL defense.*effect=$effect" } }
    foreach($value in $states) { $read=$value | ConvertFrom-Json; $saved=Get-Content -LiteralPath (Save-Path $read.Inventory.CharacterId) -Raw | ConvertFrom-Json; if($saved.Revision -ne $read.Progress.Revision -or $saved.Inventory.Revision -ne $read.Inventory.Revision) { throw 'Client success preceded durable aggregate.' } }
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'server' 'left\. Total: 0'
    $a=Launch 'reconnect-a' 'a' 'restore'; $b=Launch 'reconnect-b' 'b' 'restore'
    Wait-ForLog 'reconnect-a' 'SKILLS complete'; Wait-ForLog 'reconnect-b' 'SKILLS complete'
    if((Read-Line 'reconnect-a' 'SKILLS state=') -ne $states[0] -or (Read-Line 'reconnect-b' 'SKILLS state=') -ne $states[1]) { throw 'Reconnect changed skills or inventory.' }
    Stop-Game $a; Stop-Game $b; Stop-Game $server
    $server=Launch 'restart-server' ''; Wait-ForLog 'restart-server' 'Arvenok server ready'
    $a=Launch 'restart-a' 'a' 'restore'; $b=Launch 'restart-b' 'b' 'restore'
    Wait-ForLog 'restart-a' 'SKILLS complete'; Wait-ForLog 'restart-b' 'SKILLS complete'
    if((Read-Line 'restart-a' 'SKILLS state=') -ne $states[0] -or (Read-Line 'restart-b' 'SKILLS state=') -ne $states[1]) { throw 'Restart changed skills or inventory.' }
    foreach($log in Get-ChildItem -LiteralPath $logs -Filter '*.log') {
        if(Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Parse Error|CHECK FAILED|Unhandled exception|Failed to instantiate|System\..*Exception:|Error loading resource' -Quiet) { throw "Skills engine failure: $($log.FullName)" }
    }
    Write-Host "Skills v0.2 $Scenario passed: native scroll learning, forged/replayed intents, shared combat/effects, equipment, reconnect and restart."
    Write-Host "Logs: $logs"
} finally { foreach($p in @($started.ToArray())) { Stop-Game $p } }
