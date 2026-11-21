#Requires -Version 5.1
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $repo 'content/art-source/ui/arvenok-skills-v02/data/asset_manifest.json') -Raw | ConvertFrom-Json
$runtime=Join-Path $repo 'apps/game/Assets/UI/arvenok-skills-v02'
function Check-Png([string]$path,[int]$width,[int]$height) {
    $bytes=[IO.File]::ReadAllBytes($path)
    if($bytes.Length -lt 33 -or $bytes[0] -ne 137 -or $bytes[25] -ne 6) { throw "Expected RGBA PNG: $path" }
    $actualWidth=([int]$bytes[16] -shl 24) + ([int]$bytes[17] -shl 16) + ([int]$bytes[18] -shl 8) + $bytes[19]
    $actualHeight=([int]$bytes[20] -shl 24) + ([int]$bytes[21] -shl 16) + ([int]$bytes[22] -shl 8) + $bytes[23]
    if($actualWidth -ne $width -or $actualHeight -ne $height) { throw "Unexpected image dimensions: $path" }
    if(Test-Path -LiteralPath ($path+'.import')) {
        $settings=[IO.File]::ReadAllText($path+'.import')
        if($settings -notmatch '(?m)^compress/mode=0\s*$' -or $settings -notmatch '(?m)^mipmaps/generate=false\s*$') { throw "Expected lossless import without mipmaps: $path" }
    }
}
foreach($icon in $manifest.icons) { Check-Png (Join-Path $runtime $icon.path.Substring(7)) $icon.size_px[0] $icon.size_px[1] }
foreach($effect in @('lifesteal','dodge','parry','block')) { Check-Png (Join-Path $runtime "vfx/$($effect)_6frames_96.png") 576 96 }
foreach($frame in @('hotbar_slot','hotbar_slot_empty','hotbar_slot_selected','hotbar_slot_locked','cooldown_wedge_example')) { Check-Png (Join-Path $runtime "ui/$frame.png") 96 96 }
Write-Host 'Skills v0.2 assets passed: 32 icons, five hotbar images and four six-frame RGBA effects.'
