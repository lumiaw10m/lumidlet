param([string]$Phone = $env:LUMIA_IP)
if (-not $Phone) { throw 'set LUMIA_IP to the phone address' }
$app = Join-Path (Split-Path $PSScriptRoot) 'app'
$dir = Get-ChildItem "$app\out" -Directory | Sort-Object LastWriteTime | Select-Object -Last 1
$appx = Get-ChildItem $dir.FullName -Filter *.appx | Select-Object -First 1
Add-Type -AssemblyName System.Net.Http
$h = New-Object Net.Http.HttpClient
$h.Timeout = [TimeSpan]::FromMinutes(5)
$mp = New-Object Net.Http.MultipartFormDataContent
$files = @($appx) + @(Get-ChildItem "$($dir.FullName)\Dependencies\ARM" -Filter *.appx) + @(Get-ChildItem $app -Filter *.cer)
foreach ($x in $files) {
    $bc = New-Object Net.Http.ByteArrayContent(, [IO.File]::ReadAllBytes($x.FullName))
    $bc.Headers.TryAddWithoutValidation('Content-Disposition', "form-data; name=`"$($x.Name)`"; filename=`"$($x.Name)`"") | Out-Null
    $mp.Add($bc)
}
try { $r = $h.PostAsync("http://$Phone/api/app/packagemanager/package?package=$($appx.Name)", $mp).Result } catch { "upload error: " + $_.Exception.InnerException; exit 1 }
"upload $($appx.Name): $([int]$r.StatusCode)"
if ([int]$r.StatusCode -ne 202) { exit 1 }
Start-Sleep 3
for ($i = 0; $i -lt 90; $i++) {
    try { $s = $h.GetAsync("http://$Phone/api/app/packagemanager/state").Result } catch { Start-Sleep -Seconds 2; continue }
    if ($s -and [int]$s.StatusCode -ne 204) { "install state $([int]$s.StatusCode): $($s.Content.ReadAsStringAsync().Result)"; break }
    Start-Sleep -Seconds 2
}
