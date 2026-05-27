$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$source = Join-Path $root 'InteractiveWallpaper.cs'
$exe = Join-Path $dist 'InteractiveWallpaper.exe'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$candidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$csc = $null
foreach ($candidate in $candidates) {
    if (Test-Path $candidate) {
        $csc = $candidate
        break
    }
}

if (-not $csc) {
    throw 'No se encontro csc.exe de .NET Framework 4.x.'
}

& $csc `
    /nologo `
    /target:winexe `
    /platform:x86 `
    /optimize+ `
    /out:$exe `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "La compilacion fallo con codigo $LASTEXITCODE."
}

Write-Host "EXE creado: $exe"
