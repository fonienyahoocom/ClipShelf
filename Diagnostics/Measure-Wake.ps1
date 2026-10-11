param([int[]]$HiddenSeconds = @(0,60,600,1800), [int]$WarmCycles = 50,
      [string]$CorePath = "$env:LOCALAPPDATA\Programs\ClipShelf\ClipShelf.exe")
$ErrorActionPreference = 'Stop'
# Windows-only, not executed in the Linux build environment. Never copies/pastes.
# Run with the default Ctrl+tilde hotkey; do not interact during this measurement.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ClipShelfWakeProbe {
 [DllImport("user32.dll")] static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
 public static void Toggle() {
  foreach(int k in new[]{16,17,18,91,92}) if((GetAsyncKeyState(k)&0x8000)!=0) throw new Exception("Release physical modifier keys.");
  keybd_event(17,0,0,UIntPtr.Zero); keybd_event(192,0,0,UIntPtr.Zero);
  keybd_event(192,0,2,UIntPtr.Zero); keybd_event(17,0,2,UIntPtr.Zero);
 }
}
'@
$Dir = Join-Path $env:LOCALAPPDATA 'ClipShelf\diagnostics'
$Csv = Join-Path $Dir 'picker-wake.csv'
New-Item -ItemType Directory -Path $Dir -Force | Out-Null
function LastWake {
 if (Test-Path $Csv) { return (Get-Content -LiteralPath $Csv -Tail 1) }
 return ''
}
function Wake([string]$Phase) {
 $before = LastWake
 [ClipShelfWakeProbe]::Toggle()
 $limit = [Diagnostics.Stopwatch]::StartNew()
 do { Start-Sleep -Milliseconds 20; $line = LastWake } while (($line -eq $before) -and ($limit.ElapsedMilliseconds -lt 10000))
 if ($line -eq $before) { throw 'No wake record: verify hotkey registration and picker visibility.' }
 $f = $line.Split(',')
 $entry = [pscustomobject]@{ Phase=$Phase;Utc=$f[0];Version=$f[1];UiPid=[int]$f[2];WakeMs=[double]::Parse($f[3],[Globalization.CultureInfo]::InvariantCulture);WorkingSetBytes=[long]$f[4];PrivateBytes=[long]$f[5] }
 Start-Sleep -Milliseconds 200
 [ClipShelfWakeProbe]::Toggle() # Hide without touching the clipboard.
 Start-Sleep -Milliseconds 200
 return $entry
}
function Resources {
 @(Get-Process ClipShelf,ClipShelf.UI -ErrorAction SilentlyContinue | ForEach-Object {
  [pscustomobject]@{Name=$_.ProcessName;Pid=$_.Id;CpuSeconds=$_.CPU;WorkingSetBytes=$_.WorkingSet64;PrivateBytes=$_.PrivateMemorySize64}
 })
}
$Rows = @()
$StartedByProbe = -not (Get-Process ClipShelf -ErrorAction SilentlyContinue)
if ($StartedByProbe) { Start-Process -FilePath $CorePath -ArgumentList '--background'; Start-Sleep -Seconds 3 }
$Rows += Wake 'first-wake-after-background-start-or-existing-process'
foreach ($seconds in $HiddenSeconds) {
 Write-Host "Hidden for $seconds seconds..."
 Start-Sleep -Seconds $seconds
 $Rows += Wake "hidden-$seconds-seconds"
}
for ($i=0;$i -lt $WarmCycles;$i++) { $Rows += Wake 'warm-repeat' }
$Before = Resources
$IdleWatch = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds 10
$After = Resources
$Idle = @($After | ForEach-Object {
 $p = $_; $b = $Before | Where-Object Pid -eq $p.Pid
 [pscustomobject]@{Name=$p.Name;Pid=$p.Pid;SameProcess=[bool]$b;WorkingSetBytes=$p.WorkingSetBytes;PrivateBytes=$p.PrivateBytes;CpuPercentOneLogicalCore= $(if($b){100*($p.CpuSeconds-$b.CpuSeconds)/$IdleWatch.Elapsed.TotalSeconds}else{$null})}
})
$Warm = @($Rows | Where-Object Phase -eq 'warm-repeat' | Sort-Object WakeMs)
$P95 = if ($Warm.Count) { $Warm[[Math]::Ceiling(.95*$Warm.Count)-1].WakeMs } else { $null }
$Report = [ordered]@{Version='2.15.2';StartedByProbe=$StartedByProbe;Scope='Core hotkey timestamp to WPF ContextIdle, includes IPC, not first-pixel paint';WarmP95Ms=$P95;Target250Ms=$(if($null -ne $P95){$P95 -lt 250}else{$null});StableUiPid=(@($Rows.UiPid | Select-Object -Unique).Count -eq 1);Idle=$Idle;Wakes=$Rows;NativePasteImeDpi='NOT_TESTED_BY_THIS_SCRIPT'}
$Path = Join-Path $Dir ('wake-report-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.json')
$Report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
Write-Host "Report: $Path"
