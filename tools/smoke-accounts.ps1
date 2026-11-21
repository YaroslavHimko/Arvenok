#Requires -Version 5.1
[CmdletBinding()]
param([string]$Godot='godot',[switch]$Capture)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$engine=(Get-Command $Godot -CommandType Application).Source
$logs=Join-Path $repo ('artifacts/accounts-smoke/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$null=New-Item -ItemType Directory -Path $logs -Force
$started=New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0)
$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
function Launch([string]$name,[string]$role='',[string]$mode='test'){
    $args=@('--headless','--max-fps','60','--path',('"'+(Join-Path $repo 'apps/game')+'"'),'--log-file',('"'+(Join-Path $logs "$name.engine.log")+'"'))
    if($Capture -and $name -eq 'a'){$args=$args | Where-Object {$_ -ne '--headless'};$args+=@('--position','-10000,-10000','--resolution','1000x650')}
    if($role){
        $source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/accounts-client.gd')).Replace('__ROLE__',$role).Replace('__MODE__',$mode)
        $script=Join-Path $logs "$name.gd";[IO.File]::WriteAllText($script,$source,(New-Object Text.UTF8Encoding($false)))
        $args+=@('--script',('"'+$script+'"'))
    }
    $args+=@('--','--port',$port)
    if(!$role){$args+=@('--server','--data-dir',('"'+(Join-Path $logs 'data')+'"'))}
    $p=Start-Process -WindowStyle Hidden -FilePath $engine -ArgumentList $args -WorkingDirectory $repo -RedirectStandardOutput (Join-Path $logs "$name.stdout.log") -RedirectStandardError (Join-Path $logs "$name.stderr.log") -PassThru
    $null=$p.Handle;$started.Add($p);return $p
}
function WaitLog([string]$name,[string]$pattern){
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    while([DateTime]::UtcNow -lt $deadline){
        foreach($p in $started){if($p.HasExited){throw "Process exited. Inspect $logs"}}
        if(Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern $pattern -Quiet){return}
        Start-Sleep -Milliseconds 100
    }
    throw "Timeout $name / $pattern. Inspect $logs"
}
function StopGame($p){if(!$p.HasExited){$p.Kill();$null=$p.WaitForExit(5000)};$null=$started.Remove($p);$p.Dispose()}
try{
    $server=Launch 'server';WaitLog 'server' 'Arvenok server ready'
    $tls=Start-Process -WindowStyle Hidden -FilePath $engine -WorkingDirectory $repo -ArgumentList @('--headless','--path',('"'+(Join-Path $repo 'apps/game')+'"'),'--script',('"'+(Join-Path $PSScriptRoot 'fixtures/accounts-tls.gd')+'"'),'--','--port',$port) -RedirectStandardOutput (Join-Path $logs 'tls.stdout.log') -RedirectStandardError (Join-Path $logs 'tls.stderr.log') -PassThru
    $null=$tls.Handle
    if(!$tls.WaitForExit(20000)){$tls.Kill();throw 'TLS verification timed out'}
    if($tls.ExitCode -ne 0 -or !(Select-String -LiteralPath (Join-Path $logs 'tls.stdout.log') -Pattern 'ACCOUNT TLS rejects' -Quiet)){throw "TLS verification failed. Inspect $logs"}
    $tls.Dispose()
    $a=Launch 'a' 'a';$b=Launch 'b' 'b';$duplicate=Launch 'duplicate' 'duplicate'
    WaitLog 'a' 'ACCOUNT flow passed';WaitLog 'b' 'ACCOUNT flow passed';WaitLog 'duplicate' 'ACCOUNT duplicate entry rejected'
    StopGame $a;StopGame $b;StopGame $duplicate;StopGame $server
    $server=Launch 'restart-server';WaitLog 'restart-server' 'Arvenok server ready'
    $a=Launch 'restore-a' 'a' 'restore';$b=Launch 'restore-b' 'b' 'restore'
    WaitLog 'restore-a' 'ACCOUNT restart passed';WaitLog 'restore-b' 'ACCOUNT restart passed'
    foreach($log in Get-ChildItem $logs -Filter '*.log'){
        if(Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR|CHECK FAILED|Parse Error|Unhandled exception|System\..*Exception:' -Quiet){throw "Engine error in $($log.Name)"}
    }
    Write-Host "Account smoke passed. TLS, six slots, ownership, shared world, switching, deletion, logout and restart. Logs: $logs"
}finally{foreach($p in @($started.ToArray())){StopGame $p}}
