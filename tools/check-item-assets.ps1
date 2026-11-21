#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$catalog = Get-Content -LiteralPath (Join-Path $repo 'content/items/catalog-v1.json') -Raw | ConvertFrom-Json
$runtime = Join-Path $repo 'apps/game/Assets/UI/items'
foreach ($item in $catalog.items) {
    if ($item.icon -notmatch '^[a-z][a-z0-9_-]{0,39}$') { throw 'Invalid inventory icon key.' }
    $extension = if ($item.iconFormat) { $item.iconFormat } else { 'svg' }
    if ($extension -notin @('svg','png')) { throw 'Unsupported item icon format.' }
    $path = Join-Path $runtime ($item.icon + '.' + $extension)
    if ($extension -eq 'svg') {
        [xml] $icon = Get-Content -LiteralPath $path -Raw
        if ($icon.svg.width -ne '32' -or $icon.svg.height -ne '32') { throw "Expected a 32x32 placeholder icon: $path" }
    } else {
        $bytes = [IO.File]::ReadAllBytes($path)
        if ($bytes.Length -lt 33 -or $bytes[0] -ne 137 -or $bytes[25] -ne 6) { throw "Expected transparent RGBA PNG: $path" }
    }
    if (Test-Path -LiteralPath ($path + '.import')) {
        $settings = [IO.File]::ReadAllText($path + '.import')
        if ($settings -notmatch '(?m)^compress/mode=0\s*$' -or $settings -notmatch '(?m)^mipmaps/generate=false\s*$') { throw "Expected lossless item icon import: $path" }
    }
}
$bag = Join-Path $runtime 'loot_bag.png'
$bagBytes = [IO.File]::ReadAllBytes($bag)
if ($bagBytes.Length -lt 33 -or $bagBytes[0] -ne 137 -or $bagBytes[25] -ne 6) { throw 'Missing or invalid RGBA ground-loot bag.' }
if (Test-Path -LiteralPath ($bag + '.import')) {
    $settings = [IO.File]::ReadAllText($bag + '.import')
    if ($settings -notmatch '(?m)^compress/mode=0\s*$' -or $settings -notmatch '(?m)^mipmaps/generate=false\s*$') { throw 'Ground-loot bag requires lossless import without mipmaps.' }
}
Write-Host "Inventory asset checks passed ($($catalog.items.Count) catalog icon references)."
