#Requires -Version 5
<#
================================================================================
  Empaqueta la versión JUGABLE de Pokémon Ultra Yea y genera manifest.json.
================================================================================

  Uso tipico:
      .\tools\build_release.ps1 -Version 1.2.2.0 -NotesFile .\notes.md

  Otros ejemplos:
      .\tools\build_release.ps1 -Version 1.2.2.0 -Notes "Arreglado el crash de la Ruta 4."
      .\tools\build_release.ps1 -Version 1.2.2.0 -NotesFile .\notes.md -Mandatory
      .\tools\build_release.ps1 -Version 1.2.2.0 -Notes "hotfix" -SkipLauncherBuild

  Genera en  build\ :
      build\UltraYea-<version>\       carpeta jugable (para probar en local)
      build\UltraYea-<version>.zip    <-- subir como asset de la Release
      build\manifest.json             <-- subir tambien como asset de la Release

  NO incluye: PBS\, Plugins\ (codigo), Tilesets Gen4\, .vscode\, Game.rxproj,
  backups de Data\, ni ningun archivo de proyecto. Hay una comprobacion final
  que aborta si algo de eso se cuela.
================================================================================
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Notes = "",
    [string] $NotesFile = "",
    [switch] $Mandatory,
    [string] $LauncherExe = "",
    [switch] $SkipLauncherBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # ...\Pokémon Ultra Yea
Set-Location $root

Write-Host "== Pokemon Ultra Yea - build de release $Version ==" -ForegroundColor Cyan

# --- 0. Notas de version ------------------------------------------------------
if ($NotesFile) {
    if (-not (Test-Path -LiteralPath $NotesFile)) { throw "No existe el archivo de notas: $NotesFile" }
    $Notes = Get-Content -Raw -LiteralPath $NotesFile
}

# --- 1. Compilar el launcher ------------------------------------------------
if (-not $LauncherExe) { $LauncherExe = Join-Path $root "Launcher\dist\Launcher.exe" }
if (-not $SkipLauncherBuild) {
    Write-Host "-- compilando el launcher..." -ForegroundColor DarkGray
    & (Join-Path $root "Launcher\build.ps1")
}
if (-not (Test-Path -LiteralPath $LauncherExe)) {
    throw "No encuentro Launcher.exe en '$LauncherExe'. Compilalo con .\Launcher\build.ps1 o pasa -LauncherExe."
}

# --- 2. Carpeta de salida limpia ------------------------------------------
$stage = Join-Path $root ("build\UltraYea-" + $Version)
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# --- 3. Lista BLANCA de lo que se distribuye ------------------------------
$includeFiles = @(
    "Game.exe", "Game.ini", "mkxp.json", "preload.rb", "soundfont.sf2",
    "RGSS104E.dll", "libgcc_s_seh-1.dll", "libgomp-1.dll", "libwinpthread-1.dll",
    "x64-msvcrt-ruby310.dll", "zlib1.dll"
)
$includeDirs = @("Audio", "Fonts", "Graphics", "Ruby Library 3.3.0")

foreach ($f in $includeFiles) {
    if (Test-Path -LiteralPath $f) { Copy-Item -LiteralPath $f -Destination $stage }
    else { Write-Warning "  falta el archivo:  $f" }
}
foreach ($d in $includeDirs) {
    if (Test-Path -LiteralPath $d) {
        Write-Host "-- copiando $d\ ..." -ForegroundColor DarkGray
        Copy-Item -LiteralPath $d -Destination $stage -Recurse
    }
    else { Write-Warning "  falta la carpeta:  $d" }
}

# --- 3b. Data\ : SOLO runtime (.rxdata / .dat), sin backups --------------
$dataOut = Join-Path $stage "Data"
New-Item -ItemType Directory -Force -Path $dataOut | Out-Null
$dataCount = 0
Get-ChildItem -LiteralPath (Join-Path $root "Data") -File | Where-Object {
    ($_.Extension -ieq ".rxdata" -or $_.Extension -ieq ".dat") -and
    ($_.Name -notmatch "(?i)backup") -and
    ($_.Name -notmatch "(?i)\.bak")
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $dataOut
    $dataCount++
}
Write-Host "-- Data\: $dataCount archivo(s) de runtime copiados" -ForegroundColor DarkGray

# --- 4. Launcher + config + version -------------------------------------
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
Copy-Item -LiteralPath $LauncherExe -Destination (Join-Path $stage "Launcher.exe")
Copy-Item -LiteralPath (Join-Path $root "Launcher\launcher_config.json") -Destination $stage
[System.IO.File]::WriteAllText((Join-Path $stage "version.txt"), $Version, $Utf8NoBom)

# --- 5. README (jugadores) + CREDITS ----------------------------------
Copy-Item -LiteralPath (Join-Path $root "release-assets\README.txt") -Destination $stage
& (Join-Path $root "tools\make_credits.ps1") -OutFile (Join-Path $stage "CREDITS.txt")

# --- 6. Comprobacion anti-fugas de material de desarrollo ------------
$forbiddenNames = @("PBS", "Plugins", ".vscode", ".git", "Tilesets Gen4",
    "Game.rxproj", "rubocop.yml", ".editorconfig", ".nomedia", "RELEASING.md")
$leak = Get-ChildItem -LiteralPath $stage -Recurse -Force | Where-Object {
    ($forbiddenNames -contains $_.Name) -or ($_.Name -match "(?i)\.(rxproj|bak|psd|kra)$")
}
if ($leak) {
    Write-Host ""
    Write-Host "SE HA COLADO MATERIAL DE DESARROLLO EN EL PAQUETE:" -ForegroundColor Red
    $leak.FullName | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    throw "Abortado. Revisa la lista blanca de build_release.ps1."
}

# --- 7. Zip (con carpeta raiz, para una extraccion ordenada) ---------
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $root ("build\UltraYea-" + $Version + ".zip")
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Write-Host "-- comprimiendo (esto tarda un poco)..." -ForegroundColor DarkGray
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

# --- 8. manifest.json ----------------------------------------------
$sha  = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLower()
$size = (Get-Item -LiteralPath $zip).Length
$manifest = [ordered]@{
    version   = $Version
    released  = (Get-Date -Format "yyyy-MM-dd")
    mandatory = [bool]$Mandatory
    notes     = $Notes
    package   = [ordered]@{
        asset  = [System.IO.Path]::GetFileName($zip)
        sha256 = $sha
        size   = $size
    }
}
# Nota: si necesitas borrar archivos que ya no existen en esta version, edita
# manifest.json a mano y anade  "delete": ["ruta/relativa", ...]  antes de publicar.
$manifestPath = Join-Path $root "build\manifest.json"
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $Utf8NoBom)

# --- 9. Resumen ---------------------------------------------------
$zipMb = [math]::Round($size / 1MB, 1)
Write-Host ""
Write-Host "LISTO" -ForegroundColor Green
Write-Host "  carpeta  : $stage"
Write-Host "  zip      : $zip  ($zipMb MB)"
Write-Host "  sha256   : $sha"
Write-Host "  manifest : $manifestPath"
Write-Host ""
Write-Host "1) Prueba:  `"$stage\Launcher.exe`""
Write-Host "2) Publica la Release:" -ForegroundColor Cyan
Write-Host "     gh release create v$Version `"$zip`" `"$manifestPath`" --title `"v$Version`" --notes-file `"$( if ($NotesFile) { $NotesFile } else { '<notas>' } )`""
