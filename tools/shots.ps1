param([string]$Arg, [string]$Name, [string]$Times)
. (Join-Path $PSScriptRoot 'phone.ps1')
Tx-Run 'kill.exe Lumidlet.exe' 5 | Out-Null
Tx-Run "TestNavigationApi.exe LaunchModernApplication $($script:Pkg)!App App $Arg" 8 | Out-Null
$start = Get-Date
foreach ($t in ($Times -split ',')) {
    $wait = [double]$t - ((Get-Date) - $start).TotalSeconds
    if ($wait -gt 0) { Start-Sleep -Milliseconds ([int]($wait * 1000)) }
    L-Shot ("{0}_{1:000}" -f $Name, [int]$t)
}
