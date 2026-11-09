param([string]$Name)
. (Join-Path $PSScriptRoot 'phone.ps1')
$out = Tx-Run ('dir /b "' + $script:Ls + '\apps"') 5
foreach ($id in ($out -split "`n" | Where-Object { $_ -match '^[0-9a-f]{12}$' })) {
    $meta = Tx-Run ('type "' + $script:Ls + '\apps\' + $id + '\meta.txt"') 5
    if ($meta -match "name=.*$Name") { Tx-Run ('type "' + $script:Ls + '\apps\' + $id + '\log.txt"') 8 }
}
