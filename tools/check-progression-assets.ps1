#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $repo 'apps/game/Assets/UI/woodvale-progression'
$manifest = Get-Content -LiteralPath (Join-Path $runtime 'asset_manifest.json') -Raw | ConvertFrom-Json
if ($manifest.assets.Count -ne 7) { throw 'Expected seven progression decorations.' }
foreach ($asset in $manifest.assets) {
    $path = [IO.Path]::GetFullPath((Join-Path $runtime $asset.file))
    if (-not $path.StartsWith($runtime + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Asset escapes collection.' }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 26 -or [BitConverter]::ToString($bytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A' -or $bytes[25] -ne 6) { throw "Expected RGBA PNG: $path" }
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    if ($width -ne $asset.width -or $height -ne $asset.height) { throw "Progression image dimensions mismatch: $path" }
    if (Test-Path -LiteralPath ($path + '.import')) {
        $settings = [IO.File]::ReadAllText($path + '.import')
        if ($settings -notmatch '(?m)^compress/mode=0\s*$' -or $settings -notmatch '(?m)^mipmaps/generate=false\s*$') { throw "Use lossless import without mipmaps: $path" }
    }
}
Write-Host 'Progression assets passed (7 RGBA images, manifest sizes and import settings).'
