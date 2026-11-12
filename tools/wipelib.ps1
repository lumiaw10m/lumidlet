. (Join-Path $PSScriptRoot 'phone.ps1')
function Run-Retry([string]$cmd, [int]$wait) {
    for ($i = 0; $i -lt 6; $i++) {
        $o = try { Tx-Run $cmd $wait 2>$null } catch { $null }
        if ($o -and $o -notmatch 'no output marker') { return $o }
        Start-Sleep 3
    }
    'gave up: ' + $cmd
}
Run-Retry 'kill.exe Lumidlet.exe' 5 | Out-Null
Run-Retry ('rmdir /s /q "' + $script:Ls + '\apps"') 30
Run-Retry ('rmdir /s /q "' + $script:Ls + '\inbox"') 10
Run-Retry ('dir /b "' + $script:Ls + '"') 5
