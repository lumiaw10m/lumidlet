param([string]$Folder = 'j2me')
. (Join-Path $PSScriptRoot 'phone.ps1')
for ($i = 0; $i -lt 10; $i++) {
    $o = try { Tx-Run ("mkdir C:\Data\Users\Public\Documents\" + $Folder) 5 2>$null } catch { $null }
    if ($o -and $o -notmatch 'no output marker') { break }
    Start-Sleep 3
}
Add-Type -AssemblyName System.Net.Http
$h = New-Object Net.Http.HttpClient
$h.Timeout = [TimeSpan]::FromMinutes(5)
$dl = Join-Path $env:USERPROFILE 'Downloads'
$files = @(Get-ChildItem (Join-Path $dl 'J2ME Roms') -Recurse -Include *.jar, *.jad) + @(Get-ChildItem $dl -Filter *.jar)
$path = [Uri]::EscapeDataString("\" + $Folder)
$have = @{}
$list = $h.GetStringAsync("http://$($script:TxPhone)/api/filesystem/apps/files?knownfolderid=PublicDocuments&path=$path").Result | ConvertFrom-Json
foreach ($it in $list.Items) { $have[$it.Name] = [long]$it.FileSize }
$todo = @($files | Where-Object { -not ($have.ContainsKey($_.Name) -and $have[$_.Name] -eq $_.Length) })
"total $($files.Count), already there $($files.Count - $todo.Count), to copy $($todo.Count)"
$ok = 0; $fail = @()
foreach ($f in $todo) {
    $done = $false
    for ($try = 0; $try -lt 6 -and -not $done; $try++) {
        try {
            $mp = New-Object Net.Http.MultipartFormDataContent
            $bc = New-Object Net.Http.ByteArrayContent(, [IO.File]::ReadAllBytes($f.FullName))
            $bc.Headers.TryAddWithoutValidation('Content-Disposition', "form-data; name=`"file`"; filename=`"$($f.Name)`"") | Out-Null
            $mp.Add($bc)
            $code = [int]$h.PostAsync("http://$($script:TxPhone)/api/filesystem/apps/file?knownfolderid=PublicDocuments&path=$path", $mp).Result.StatusCode
            if ($code -eq 200) { $done = $true; $ok++ } else { Start-Sleep 2 }
        } catch { Start-Sleep 3 }
    }
    if (-not $done) { $fail += $f.Name }
    if ($done -and $ok % 50 -eq 0) { "$ok / $($todo.Count)" }
}
"copied $ok of $($todo.Count)"
if ($fail.Count) { "failed ($($fail.Count)): " + ($fail -join '; ') }
