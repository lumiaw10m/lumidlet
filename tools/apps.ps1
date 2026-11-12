. (Join-Path $PSScriptRoot 'phone.ps1')
$ids = (Tx-Run ('dir /b "' + $script:Ls + '\apps"') 5) -split "`n" | Where-Object { $_ -match '^[0-9a-f]{12}$' }
foreach ($id in $ids) {
    $m = Tx-Run ('type "' + $script:Ls + '\apps\' + $id + '\meta.txt"') 5
    $n = ($m -split "`n" | Where-Object { $_ -match '^(name|width|height|padlayout)=' }) -join ' '
    "$id $n"
}
