#Requires -Version 5.1
[CmdletBinding()]
param([string] $Godot = 'godot', [int] $TimeoutSeconds = 60, [switch] $Capture)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$engine = (Get-Command $Godot -CommandType Application).Source
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$logs = Join-Path $repo "artifacts/effects-smoke/$runId"
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
        $source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/effects-client.gd')).Replace('__ROLE__', $profile).Replace('__CAPTURE__', ($Capture -and $name -eq 'items-a').ToString().ToLowerInvariant()).Replace('__FOREIGN_STACK__', $foreign)
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
    return ((Select-String -LiteralPath (Join-Path $logs "$name.stdout.log") -Pattern '^EFFECTS state=').Line -replace '^EFFECTS state=', '')
}
function Saved([string] $id) { return Get-Content -LiteralPath (Join-Path $logs ('characters/characters/' + ([Guid]$id).ToString('N') + '.json')) -Raw | ConvertFrom-Json }
try {
    $server = Launch 'server' ''; Wait-ForLog 'server' 'Arvenok server ready'
    $a = Launch 'a' 'a'; $b = Launch 'b' 'b'
    Wait-ForLog 'a' 'EFFECTS complete'; Wait-ForLog 'b' 'EFFECTS complete'
    if ((State 'a') -ne (State 'b')) { throw 'Clients received different defense outcomes.' }
    foreach ($name in @('a','b')) { Wait-ForLog $name 'VISUAL defense.*effect=Dodged' }
    foreach ($log in Get-ChildItem -LiteralPath $logs -Filter '*.log') {
        if (Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Parse Error|CHECK FAILED|Unhandled exception|Failed to instantiate|System\..*Exception:|Error loading resource' -Quiet) { throw "Effect engine failure: $($log.FullName)" }
    }
    Write-Host 'Effects smoke passed: identical authoritative dodge, consumed charge, retained mitigation and event VFX in two clients.'
    Write-Host "Logs: $logs"
} finally { foreach ($process in @($started.ToArray())) { Stop-Game $process } }