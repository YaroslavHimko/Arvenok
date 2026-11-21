#Requires -Version 5.1
[CmdletBinding()]
param([string]$DataDirectory='.local/server/world-001', [switch]$Backup, [switch]$Quarantine, [string]$Manifest='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$root=[IO.Path]::GetFullPath((Join-Path $repo $DataDirectory))
$allowed=[IO.Path]::GetFullPath((Join-Path $repo '.local/server'))+[IO.Path]::DirectorySeparatorChar
if(!$root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'This development cleanup only accepts .local/server directories.'}
$lock=New-Object IO.FileStream((Join-Path $root 'server.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    $rows=@(Get-ChildItem (Join-Path $root 'characters') -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
        $c=Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        if(!$c.OwnerAccountId -or $c.OwnerAccountId -eq [Guid]::Empty.ToString()){
            [pscustomobject]@{Id=$c.Id;File=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash;Level=$c.Level;InventoryStacks=@($c.Inventory.Slots).Count;Equipment=@($c.Equipment.Weapon,$c.Equipment.Body,$c.Equipment.OffHand | Where-Object {$_}).Count;LearnedSkills=@($c.Tree.Nodes | Where-Object {$_}).Count;Progression=1;Position=[int]($null -ne $c.WorldPosition);RecipeStacks=@($c.Inventory.Slots | Where-Object {$_.Stack.ItemId -like 'recipe.*'}).Count}
        }
    })
    $rows | Format-Table Id,Level,InventoryStacks,Equipment,LearnedSkills,RecipeStacks
    Write-Host "Legacy characters eligible: $($rows.Count)"
    if($Backup){
        $destination=Join-Path $repo ('.local/backups/legacy-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
        $null=New-Item -ItemType Directory -Path $destination
        foreach($folder in @('characters','credentials')){if(Test-Path (Join-Path $root $folder)){Copy-Item -LiteralPath (Join-Path $root $folder) -Destination $destination -Recurse}}
        $rows | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'dry-run.json') -Encoding UTF8
        Write-Host "Backup and dry-run manifest: $destination"
    }
    if($Quarantine){
        if(!$Manifest){throw 'Supply an approved backup dry-run.json with -Manifest.'}
        $approved=@(Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json)
        $target=Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($Manifest))) 'quarantine'
        $backupRoot=[IO.Path]::GetFullPath((Join-Path $repo '.local/backups'))+[IO.Path]::DirectorySeparatorChar
        if(!$target.StartsWith($backupRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest must be in the repository backup directory.'}
        foreach($row in $approved){
            $current=@($rows | Where-Object {$_.Id -eq $row.Id})
            if(!$current.Count){continue}
            if($current[0].Sha256 -ne $row.Sha256){throw 'Legacy data changed since the approved preview.'}
            $backupFile=Join-Path (Split-Path -Parent $Manifest) ('characters/'+$current[0].File)
            if((Get-FileHash -LiteralPath $backupFile -Algorithm SHA256).Hash -ne $row.Sha256){throw 'Verified backup required.'}
        }
        $null=New-Item -ItemType Directory -Path (Join-Path $target 'characters') -Force
        $null=New-Item -ItemType Directory -Path (Join-Path $target 'credentials') -Force
        $moved=0
        foreach($row in $approved){
            $current=@($rows | Where-Object {$_.Id -eq $row.Id})
            if(!$current.Count){continue}
            Move-Item -LiteralPath (Join-Path $root ('characters/'+$current[0].File)) -Destination (Join-Path $target 'characters')
            $moved++
        }
        foreach($credential in Get-ChildItem (Join-Path $root 'credentials') -Filter '*.json'){
            $id=Get-Content -LiteralPath $credential.FullName -Raw | ConvertFrom-Json
            if($approved.Id -contains $id){Move-Item -LiteralPath $credential.FullName -Destination (Join-Path $target 'credentials')}
        }
        Write-Host "Quarantined $moved legacy character aggregates and their credential indexes."
    }
} finally {$lock.Dispose()}
