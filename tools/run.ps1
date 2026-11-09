param([string]$Arg = '', [int]$Wait = 6, [string]$Shot = 'shot')
. (Join-Path $PSScriptRoot 'phone.ps1')
L-Launch $Arg | Out-Null
Start-Sleep $Wait
L-Shot $Shot
