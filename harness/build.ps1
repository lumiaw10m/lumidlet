$root = Split-Path $PSScriptRoot
$csc = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\Roslyn\csc.exe'
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$src = @(Get-ChildItem "$root\core" -Recurse -Filter *.cs | ForEach-Object FullName) + @(Get-ChildItem $PSScriptRoot -Filter *.cs | ForEach-Object FullName)
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
& $csc /nologo /langversion:7.3 /optimize+ /unsafe /platform:x64 /out:"$out\harness.exe" /r:"$fw\System.IO.Compression.dll" /r:"$fw\System.Drawing.dll" /r:"$fw\System.Net.Http.dll" /r:System.Numerics.dll /nowarn:1998 $src
exit $LASTEXITCODE
