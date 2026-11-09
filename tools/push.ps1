param([string]$Dir)
. (Join-Path $PSScriptRoot 'phone.ps1')
$inbox = "$($script:Ls)\inbox"
Tx-Run "mkdir `"$inbox`"" 5 | Out-Null
foreach ($j in Get-ChildItem $Dir -Filter *.ja?) { Tx-Upload $j.FullName $j.Name $inbox }
Tx-Run "dir /b `"$inbox`"" 5
