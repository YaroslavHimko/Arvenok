#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('client', 'server')] [string] $Mode = 'client',
    [string] $Godot = 'godot',
    [string[]] $SessionArguments = @()
)
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'apps/game'
$arguments = @('--path', $project)
if ($Mode -eq 'server') { $arguments += '--headless' }
$arguments += '--'
if ($Mode -eq 'server') { $arguments += '--server' }
$arguments += $SessionArguments
& $Godot @arguments
if ($LASTEXITCODE -ne 0) { throw "Godot exited with code $LASTEXITCODE" }
