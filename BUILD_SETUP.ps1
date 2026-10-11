$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Payload = Join-Path $Root 'ClipShelf_READY'
if (-not (Test-Path (Join-Path $Payload 'ClipShelf.exe'))) { throw 'Run BUILD_EXE.cmd first.' }
$Nsis = Get-Command makensis.exe -ErrorAction SilentlyContinue
if ($Nsis) { $Compiler = $Nsis.Source } else { $Compiler = Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe' }
if (-not (Test-Path $Compiler)) { throw 'Install NSIS 3.x first.' }
$Output = Join-Path $Root 'ClipShelf-2.15.2-Setup.exe'
$Lines = @('# Generated from the exact published payload.')
Get-ChildItem -LiteralPath $Payload -Recurse -File | ForEach-Object {
  $Relative = $_.FullName.Substring($Payload.Length + 1).Replace('$', '$$')
  $Lines += 'Delete "$INSTDIR\' + $Relative + '"'
}
Get-ChildItem -LiteralPath $Payload -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
  $Relative = $_.FullName.Substring($Payload.Length + 1).Replace('$', '$$')
  $Lines += 'RMDir "$INSTDIR\' + $Relative + '"'
}
[IO.File]::WriteAllLines((Join-Path $Root 'installer\uninstall-files.nsh'), $Lines, [Text.UTF8Encoding]::new($false))
& $Compiler "/DPAYLOAD_DIR=$Payload" "/DOUTPUT_FILE=$Output" (Join-Path $Root 'installer\ClipShelf.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Write-Host "Installer ready: $Output"
