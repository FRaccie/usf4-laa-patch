# Sets the Large Address Aware flag (0x20) on a 32-bit PE exe. Windows PowerShell 5.1, no installs.
param(
  [Parameter(Mandatory)][string]$Path,
  [switch]$InPlace, [switch]$Check, [switch]$Restore
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
public static class PeSum {
  // Standard PE checksum: 16-bit word sum with carry fold, checksum field skipped, plus file length.
  public static uint Compute(byte[] b, int skipOff) {
    ulong sum = 0;
    for (int i = 0; i + 1 < b.Length + 1; i += 2) {
      if (i == skipOff || i == skipOff + 2) continue;
      uint w = i + 1 < b.Length ? (uint)(b[i] | (b[i + 1] << 8)) : (uint)b[i];
      sum += w;
      if (sum > 0xFFFFFFFF) sum = (sum & 0xFFFFFFFF) + (sum >> 32);
    }
    sum = (sum & 0xFFFF) + (sum >> 16);
    sum = (sum & 0xFFFF) + (sum >> 16);
    return (uint)(sum + (ulong)b.Length);
  }
}
'@

function Read-Pe([string]$p) {
  $fs = [IO.File]::Open($p, 'Open', 'Read', 'None')   # exclusive: fails if the file is in use
  try { $b = New-Object byte[] $fs.Length; [void]$fs.Read($b, 0, $b.Length) } finally { $fs.Dispose() }
  if ($b.Length -lt 0x40 -or $b[0] -ne 0x4D -or $b[1] -ne 0x5A) { throw 'Not an MZ file.' }
  $pe = [BitConverter]::ToInt32($b, 0x3C)
  if ($pe -lt 0x40 -or $pe + 0x58 -gt $b.Length) { throw 'e_lfanew out of range.' }
  if ([BitConverter]::ToUInt32($b, $pe) -ne 0x00004550) { throw 'PE signature missing.' }
  if ([BitConverter]::ToUInt16($b, $pe + 4) -ne 0x14C) { throw 'Not an i386 (0x14C) image.' }
  if ([BitConverter]::ToUInt16($b, $pe + 24) -ne 0x10B) { throw 'Not PE32 (optional header magic 0x10B).' }
  [pscustomobject]@{ Bytes = $b; Chr = $pe + 22; Sum = $pe + 24 + 64 }
}
function Write-Pe([string]$p, [byte[]]$b) {
  $fs = [IO.File]::Open($p, 'Create', 'Write', 'None')
  try { $fs.Write($b, 0, $b.Length) } finally { $fs.Dispose() }
}
function Show($pe, $tag) {
  $c = [BitConverter]::ToUInt16($pe.Bytes, $pe.Chr); $s = [BitConverter]::ToUInt32($pe.Bytes, $pe.Sum)
  '{0}: Characteristics=0x{1:X} LAA={2} CheckSum=0x{3:X8}' -f $tag, $c, [bool]($c -band 0x20), $s
}

$Path = (Resolve-Path -LiteralPath $Path).Path
$bak = $Path + '.bak'

if ($Restore) {
  if (-not (Test-Path -LiteralPath $bak)) { throw "No backup: $bak" }
  [IO.File]::WriteAllBytes($Path, [IO.File]::ReadAllBytes($bak))   # fails if the exe is in use
  "Restored $Path from $bak"; return
}

$pe = Read-Pe $Path
if ($Check) { Show $pe 'Current'; return }
Show $pe 'Before'
$c = [BitConverter]::ToUInt16($pe.Bytes, $pe.Chr)
if ($c -band 0x20) { 'Already Large Address Aware; nothing to do.'; return }

[BitConverter]::GetBytes([uint16]($c -bor 0x20)).CopyTo($pe.Bytes, $pe.Chr)
[BitConverter]::GetBytes([PeSum]::Compute($pe.Bytes, $pe.Sum)).CopyTo($pe.Bytes, $pe.Sum)
Show $pe 'After '

if ($InPlace) {
  if (Test-Path -LiteralPath $bak) {
    $a = [IO.File]::ReadAllBytes($bak); $o = [IO.File]::ReadAllBytes($Path)
    if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$a, [byte[]]$o)) { throw "$bak exists and differs from the current file; refusing." }
  } else { [IO.File]::Copy($Path, $bak) }
  Write-Pe $Path $pe.Bytes; "Patched in place; backup: $bak"
} else {
  $out = [IO.Path]::ChangeExtension($Path, '.laa.exe')
  Write-Pe $out $pe.Bytes; "Wrote $out"
}
