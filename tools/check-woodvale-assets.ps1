#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $repo 'content/art-source/art2d/woodvale/manifest.json') -Raw | ConvertFrom-Json
$runtime = Join-Path $repo 'apps/game/Assets/Art2D/woodvale'
$assets = New-Object 'System.Collections.Generic.List[object]'
$assets.Add(@{ Path = $manifest.terrain_atlas; Width = $manifest.terrain_columns * $manifest.tile_size_px; Height = $manifest.terrain_rows * $manifest.tile_size_px; Alpha = $false })
foreach ($character in $manifest.characters.frames.PSObject.Properties) {
    foreach ($direction in $manifest.characters.directions) {
        $assets.Add(@{ Path = $character.Value.$direction; Width = $manifest.characters.frame_size_px[0]; Height = $manifest.characters.frame_size_px[1]; Alpha = $true })
    }
}
foreach ($collection in @($manifest.props, $manifest.monsters)) {
    foreach ($asset in $collection.PSObject.Properties) {
        $assets.Add(@{ Path = $asset.Value.file; Width = $asset.Value.image_size[0]; Height = $asset.Value.image_size[1]; Alpha = $true })
    }
}
foreach ($asset in $assets) {
    $path = [IO.Path]::GetFullPath((Join-Path $runtime $asset.Path))
    if (-not $path.StartsWith($runtime + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Asset path escapes its collection.' }
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Woodvale PNG: $($asset.Path)" }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 26 -or [BitConverter]::ToString($bytes[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A') { throw "Invalid PNG: $path" }
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    if ($width -ne $asset.Width -or $height -ne $asset.Height) { throw "Manifest/PNG size mismatch: $path" }
    if ($asset.Alpha -and $bytes[25] -ne 6) { throw "Expected the supplied RGBA transparency: $path" }
    if (Test-Path -LiteralPath ($path + '.import')) {
        $settings = [IO.File]::ReadAllText($path + '.import')
        if ($settings -notmatch '(?m)^compress/mode=0\s*$' -or $settings -notmatch '(?m)^mipmaps/generate=false\s*$') {
            throw "Woodvale imports must be lossless with no mipmaps: $path"
        }
    }
}
Write-Host "Woodvale asset checks passed ($($assets.Count) PNGs; manifest dimensions and transparency)."
