$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$laa = Join-Path $here 'laa-patch.ps1'
Add-Type -TypeDefinition 'public static class T { public static uint Sum(byte[] b,int skip){ulong s=0;for(int i=0;i<b.Length;i+=2){if(i==skip||i==skip+2)continue;s+=i+1<b.Length?(uint)(b[i]|(b[i+1]<<8)):(uint)b[i];s=(s&0xFFFF)+(s>>16);}s=(s&0xFFFF)+(s>>16);return (uint)(s+(ulong)b.Length);} }'
function Assert($ok, $m) { if (-not $ok) { throw "FAIL: $m" }; "ok: $m" }
function PeOff($b) { [BitConverter]::ToInt32($b, 0x3C) }

# Synthetic PE32: 0x200 bytes, header at 0x80, odd-ish content
$d = Join-Path $env:TEMP ('laa-test-' + [guid]::NewGuid()); [void](New-Item -ItemType Directory $d)
$b = New-Object byte[] 0x201; (New-Object Random 7).NextBytes($b)
$b[0]=0x4D;$b[1]=0x5A; [BitConverter]::GetBytes(0x80).CopyTo($b,0x3C)
[BitConverter]::GetBytes([uint32]0x4550).CopyTo($b,0x80)
[BitConverter]::GetBytes([uint16]0x14C).CopyTo($b,0x84)
[BitConverter]::GetBytes([uint16]0x103).CopyTo($b,0x80+22)
[BitConverter]::GetBytes([uint16]0x10B).CopyTo($b,0x80+24)
$exe = Join-Path $d 'a.exe'; [IO.File]::WriteAllBytes($exe, $b)

& $laa -Path $exe | Out-Null
$p = [IO.File]::ReadAllBytes((Join-Path $d 'a.laa.exe')); $o = PeOff $p
Assert (([BitConverter]::ToUInt16($p,$o+22) -band 0x20) -ne 0) 'flag set on copy'
Assert ([BitConverter]::ToUInt32($p,$o+88) -eq [T]::Sum($p,$o+88)) 'checksum matches recompute'
Assert ([IO.File]::ReadAllBytes($exe)[$o+22] -eq 3) 'original untouched'

& $laa -Path $exe -InPlace | Out-Null
$h1 = (Get-FileHash $exe).Hash
$r = & $laa -Path $exe -InPlace
Assert ($r -match 'Already') 'second run is a no-op'
Assert ((Get-FileHash $exe).Hash -eq $h1) 'file unchanged by second run'
& $laa -Path $exe -Restore | Out-Null
Assert ([IO.File]::ReadAllBytes($exe).Length -eq $b.Length -and (Get-FileHash $exe).Hash -eq (Get-FileHash -InputStream ([IO.MemoryStream]::new($b))).Hash) 'restore exact'

$bad = Join-Path $d 'bad.exe'; [IO.File]::WriteAllBytes($bad, (New-Object byte[] 100))
$threw = $false; try { & $laa -Path $bad } catch { $threw = $true }
Assert $threw 'non-PE refused'
Remove-Item $d -Recurse -Force

# Read-only comparison with the real game
$g = $env:SF4E_GAME_DIR
if (-not $g) { $g = 'C:\Program Files (x86)\Steam\steamapps\common\Super Street Fighter IV - Arcade Edition' }
$src = Join-Path $g 'SSFIV.exe'; $ref = Join-Path $g 'SSFIV.ember-laa.exe'
if ((Test-Path $src) -and (Test-Path $ref)) {
  $t = Join-Path $env:TEMP 'laa-ssfiv.exe'; Copy-Item $src $t -Force
  & $laa -Path $t | Out-Null
  $x = [IO.File]::ReadAllBytes((Join-Path $env:TEMP 'laa-ssfiv.laa.exe')); $y = [IO.File]::ReadAllBytes($ref)
  if ($x.Length -ne $y.Length) { "SSFIV: length differs $($x.Length) vs $($y.Length)" }
  else {
    $diff = 0..($x.Length-1) | Where-Object { $x[$_] -ne $y[$_] }
    $o = PeOff $x; $names = $diff | ForEach-Object { if ($_ -ge $o+88 -and $_ -lt $o+92) {'CheckSum'} elseif ($_ -ge $o+22 -and $_ -lt $o+24) {'Characteristics'} else {"0x{0:X}" -f $_} } | Select-Object -Unique
    "SSFIV vs known-good: $($diff.Count) differing bytes: $($names -join ', ')"
    "ours checksum=0x{0:X8} known-good=0x{1:X8}" -f [BitConverter]::ToUInt32($x,$o+88), [BitConverter]::ToUInt32($y,$o+88)
  }
  Remove-Item $t, (Join-Path $env:TEMP 'laa-ssfiv.laa.exe') -Force
} else { 'SSFIV comparison skipped (files not found)' }
'ALL PASS'
