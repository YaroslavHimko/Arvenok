#Requires -Version 5.1
[CmdletBinding()]
param([string]$Godot='godot', [int]$TimeoutSeconds=120)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$engine=(Get-Command $Godot -CommandType Application).Source
$runId=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)
$logs=Join-Path $repo "artifacts/skills-disconnect-smoke/$runId"
$null=New-Item -ItemType Directory -Path $logs -Force
$started=New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$socket=New-Object Net.Sockets.UdpClient
try { $socket.Client.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback,0))); $port=$socket.Client.LocalEndPoint.Port } finally { $socket.Dispose() }
function Launch([string]$name,[string]$profile,[string]$mode='create') {
    $arguments=@('--max-fps','60','--path',('"'+(Join-Path $repo 'apps/game')+'"'),'--log-file',('"'+(Join-Path $logs "$name.engine.log")+'"'))
    $arguments+='--headless'
    if ($profile -ne '') {
        $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/skills-disconnect.gd')).Replace('__ROLE__',$profile).Replace('__MODE__',$mode)
        $script=Join-Path $logs "$name.gd"; [IO.File]::WriteAllText($script,$source,(New-Object Text.UTF8Encoding($false)))
        $arguments+=@('--script',('"'+$script+'"'))
    }
    $arguments+=@('--','--port',$port)
    if ($profile -eq '') { $arguments+=@('--server','--data-dir',('"'+(Join-Path $logs 'characters')+'"')) }
    else { $arguments+=@('--profile',"$profile-$runId",'--smoke-role','inventory') }
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
    Wait-ForLog 'create-a' 'DISCONNECT complete'; Wait-ForLog 'create-b' 'DISCONNECT complete'
    $paths=@((Save-Path (Read-Line 'create-a' 'CHARACTER ')), (Save-Path (Read-Line 'create-b' 'CHARACTER ')))
    Stop-Game $a; Stop-Game $b; Wait-ForLog 'create-server' 'left\. Total: 0'; Stop-Game $server
    for($i=0;$i -lt 2;$i++) {
        $c=Get-Content -LiteralPath $paths[$i] -Raw | ConvertFrom-Json
        $c.Class=if($i -eq 0){1}else{0}; $c.ClassLocked=$true; $c.Level=6; $c.SkillPoints=5; $c.Revision++; $c.Inventory.Revision++
        if($i -eq 0){$c.Ranks.Primary=2; $c.Tree.Nodes=@(@{Id='mage.ember.embers'; Rank=1}); $c.SkillPoints=3; $ids=@('scroll.skill.mage.flame_lance','scroll.skill.mage.flame_lance')}
        else {$ids=@('equipment.weapon.forged_greatsword')}
        $index=0; $c.Inventory.Slots=@($ids | ForEach-Object { @{Index=$index;Stack=@{Id=[Guid]::NewGuid().ToString();ItemId=$_;Quantity=1;Properties=@()}}; $index++ })
        [IO.File]::WriteAllText($paths[$i],($c | ConvertTo-Json -Depth 30),(New-Object Text.UTF8Encoding($false)))
    }
    $server=Launch 'server' ''; Wait-ForLog 'server' 'Arvenok server ready'
    $a=Launch 'submit-a' 'a' 'submit'; $b=Launch 'submit-b' 'b' 'submit'
    Wait-ForLog 'submit-a' 'SUBMITTED'; Wait-ForLog 'submit-b' 'SUBMITTED'
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    foreach($path in $paths){
        while((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).Inventory.Revision -ne 3){if([DateTime]::UtcNow -gt $deadline){throw 'Server did not durably commit'}; Start-Sleep -Milliseconds 50}
    }
    Stop-Game $a; Stop-Game $b; Stop-Game $server
    $server=Launch 'restart-server' ''; Wait-ForLog 'restart-server' 'Arvenok server ready'
    $a=Launch 'restore-a' 'a' 'restore'; $b=Launch 'restore-b' 'b' 'restore'
    Wait-ForLog 'restore-a' 'DURABLE replay passed'; Wait-ForLog 'restore-b' 'DURABLE replay passed'
    foreach($log in Get-ChildItem -LiteralPath $logs -Filter '*.log') { if(Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|CHECK FAILED|Parse Error|System\..*Exception:' -Quiet){throw "Engine failure: $($log.FullName)"} }
    Write-Host 'Disconnect smoke passed. Both clients lost their acknowledgements. Restart and replay preserved one learned skill, one remaining scroll and one equipped weapon.'
    Write-Host "Logs: $logs"
} finally { foreach($p in @($started.ToArray())) { Stop-Game $p } }
