param([switch]$Install, [string]$Phone = $env:LUMIA_IP)
$app = $PSScriptRoot
$mf = Join-Path $app 'Package.appxmanifest'
$m = [IO.File]::ReadAllText($mf)
$m = [regex]::Replace($m, '(<Identity [^>]*Version=")(\d+)\.(\d+)\.(\d+)\.0"', { param($x) $x.Groups[1].Value + $x.Groups[2].Value + '.' + $x.Groups[3].Value + '.' + ([int]$x.Groups[4].Value + 1) + '.0"' })
[IO.File]::WriteAllText($mf, $m)
$root = Split-Path $app
$bat = @"
@echo off
subst L: /d >nul 2>&1
subst L: "$root" || exit /b 1
cd /d L:\app
"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" Lumidlet.csproj /nologo /v:m /restore /p:Configuration=Release /p:Platform=ARM /p:AppxPackageDir=L:\app\out\ /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never
set RC=%ERRORLEVEL%
cd /d "$env:TEMP"
subst L: /d
exit /b %RC%
"@
$f = Join-Path $env:TEMP 'lumidlet-build.bat'
Set-Content -Path $f -Value $bat -Encoding ascii
cmd /c $f
$rc = $LASTEXITCODE
Remove-Item $f
if ($rc) { "BUILD FAILED $rc"; exit $rc }
$dir = Get-ChildItem "$app\out" -Directory | Sort-Object LastWriteTime | Select-Object -Last 1
"built: $($dir.FullName)"
if (-not $Install) { return }
if (-not $Phone) { "set LUMIA_IP or pass -Phone"; exit 1 }
$appx = Get-ChildItem $dir.FullName -Filter *.appx | Select-Object -First 1
Add-Type -AssemblyName System.Net.Http
$h = New-Object Net.Http.HttpClient
$h.Timeout = [TimeSpan]::FromMinutes(5)
$mp = New-Object Net.Http.MultipartFormDataContent
$files = @($appx) + @(Get-ChildItem "$($dir.FullName)\Dependencies\ARM" -Filter *.appx -ErrorAction SilentlyContinue) + @(Get-ChildItem $app -Filter *.cer)
foreach ($x in $files) {
    $bc = New-Object Net.Http.ByteArrayContent(, [IO.File]::ReadAllBytes($x.FullName))
    $bc.Headers.TryAddWithoutValidation('Content-Disposition', "form-data; name=`"$($x.Name)`"; filename=`"$($x.Name)`"") | Out-Null
    $mp.Add($bc)
}
$r = $h.PostAsync("http://$Phone/api/app/packagemanager/package?package=$($appx.Name)", $mp).Result
"upload: $([int]$r.StatusCode)"
for ($i = 0; $i -lt 90; $i++) {
    try { $s = $h.GetAsync("http://$Phone/api/app/packagemanager/state").Result } catch { Start-Sleep -Seconds 2; continue }
    if ($s -and [int]$s.StatusCode -ne 204) { "install state $([int]$s.StatusCode): $($s.Content.ReadAsStringAsync().Result)"; break }
    Start-Sleep -Seconds 2
}
