#Requires -Version 5.1
[CmdletBinding()]
param([string] $Godot = 'godot')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$engine = (Get-Command $Godot -CommandType Application).Source
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$output = Join-Path $repo "artifacts/combat-captures/$runId"
$null = New-Item -ItemType Directory -Path $output -Force
$socket = New-Object Net.Sockets.UdpClient
try {
    $socket.Client.Bind((New-Object Net.IPEndPoint([Net.IPAddress]::Loopback, 0)))
    $port = $socket.Client.LocalEndPoint.Port
} finally { $socket.Dispose() }
$started = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
function Launch([string] $name, [string[]] $arguments) {
    $parameters = @{
        FilePath = $engine
        ArgumentList = @('--path', ('"' + (Join-Path $repo 'apps/game') + '"'), '--max-fps', '60', '--log-file', ('"' + (Join-Path $output "$name.log") + '"')) + $arguments
        PassThru = $true
    }
    if ($env:OS -eq 'Windows_NT') { $parameters.WindowStyle = 'Hidden' }
    $process = Start-Process @parameters
    $started.Add($process)
    return $process
}
try {
    $server = Launch 'server' @('--headless', '--', '--server', '--port', $port, '--data-dir', ('"' + (Join-Path $output 'characters') + '"'))
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if ($server.HasExited) { throw 'Capture server exited.' }
        if ((Test-Path (Join-Path $output 'server.log')) -and (Select-String -LiteralPath (Join-Path $output 'server.log') -Pattern 'Arvenok server ready' -Quiet)) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    $template = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'fixtures/capture-combat.gd'))
    foreach ($role in 0..2) {
        $script = Join-Path $output "capture-$role.gd"
        [IO.File]::WriteAllText($script, $template.Replace('__ROLE__', [string]$role), (New-Object Text.UTF8Encoding($false)))
        $arguments = @('--script', ('"' + $script + '"'))
        if ($role -eq 0) { $arguments += @('--position', '-10000,-10000', '--resolution', '1000x650') }
        else { $arguments += '--headless' }
        $arguments += @('--', '--port', $port, '--profile', "capture-$role-$runId")
        $null = Launch "client-$role" $arguments
    }
    foreach ($client in @($started | Select-Object -Skip 1)) {
        if (-not $client.WaitForExit(45000)) { throw "Capture client timed out; inspect $output" }
        if ($client.ExitCode -ne 0) { throw "Capture client failed; inspect $output" }
    }
    foreach ($log in (Get-ChildItem -LiteralPath $output -Filter '*.log')) {
        if (Select-String -LiteralPath $log.FullName -Pattern 'SCRIPT ERROR:|Parse Error|Assertion failed|System\..*Exception:' -Quiet) { throw "Capture failure: $($log.FullName)" }
    }
    foreach ($expected in @('mage-casting','warrior-attack','defensive-auras','movement-trails','effects-mage-firebolt','effects-mage-blink','effects-archer-power_shot','effects-archer-disrupting_arrow','monster-death','player-death')) {
        if (-not (Test-Path -LiteralPath (Join-Path $output "$expected.png"))) { throw "Missing capture $expected; inspect $output" }
    }
    Write-Host "Three-client combat captures passed: $output"
} finally {
    foreach ($process in $started) {
        if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(5000) }
        $process.Dispose()
    }
}
