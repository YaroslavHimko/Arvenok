#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $repo 'apps/game/Assets/Art2D/woodvale-combat'
$manifest = Get-Content -LiteralPath (Join-Path $runtime 'animation_manifest.json') -Raw | ConvertFrom-Json
$clips = New-Object 'System.Collections.Generic.List[object]'
function Find-Clips($node) {
    if ($node.file) { $clips.Add($node); return }
    foreach ($property in $node.PSObject.Properties) { Find-Clips $property.Value }
}
Find-Clips $manifest.characters
Find-Clips $manifest.monsters
Find-Clips $manifest.effects
foreach ($clip in $clips) {
    $path = [IO.Path]::GetFullPath((Join-Path $runtime $clip.file))
    if (-not $path.StartsWith($runtime + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Animation escapes collection.' }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 26 -or [BitConverter]::ToString($bytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A' -or $bytes[25] -ne 6) { throw "Expected RGBA PNG: $path" }
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    if ($width -ne $manifest.cell_px[0] * $clip.frames -or $height -ne $manifest.cell_px[1]) { throw "Animation strip size mismatch: $path" }
    if (Test-Path -LiteralPath ($path + '.import')) {
        $settings = [IO.File]::ReadAllText($path + '.import')
        if ($settings -notmatch '(?m)^compress/mode=0\s*$' -or $settings -notmatch '(?m)^mipmaps/generate=false\s*$') { throw "Animation import must be lossless without mipmaps: $path" }
    }
}
Write-Host "Combat animation assets passed ($($clips.Count) RGBA strips, manifest sizes and import settings)."
