param([Parameter(Mandatory=$true)][string]$NsisDirectory, [Parameter(Mandatory=$true)][string]$SevenZip)
$ErrorActionPreference = 'Stop'
# Upstream NSIS 3.11 Windows distribution omits amd64 stubs/plugins.
# Debian's matching 3.11-1 package supplies them; SHA comes from Debian trixie Packages.
$url = 'https://deb.debian.org/debian/pool/main/n/nsis/nsis-common_3.11-1_all.deb'
$expected = '103a3284c1a5356efa0aba90fdfc391c3cde8e7df8726377cef0f98471584047'
$temp = Join-Path $env:RUNNER_TEMP 'clipshelf-nsis-amd64'
New-Item -ItemType Directory -Path $temp -Force | Out-Null
$deb = Join-Path $temp 'nsis-common_3.11-1_all.deb'
Invoke-WebRequest -Uri $url -OutFile $deb
if ((Get-FileHash $deb -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'NSIS dependency SHA256 mismatch' }
& $SevenZip x $deb -tAr "-o$temp" -y
if ($LASTEXITCODE -ne 0) { throw 'NSIS deb extraction failed' }
& $SevenZip x (Join-Path $temp 'data.tar.xz') "-o$temp" -y
if ($LASTEXITCODE -ne 0) { throw 'NSIS xz extraction failed' }
& $SevenZip x (Join-Path $temp 'data.tar') "-o$temp" -y
if ($LASTEXITCODE -ne 0) { throw 'NSIS tar extraction failed' }
$data = Join-Path $temp 'usr\share\nsis'
Copy-Item (Join-Path $data 'Stubs\*-amd64-unicode') (Join-Path $NsisDirectory 'Stubs') -Force
$plugins = Join-Path $NsisDirectory 'Plugins\amd64-unicode'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
Copy-Item (Join-Path $data 'Plugins\amd64-unicode\*.dll') $plugins -Force
foreach ($name in @('Stubs\zlib-amd64-unicode','Stubs\lzmasolid-amd64-unicode','Plugins\amd64-unicode\System.dll','Plugins\amd64-unicode\nsDialogs.dll')) {
    if (!(Test-Path (Join-Path $NsisDirectory $name))) { throw "Missing NSIS amd64 dependency: $name" }
}
Write-Output "Verified NSIS 3.11 amd64 dependencies: $expected"
