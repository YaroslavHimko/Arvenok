#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch] $UpdateLocks,
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    & (Join-Path $PSScriptRoot 'check-architecture.ps1')
    & (Join-Path $PSScriptRoot 'check-woodvale-assets.ps1')
    & (Join-Path $PSScriptRoot 'check-combat-animations.ps1')
    & (Join-Path $PSScriptRoot 'check-progression-assets.ps1')
    & (Join-Path $PSScriptRoot 'check-item-assets.ps1')
    & (Join-Path $PSScriptRoot 'check-skills-v02-assets.ps1')
    $restoreArguments = @('restore', 'Arvenok.sln', "-p:Configuration=$Configuration")
    if ($UpdateLocks) { $restoreArguments += '--force-evaluate' }
    else { $restoreArguments += '--locked-mode' }
    & dotnet @restoreArguments
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & dotnet build Arvenok.sln --configuration $Configuration --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & dotnet run --project tests/Arvenok.Tests --configuration $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
}
finally { Pop-Location }
