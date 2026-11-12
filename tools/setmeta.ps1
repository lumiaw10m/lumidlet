param([string]$Name, [string]$Set)
. (Join-Path $PSScriptRoot 'phone.ps1')
Tx-Run 'kill.exe Lumidlet.exe' 5 | Out-Null
$ids = (Tx-Run ('dir /b "' + $script:Ls + '\apps"') 5) -split "`n" | Where-Object { $_ -match '^[0-9a-f]{12}$' }
foreach ($id in $ids) {
    $path = $script:Ls + '\apps\' + $id + '\meta.txt'
    $lines = (Tx-Run ('type "' + $path + '"') 5) -split "`n" | Where-Object { $_ -match '^[a-z]+=' }
    if (-not ($lines | Where-Object { $_ -match "^name=.*$Name" })) { continue }
    foreach ($kv in ($Set -split ',')) {
        $k = $kv.Split('=')[0]
        $lines = @($lines | Where-Object { $_ -notmatch "^$k=" }) + $kv
    }
    $tmp = Join-Path $env:TEMP 'meta.txt'
    [IO.File]::WriteAllText($tmp, (($lines | ForEach-Object { $_.Trim() }) -join "`n") + "`n")
    Tx-Upload $tmp 'meta.txt' ($script:Ls + '\apps\' + $id)
    "$id updated: $Set"
}
