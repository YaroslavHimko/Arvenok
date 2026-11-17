#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$catalog = Get-Content -LiteralPath (Join-Path $repo 'catalog/components.json') -Raw | ConvertFrom-Json
if ($catalog.schemaVersion -ne 1) { throw 'Unsupported component catalog version.' }
$byId = @{}
$byProject = @{}

function Resolve-RepositoryPath([string] $path) {
    $resolved = [IO.Path]::GetFullPath((Join-Path $repo $path))
    if (-not $resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes the repository: $path"
    }
    return $resolved
}

foreach ($component in $catalog.components) {
    if (-not $component.id -or $byId.ContainsKey($component.id)) { throw "Duplicate or missing component ID: $($component.id)" }
    if (-not $component.owner) { throw "Missing owner: $($component.id)" }
    if ($component.status -notin @('implemented', 'reserved', 'design-only')) { throw "Invalid component status: $($component.id)" }
    if (-not (Test-Path -LiteralPath (Resolve-RepositoryPath $component.path))) { throw "Missing component path: $($component.path)" }
    $byId[$component.id] = $component
    if ($component.project) {
        $project = Resolve-RepositoryPath $component.project
        if (-not (Test-Path -LiteralPath $project)) { throw "Missing project: $project" }
        if ($byProject.ContainsKey($project)) { throw "Duplicate catalog project: $project" }
        $byProject[$project] = $component
    }
}

$solution = Get-Content -LiteralPath (Join-Path $repo 'Arvenok.sln') -Raw
foreach ($project in $byProject.Keys) {
    $component = $byProject[$project]
    [xml] $xml = Get-Content -LiteralPath $project -Raw
    $actual = @()
    foreach ($reference in $xml.SelectNodes('//ProjectReference')) {
        $target = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $project) $reference.Include))
        if (-not $byProject.ContainsKey($target)) { throw "Unregistered dependency in $project`: $target" }
        $dependency = $byProject[$target].id
        if ($dependency -notin $component.dependencies) { throw "Forbidden dependency: $($component.id) -> $dependency" }
        $actual += $dependency
    }
    foreach ($dependency in $component.dependencies) {
        if (-not $byId.ContainsKey($dependency) -or $dependency -notin $actual) { throw "Stale dependency in catalog: $($component.id) -> $dependency" }
    }
    $solutionPath = $component.project.Replace('/', '\')
    if (-not $solution.Contains('"' + $solutionPath + '"')) { throw "Project missing from solution: $($component.project)" }
    if ($component.path.StartsWith('packages/')) {
        if ($xml.Project.Sdk -ne 'Microsoft.NET.Sdk') { throw "Engine SDK is forbidden in package $($component.id)" }
        if ($xml.SelectNodes('//PackageReference[contains(@Include,"Godot")]').Count -gt 0) { throw "Godot dependency in package $($component.id)" }
        $source = Get-ChildItem -LiteralPath (Join-Path $repo $component.path) -Recurse -Filter '*.cs' -File |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
        if ($source | Select-String -Pattern '\bGodot\b|\bArvenok\.Game\b' -Quiet) {
            # Inspect code lines only; documentation may describe the adapter boundary.
            foreach ($file in $source) {
                foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
                    if ($line -notmatch '^\s*//' -and $line -match '\bGodot\b|\bArvenok\.Game\b') { throw "Engine coupling in $($file.FullName)" }
                }
            }
        }
    }
}

# No dependency cycles, including cycles introduced through otherwise valid catalog edits.
$visiting = @{}
$visited = @{}
function Visit-Component([string] $id) {
    if ($visiting[$id]) { throw "Dependency cycle through $id" }
    if ($visited[$id]) { return }
    $visiting[$id] = $true
    foreach ($dependency in $byId[$id].dependencies) { Visit-Component $dependency }
    $visiting[$id] = $false
    $visited[$id] = $true
}
foreach ($id in $byId.Keys) { Visit-Component $id }

foreach ($root in @('apps', 'packages', 'tests', 'services')) {
    foreach ($project in (Get-ChildItem -LiteralPath (Join-Path $repo $root) -Recurse -Filter '*.csproj' -File)) {
        if ($project.FullName -match '[\\/](obj|bin|\.godot)[\\/]') { continue }
        if (-not $byProject.ContainsKey($project.FullName)) { throw "Register new project in catalog: $($project.FullName)" }
    }
}
$presentation = Get-ChildItem -LiteralPath (Join-Path $repo 'apps/game/Features') -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -match '[\\/]Presentation[\\/]' }
if ($presentation | Select-String -Pattern 'Arvenok\.Simulation|\bArenaSimulation\b' -Quiet) { throw 'Presentation must consume snapshots, not authoritative simulation.' }
Write-Host "Architecture checks passed ($($byProject.Count) projects, $($byId.Count) components)."
