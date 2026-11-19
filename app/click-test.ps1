# Drives LaaPatch.exe via UI Automation on a temp COPY of SSFIV.exe. Never touches the Steam folder.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$ErrorActionPreference = 'Stop'
$g = 'C:\Program Files (x86)\Steam\steamapps\common\Super Street Fighter IV - Arcade Edition'
$d = Join-Path $env:TEMP ('laa-click-' + [guid]::NewGuid()); [void](New-Item -ItemType Directory $d)
$exe = Join-Path $d 'SSFIV.exe'; Copy-Item (Join-Path $g 'SSFIV.exe') $exe
$h0 = (Get-FileHash $exe).Hash
$app = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'LaaPatch.exe'
$p = Start-Process $app "`"$exe`"" -PassThru
$AE = [Windows.Automation.AutomationElement]; $PC = [Windows.Automation.AutomationElement]::AutomationIdProperty
$win = $null; for ($i=0; $i -lt 100 -and -not $win; $i++) { Start-Sleep -Milliseconds 100; $p.Refresh(); if ($p.MainWindowHandle -ne 0) { $win = $AE::FromHandle($p.MainWindowHandle) } }
Add-Type 'using System;using System.Runtime.InteropServices;public class M{[DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);}'
# WinForms controls show up as Panes named by their text; match by text prefix.
$names = @{ patch='Patch SSFIV.exe'; restore='Restore original'; copy='Patch a copy'; status='Large Address Aware*'; detail='32-bit PE32*' }
function El($id) { $win.FindAll('Descendants',[Windows.Automation.Condition]::TrueCondition) | ? { $_.Current.Name -like ($names[$id] + '*') } | select -First 1 }
function Status { Start-Sleep -Milliseconds 400; (El 'status').Current.Name + ' | ' + ((El 'detail').Current.Name -replace "`r?`n",' / ') }
function Click($id) { [void][M]::SendMessage([IntPtr](El $id).Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep -Milliseconds 800 }
function Assert($ok, $m) { if (-not $ok) { $p.Kill(); throw "FAIL: $m" }; "ok: $m" }
try {
  Start-Sleep 1
  $s0 = Status; "[$s0]"; Assert ($s0 -match 'OFF') 'starts OFF'
  Click 'patch'; $s = Status; $s
  Assert ($s -match 'ON' -and $s -match 'Ember will now start the 4 GB') 'patch -> ON + message'
  Assert (-not (El 'patch').Current.IsEnabled) 'Patch disabled when already ON (second patch is a no-op)'
  Assert ((El 'restore').Current.IsEnabled) 'Restore enabled'
  Assert ((Get-FileHash ($exe+'.bak')).Hash -eq $h0) 'bak equals original'
  Assert ((Get-FileHash $exe).Hash -ne $h0) 'exe changed'
  # screenshot of the window only
  Add-Type -AssemblyName System.Drawing
  Add-Type 'using System;using System.Runtime.InteropServices;public class W2{[StructLayout(LayoutKind.Sequential)]public struct R{public int l,t,r,b;}[DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R r);[DllImport("user32.dll")]public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint f);}'
  $r = New-Object W2+R; [void][W2]::GetWindowRect($p.MainWindowHandle,[ref]$r)
  $bmp = New-Object Drawing.Bitmap ($r.r-$r.l),($r.b-$r.t); $gr = [Drawing.Graphics]::FromImage($bmp); $dc = $gr.GetHdc(); [void][W2]::PrintWindow($p.MainWindowHandle,$dc,2); $gr.ReleaseHdc($dc)
  $bmp.Save((Join-Path (Split-Path -Parent $app) 'screenshot-patched.png'))
  Click 'restore'; $s = Status; $s
  Assert ($s -match 'OFF') 'restore -> OFF'
  Assert ((Get-FileHash $exe).Hash -eq $h0) 'bytes identical after restore'
  Assert ($s -match 'Original restored; Ember starts the 2 GB version again') 'restore message'
  Click 'patch'; Assert ((Status) -match 'ON') 'patch again works'
  Assert (-not (El 'patch').Current.IsEnabled) 'no-op: Patch disabled'
  # Steam updated the game meanwhile: exe is a new unpatched build, old .bak must not overwrite it
  $nb2 = [IO.File]::ReadAllBytes($exe + '.bak'); $nb2[$nb2.Length-1] = $nb2[$nb2.Length-1] -bxor 0xFF
  [IO.File]::WriteAllBytes($exe, $nb2); $hn = (Get-FileHash $exe).Hash
  Click 'restore'; $s = Status; $s
  Assert ($s -match 'already unpatched; backup left as is') 'updated game: message'
  Assert ((Get-FileHash $exe).Hash -eq $hn) 'updated exe NOT overwritten by older backup'
  'ALL PASS'
} finally { if (-not $p.HasExited) { $p.Kill() }; Remove-Item $d -Recurse -Force }
