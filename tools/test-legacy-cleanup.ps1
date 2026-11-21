#Requires -Version 5.1
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$relative='.local/server/cleanup-test-'+[Guid]::NewGuid().ToString('N')
$root=Join-Path $repo $relative
$null=New-Item -ItemType Directory -Path (Join-Path $root 'characters') -Force
$null=New-Item -ItemType Directory -Path (Join-Path $root 'credentials') -Force
$legacy=[Guid]::NewGuid().ToString()
$owned=[Guid]::NewGuid().ToString()
foreach($id in @($legacy,$owned)){
    $record=@{Id=$id;SchemaVersion=4;Level=1;Inventory=@{Slots=@()};Equipment=@{};Tree=@{Nodes=@()}}
    if($id -eq $owned){$record.OwnerAccountId=[Guid]::NewGuid().ToString()}
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root "characters/$id.json")
    ConvertTo-Json $id | Set-Content -LiteralPath (Join-Path $root "credentials/$id.json")
}
$before=@(Get-ChildItem (Join-Path $repo '.local/backups') -Directory).FullName
& (Join-Path $PSScriptRoot 'legacy-characters.ps1') -DataDirectory $relative -Backup
$backups=@(Get-ChildItem (Join-Path $repo '.local/backups') -Directory | Where-Object {$before -notcontains $_.FullName})
if($backups.Count -ne 1){throw 'Expected one new backup.'}
$backup=$backups[0]
$manifest=Join-Path $backup.FullName 'dry-run.json'
$rows=@(Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json)
if($rows.Count -ne 1 -or $rows[0].Id -ne $legacy){throw 'Dry-run included an owned character.'}
& (Join-Path $PSScriptRoot 'legacy-characters.ps1') -DataDirectory $relative -Quarantine -Manifest $manifest
& (Join-Path $PSScriptRoot 'legacy-characters.ps1') -DataDirectory $relative -Quarantine -Manifest $manifest
foreach($folder in @('characters','credentials')){
    if(Test-Path -LiteralPath (Join-Path $root "$folder/$legacy.json")){throw 'Legacy record remains.'}
    if(!(Test-Path -LiteralPath (Join-Path $root "$folder/$owned.json"))){throw 'Owned record was removed.'}
    if(!(Test-Path -LiteralPath (Join-Path $backup.FullName "$folder/$legacy.json"))){throw 'Backup missing.'}
}
Write-Host 'Cleanup checks passed. Preview selected only ownerless records, backup retained, owned data preserved and second run moved zero records.'
