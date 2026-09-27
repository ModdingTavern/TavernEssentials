[CmdletBinding()]
param(
    [switch]$Install
)
$ErrorActionPreference = 'Stop'

$game = 'C:\Games\Alta\A Township Tale'
$mgd  = Join-Path $game 'A Township Tale_Data\Managed'
$ml   = Join-Path $game 'MelonLoader\net35'
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not (Test-Path $csc)) { throw "csc not found: $csc" }
if (-not (Test-Path $mgd)) { throw "Managed dir not found: $mgd" }
if (-not (Test-Path $ml))  { throw "MelonLoader net35 dir not found: $ml" }

$out = Join-Path $here 'TavernEssentials.dll'

$refs = @()
Get-ChildItem (Join-Path $mgd '*.dll') | ForEach-Object { $refs += "/r:$($_.FullName)" }
$refs += "/r:$(Join-Path $ml 'MelonLoader.dll')"
$refs += "/r:$(Join-Path $ml '0Harmony.dll')"

$src = @(Join-Path $here 'Main.cs')
foreach ($dir in @('Shared', 'Social', 'Board', 'Backend', 'Guard', 'Names', 'Postbox', 'ModSettings', 'Discord')) {
    $src += @(Get-ChildItem (Join-Path $here $dir) -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
}

$json = @($src | Where-Object { -not $_.StartsWith((Join-Path $here 'Board') + '\') } |
    Where-Object { Select-String -Path $_ -Pattern 'Newtonsoft' -SimpleMatch -Quiet })
if ($json.Count -gt 0) { throw ("Newtonsoft outside Board\: " + (($json | ForEach-Object { $_.Substring($here.Length + 1) }) -join ', ')) }

$cscArgs = @('/target:library','/nostdlib+','/noconfig','/unsafe-','/warn:4','/warnaserror+','/nologo',
             "/out:$out") + $refs + $src

Write-Host "Compiling TavernEssentials.dll ($($refs.Count) refs, $($src.Count) sources)..." -ForegroundColor Cyan
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }

$banned = @('TavernLib', 'TavernCore', 'TavernSocial', 'TavernBoard', 'VelarisClient')
try {
    $bytes = [System.IO.File]::ReadAllBytes($out)
    $asm = [System.Reflection.Assembly]::ReflectionOnlyLoad($bytes)
    $names = @($asm.GetReferencedAssemblies() | ForEach-Object { $_.Name })
} catch {
    throw "could not read the built assembly's references, so the reference check did not run: $_"
}
if ($names.Count -eq 0) { throw "the built assembly reported no references at all, so the check did not really run" }
$bad = @($names | Where-Object { $n = $_; @($banned | Where-Object { $n -like "$_*" }).Count -gt 0 })
if ($bad.Count -gt 0) { throw ("TavernEssentials must not reference: " + ($bad -join ', ')) }
Write-Host "check OK  references: $($names -join ', ')" -ForegroundColor DarkGray

$size = (Get-Item $out).Length
$sha  = (Get-FileHash $out -Algorithm SHA256).Hash.ToLower()
Write-Host "OK  $out  $size B" -ForegroundColor Green
Write-Host "    sha256 $sha"

if ($Install) {
    $mods = Join-Path $game 'Mods'
    if (-not (Test-Path $mods)) { throw "Mods dir not found: $mods" }
    Copy-Item $out (Join-Path $mods 'TavernEssentials.dll') -Force
    Write-Host "    installed -> $(Join-Path $mods 'TavernEssentials.dll')" -ForegroundColor Green
    foreach ($old in @('TavernSocial.dll', 'TavernBoard.dll')) {
        if (Test-Path (Join-Path $mods $old)) { Write-Host "    $old is still in Mods: delete it, or that feature stays off." -ForegroundColor Yellow }
    }
}
