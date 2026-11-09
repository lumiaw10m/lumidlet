. (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'Luminal\engine\phone.ps1')
if (-not $script:TxPhone) { throw 'set LUMIA_IP to the phone address' }
$script:Pkg = 'Lumidlet_h5t9p3hk10bwg'
$script:Ls = "U:\Users\DefApps\APPDATA\Local\Packages\$($script:Pkg)\LocalState"
$script:ShotDir = Join-Path (Split-Path $PSScriptRoot) 'shots'

function L-Launch([string]$Arg = '') {
    Tx-Run 'kill.exe Lumidlet.exe' 5 | Out-Null
    Tx-Run "TestNavigationApi.exe LaunchModernApplication $($script:Pkg)!App App $Arg" 8
}

function L-Shot([string]$Name) {
    New-Item -ItemType Directory -Force $script:ShotDir | Out-Null
    Tx-Run "ScreenCapture.exe C:\Data\Users\Public\Documents\$Name.png" 8 | Out-Null
    $dest = Join-Path $script:ShotDir "$Name.png"
    Invoke-WebRequest "http://$($script:TxPhone)/api/filesystem/apps/file?knownfolderid=PublicDocuments&filename=$Name.png" -OutFile $dest -UseBasicParsing
    Tx-Run "del C:\Data\Users\Public\Documents\$Name.png" 5 | Out-Null
    $dest
}
