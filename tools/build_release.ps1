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
    [switch] $SkipLauncherBuild,

    # --- actualizaciones delta (diferenciales) ---------------------------------
    # El script genera SIEMPRE files.json (hash de cada archivo). Si ademas puede
    # obtener el files.json de una version anterior, genera un delta zip pequeno.
    #
    # El delta es ACUMULATIVO: se genera "desde" una version base y contiene TODOS
    # los cambios desde esa base hasta esta version. Gracias a eso, cualquier
    # jugador con version >= base puede actualizar bajando solo el delta (el
    # launcher lo acepta cuando su version local >= delta.fromVersion).
    #
    # Que base usar:
    #   -DeltaBase  ruta a un files.json de la PRIMERA version que quieras seguir
    #               soportando con delta (p. ej. build\files-0.3.1.0.json).
    #               El delta que se genere acumulara todos los cambios desde ahi.
    #   -PrevFilesJson  (comportamiento clasico) files.json de la version INMEDIATA
    #                   anterior; genera un delta solo para ese ultimo salto.
    #   -PrevDir        carpeta ya empaquetada de una version anterior.
    #   -PrevVersion    numero de esa version anterior (si no se deduce solo).
    #   -NoDelta        no intentar generar el delta zip (solo files.json + zip completo)
    [string] $PrevFilesJson = "",
    [string] $PrevDir = "",
    [string] $PrevVersion = "",
    [string] $DeltaBase = "",
    [switch] $NoDelta
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # ...\Pokémon Ultra Yea
Set-Location $root

Write-Host "== Pokemon Ultra Yea - build de release $Version ==" -ForegroundColor Cyan

# --- 0. Notas de version ------------------------------------------------------
if ($NotesFile) {
    if (-not (Test-Path -LiteralPath $NotesFile)) { throw "No existe el archivo de notas: $NotesFile" }
    # ReadAllText -> string limpio en UTF-8 (Get-Content adorna el string con
    # metadatos que ConvertTo-Json luego expande y rompe el manifest).
    $Notes = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesFile).Path, [System.Text.Encoding]::UTF8)
}
$Notes = [string]$Notes

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

# --- 3a. Sincroniza el titulo de la ventana con la version --------------
#   La ventana del juego mostrara siempre "Pokemon Ultra Yea <version>",
#   sin depender de que mkxp.json del proyecto este actualizado a mano.
$mkxpPath = Join-Path $stage "mkxp.json"
if (Test-Path -LiteralPath $mkxpPath) {
    $mkxp = [System.IO.File]::ReadAllText($mkxpPath, [System.Text.Encoding]::UTF8)
    $mkxp = [regex]::Replace($mkxp,
        '("windowTitle"\s*:\s*")[^"]*(")',
        ('${1}Pok' + [char]0x00E9 + 'mon Ultra Yea ' + $Version + '${2}'))
    [System.IO.File]::WriteAllText($mkxpPath, $mkxp, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "-- titulo de ventana: 'Pok$([char]0x00E9)mon Ultra Yea $Version'" -ForegroundColor DarkGray
}

# --- 3b. Data\ : todo lo de runtime, sin backups ----------------------
#   .rxdata (mapas/scripts/tilesets...), .dat (PBS compilado de Essentials),
#   .ebdx (datos compilados de Elite Battle DX). Se excluyen solo los backups
#   y cualquier .txt/.bak que se cuele.
$dataOut = Join-Path $stage "Data"
New-Item -ItemType Directory -Force -Path $dataOut | Out-Null
$dataCount = 0
Get-ChildItem -LiteralPath (Join-Path $root "Data") -File | Where-Object {
    ($_.Extension -imatch "^\.(rxdata|dat|ebdx)$") -and
    ($_.Name -notmatch "(?i)backup") -and
    ($_.Name -notmatch "(?i)\.bak") -and
    ($_.Name -notmatch "(?i)_old(er)?\.") -and
    ($_.Name -notmatch "(?i)CORRUPTO")
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

# --- 5. Textos para el jugador (junto a Launcher.exe) + CREDITS ---------
Copy-Item -LiteralPath (Join-Path $root "release-assets\LEER IMPORTANTE.txt") -Destination $stage
Copy-Item -LiteralPath (Join-Path $root "release-assets\README.txt") -Destination $stage
& (Join-Path $root "tools\make_credits.ps1") -OutFile (Join-Path $stage "CREDITS.txt")

# --- 6. Comprobacion anti-fugas -------------------------------------
# 6a. En la RAIZ del paquete solo puede haber material jugable (lista blanca).
#     Asi se detecta PBS/, Plugins/, .vscode/, Game.rxproj, etc. sin dar falsos
#     positivos con carpetas legitimas anidadas como Graphics\Plugins.
$allowedTop = @(
    "Game.exe", "Game.ini", "mkxp.json", "preload.rb", "soundfont.sf2",
    "RGSS104E.dll", "libgcc_s_seh-1.dll", "libgomp-1.dll", "libwinpthread-1.dll",
    "x64-msvcrt-ruby310.dll", "zlib1.dll",
    "Audio", "Fonts", "Graphics", "Ruby Library 3.3.0", "Data",
    "Launcher.exe", "launcher_config.json", "version.txt",
    "LEER IMPORTANTE.txt", "README.txt", "CREDITS.txt"
)
$unexpected = Get-ChildItem -LiteralPath $stage -Force | Where-Object { $allowedTop -notcontains $_.Name }
if ($unexpected) {
    Write-Host ""
    Write-Host "ARCHIVOS INESPERADOS EN LA RAIZ DEL PAQUETE:" -ForegroundColor Red
    $unexpected.FullName | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    throw "Abortado. En la raiz del paquete solo debe haber material jugable."
}

# 6b. Poda de basura en cualquier nivel (backups, arte fuente, temporales del SO).
#     No aborta: son archivos que el juego no usa; simplemente no se distribuyen.
$junk = Get-ChildItem -LiteralPath $stage -Recurse -Force -File | Where-Object {
    $_.Extension -match "(?i)^\.(psd|kra|clip|xcf|ai|rxproj|bak)$" -or
    $_.Name -match "(?i)(_backup|backup_|\bbackup\b|_orig\.|\.orig\b)" -or
    $_.Name -in @("Thumbs.db", "desktop.ini", ".DS_Store")
}
if ($junk) {
    Write-Host "-- podando $($junk.Count) archivo(s) que el juego no usa:" -ForegroundColor DarkYellow
    $junk | ForEach-Object {
        Write-Host ("   {0}" -f $_.FullName.Substring($stage.Length + 1)) -ForegroundColor DarkYellow
        Remove-Item -LiteralPath $_.FullName -Force
    }
}

# --- 7. Zip (con carpeta raiz, para una extraccion ordenada) ---------
#   Fastest, no Optimal: casi todo el peso son PNG/OGG ya comprimidos, asi que
#   el zip queda practicamente igual de tamano y tarda la mitad.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $root ("build\UltraYea-" + $Version + ".zip")
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Write-Host "-- comprimiendo (esto tarda un poco)..." -ForegroundColor DarkGray
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stage, $zip, [System.IO.Compression.CompressionLevel]::Fastest, $true)

# --- 8. manifest.json (se escribe al final, tras calcular delta) ---------
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
# (Los borrados del delta salen solos de comparar con la version anterior.)
$manifestPath = Join-Path $root "build\manifest.json"

# --- 8b. files.json + delta zip (actualizaciones diferenciales) ---------
#   files.json : hash SHA-256 y tamano de CADA archivo del paquete.
#   delta zip  : SOLO los archivos que cambiaron respecto a la version anterior.
#   El launcher, si tu version local == delta.fromVersion, baja solo el delta.
function Get-TreeHashes {
    # SHA-256 + tamano de cada archivo bajo $Base, con .NET puro. Get-FileHash /
    # Get-ChildItem por archivo tienen un coste de invocacion brutal en PS 5.1
    # (~4 min para 30k archivos); esto lo deja en ~30 s.
    param([Parameter(Mandatory)][string] $Base)
    $map  = [ordered]@{}
    $full = (Resolve-Path -LiteralPath $Base).Path.TrimEnd('\')
    $cut  = $full.Length + 1
    $sha  = [System.Security.Cryptography.SHA256]::Create()
    try {
        foreach ($path in [System.IO.Directory]::EnumerateFiles($full, '*', [System.IO.SearchOption]::AllDirectories)) {
            $rel = $path.Substring($cut).Replace('\', '/')
            $fs  = [System.IO.File]::OpenRead($path)
            try     { $hex = [System.BitConverter]::ToString($sha.ComputeHash($fs)).Replace('-', '').ToLowerInvariant() }
            finally { $fs.Dispose() }
            $map[$rel] = [ordered]@{
                sha256 = $hex
                size   = [int64] ([System.IO.FileInfo]::new($path)).Length
            }
        }
    }
    finally { $sha.Dispose() }
    return $map
}
function ConvertTo-Map {
    # Dictionary ORDINAL (sensible a mayus/minus) para que el diff con un
    # files.json descargado sea exacto.
    param($Node)
    $h = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::Ordinal)
    if ($null -ne $Node) { foreach ($p in $Node.PSObject.Properties) { $h[$p.Name] = $p.Value } }
    return $h
}
function Read-JsonUtf8 {
    # Get-Content -Raw en PS 5.1 lee UTF-8-sin-BOM como ANSI y destroza rutas
    # con acentos (p. ej. "Audio/BGM/Routé 1.mid"). Hay que forzar UTF-8.
    param([Parameter(Mandatory)][string] $Path)
    return ([System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json)
}

function Get-LauncherVersion {
    param([Parameter(Mandatory)][string] $ExePath)
    if (-not (Test-Path -LiteralPath $ExePath)) { return $null }
    $fi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath)
    $v = $fi.ProductVersion; if (-not $v) { $v = $fi.FileVersion }
    if ($v) { return ($v -split '\+')[0].Trim() }   # "1.1.0+abcdef" -> "1.1.0"
    return $null
}

Write-Host "-- calculando el hash de cada archivo del paquete..." -ForegroundColor DarkGray
$newFiles = Get-TreeHashes -Base $stage
Write-Host ("   {0} archivo(s) en el paquete" -f $newFiles.Count) -ForegroundColor DarkGray

$launcherVer     = Get-LauncherVersion -ExePath (Join-Path $stage "Launcher.exe")
$prevLauncherVer = $null

# localizar el files.json de la version base/anterior
$prevMap   = $null
$deltaFrom = $PrevVersion
$tmpPrev   = $null
if ($DeltaBase -and (Test-Path -LiteralPath $DeltaBase)) {
    # Delta ACUMULADO: comparar contra una version base concreta (la mas antigua
    # que se quiera seguir soportando). El delta incluira todo lo que haya
    # cambiado desde esa base hasta esta version -> vale para CUALQUIER version
    # intermedia >= base.
    $pj = Read-JsonUtf8 -Path $DeltaBase
    $prevMap = ConvertTo-Map $pj.files
    if (-not $deltaFrom) { $deltaFrom = [string]$pj.version }
    # Si junto al files-base hay un manifest de esa misma base, recuperamos su
    # launcherVersion para poder dejar Launcher.exe fuera del delta cuando no haya
    # cambiado (si no, el delta acumulado cargaria con los 138 MB del exe en cada
    # release). Convencion: "files-base.json" junto a "manifest-base.json", o el
    # nombre de $DeltaBase con "manifest" por "files".
    $baseManifest = Join-Path (Split-Path -Parent $DeltaBase) ((Split-Path -Leaf $DeltaBase) -replace '^files', 'manifest')
    if (-not (Test-Path -LiteralPath $baseManifest)) { $baseManifest = Join-Path (Split-Path -Parent $DeltaBase) "manifest.json" }
    if (Test-Path -LiteralPath $baseManifest) { $prevLauncherVer = [string](Read-JsonUtf8 -Path $baseManifest).launcherVersion }
    Write-Host "-- delta ACUMULADO: comparando con $DeltaBase (v$deltaFrom)" -ForegroundColor DarkGray
}
elseif ($PrevFilesJson -and (Test-Path -LiteralPath $PrevFilesJson)) {
    $pj = Read-JsonUtf8 -Path $PrevFilesJson
    $prevMap = ConvertTo-Map $pj.files
    if (-not $deltaFrom) { $deltaFrom = [string]$pj.version }
    Write-Host "-- delta: comparando con $PrevFilesJson (v$deltaFrom)" -ForegroundColor DarkGray
}
elseif ($PrevDir -and (Test-Path -LiteralPath $PrevDir)) {
    Write-Host "-- delta: hasheando la carpeta anterior $PrevDir ..." -ForegroundColor DarkGray
    $prevMap = Get-TreeHashes -Base $PrevDir
    if (-not $deltaFrom) {
        $vt = Join-Path $PrevDir "version.txt"
        if (Test-Path -LiteralPath $vt) { $deltaFrom = ([System.IO.File]::ReadAllText($vt)).Trim() }
    }
    $prevLauncherVer = Get-LauncherVersion -ExePath (Join-Path $PrevDir "Launcher.exe")
    Write-Host "-- delta: version anterior = v$deltaFrom" -ForegroundColor DarkGray
}
elseif (-not $NoDelta) {
    $ghCmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghCmd) {
        try {
            $tmpPrev = Join-Path ([System.IO.Path]::GetTempPath()) ("uy_prev_" + [guid]::NewGuid().ToString("N"))
            New-Item -ItemType Directory -Force -Path $tmpPrev | Out-Null
            & gh release download --repo adlopp/Pok-mon --pattern "files.json" --pattern "manifest.json" --dir $tmpPrev 2>$null
            $pf = Join-Path $tmpPrev "files.json"
            if (Test-Path -LiteralPath $pf) {
                $pj = Read-JsonUtf8 -Path $pf
                $prevMap = ConvertTo-Map $pj.files
                if (-not $deltaFrom) { $deltaFrom = [string]$pj.version }
                $pm = Join-Path $tmpPrev "manifest.json"
                if (Test-Path -LiteralPath $pm) { $prevLauncherVer = [string](Read-JsonUtf8 -Path $pm).launcherVersion }
                Write-Host "-- delta: comparando con el files.json de la ultima Release (v$deltaFrom)" -ForegroundColor DarkGray
            } else {
                Write-Host "-- delta: la ultima Release no trae files.json; el primer delta sera el de la PROXIMA version" -ForegroundColor DarkYellow
            }
        } catch {
            Write-Warning ("  no pude bajar files.json de la ultima Release: " + $_.Exception.Message)
        }
    } else {
        Write-Host "-- delta: 'gh' no esta instalado; pasa -PrevDir o -PrevFilesJson para generar el delta" -ForegroundColor DarkYellow
    }
}

# diff -> changed / deleted
$changed = New-Object System.Collections.Generic.List[string]
$deleted = New-Object System.Collections.Generic.List[string]
if ($prevMap -and $prevMap.Count -gt 0 -and $deltaFrom) {
    foreach ($rel in $newFiles.Keys) {
        $o = $prevMap[$rel]
        if ($null -eq $o -or [string]$o.sha256 -ne [string]$newFiles[$rel].sha256) { [void]$changed.Add($rel) }
    }
    foreach ($rel in $prevMap.Keys) {
        if (-not $newFiles.Contains($rel)) { [void]$deleted.Add($rel) }
    }

    # Launcher.exe (138 MB) cambia de hash en CADA compilacion aunque el codigo
    # sea identico (GUID/timestamp del PE). Si la version del producto no ha
    # cambiado, lo sacamos del delta: el jugador se queda con su launcher, que
    # es equivalente. El launcher, ademas, ya ignora Launcher.exe al verificar.
    if ($launcherVer -and $prevLauncherVer -and $launcherVer -eq $prevLauncherVer) {
        if ($changed.Remove("Launcher.exe")) {
            Write-Host "-- delta: Launcher.exe fuera (sin cambios, v$launcherVer)" -ForegroundColor DarkGray
        }
    }
    elseif ($changed -contains "Launcher.exe") {
        Write-Host ("-- delta: Launcher.exe DENTRO (v{0} -> v{1})" -f $prevLauncherVer, $launcherVer) -ForegroundColor DarkGray
    }
}

# files.json (siempre)
$filesManifest = [ordered]@{
    version   = $Version
    generated = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssK")
    files     = $newFiles
    deleted   = @($deleted)
}
$filesJsonPath = Join-Path $root "build\files.json"
[System.IO.File]::WriteAllText($filesJsonPath, ($filesManifest | ConvertTo-Json -Depth 8), $Utf8NoBom)

# delta zip (solo si hay version anterior y algo cambio)
$deltaZip = $null
if ($deltaFrom -and $prevMap -and $prevMap.Count -gt 0 -and $changed.Count -gt 0) {
    $deltaStage = Join-Path $root ("build\UltraYea-" + $Version + "-delta")
    if (Test-Path -LiteralPath $deltaStage) { Remove-Item -LiteralPath $deltaStage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $deltaStage | Out-Null
    foreach ($rel in $changed) {
        $srcF = Join-Path $stage      ($rel -replace '/', '\')
        $dstF = Join-Path $deltaStage ($rel -replace '/', '\')
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dstF) | Out-Null
        Copy-Item -LiteralPath $srcF -Destination $dstF
    }
    # el propio files.json viaja DENTRO del delta zip (comprime a ~2 MB): asi el
    # launcher no tiene que descargarlo aparte. NO es un archivo del juego, va
    # solo en la raiz del delta y el launcher lo excluye al aplicar.
    Copy-Item -LiteralPath $filesJsonPath -Destination (Join-Path $deltaStage "files.json")
    $deltaZip = Join-Path $root ("build\UltraYea-" + $Version + "-delta.zip")
    if (Test-Path -LiteralPath $deltaZip) { Remove-Item -LiteralPath $deltaZip -Force }
    # el delta si va en Optimal: son pocos archivos (tarda nada) y asi el
    # files.json embebido (~10 MB de texto) comprime a ~2 MB.
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $deltaStage, $deltaZip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    $dMb = [math]::Round((Get-Item -LiteralPath $deltaZip).Length / 1MB, 1)
    Write-Host ("-- delta zip: {0} cambiado(s), {1} borrado(s)  ->  UltraYea-{2}-delta.zip ({3} MB)" -f `
        $changed.Count, $deleted.Count, $Version, $dMb) -ForegroundColor Green
}
elseif (-not $NoDelta) {
    Write-Host "-- delta zip: no se genera (sin version anterior con que comparar)" -ForegroundColor DarkYellow
}

if ($tmpPrev -and (Test-Path -LiteralPath $tmpPrev)) {
    Remove-Item -LiteralPath $tmpPrev -Recurse -Force -ErrorAction SilentlyContinue
}

# completar y escribir manifest.json
if ($launcherVer) { $manifest['launcherVersion'] = $launcherVer }
$manifest['files'] = [ordered]@{
    asset  = "files.json"
    sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $filesJsonPath).Hash.ToLower()
    size   = [int64] (Get-Item -LiteralPath $filesJsonPath).Length
}
if ($deltaZip -and (Test-Path -LiteralPath $deltaZip)) {
    $manifest['delta'] = [ordered]@{
        fromVersion = [string]$deltaFrom
        asset       = [System.IO.Path]::GetFileName($deltaZip)
        sha256      = (Get-FileHash -Algorithm SHA256 -LiteralPath $deltaZip).Hash.ToLower()
        size        = [int64] (Get-Item -LiteralPath $deltaZip).Length
    }
}
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $Utf8NoBom)

# --- 9. Resumen ---------------------------------------------------
$zipMb = [math]::Round($size / 1MB, 1)
Write-Host ""
Write-Host "LISTO" -ForegroundColor Green
Write-Host "  carpeta  : $stage"
Write-Host "  zip      : $zip  ($zipMb MB)"
Write-Host "  sha256   : $sha"
Write-Host "  manifest : $manifestPath"
Write-Host "  files    : $filesJsonPath"
if ($deltaZip) {
    Write-Host "  delta    : $deltaZip  (acumulado desde v$deltaFrom; vale para versiones >= v$deltaFrom)"
}
Write-Host ""
$ghAssets = "`"$zip`" `"$manifestPath`" `"$filesJsonPath`""
if ($deltaZip) { $ghAssets += " `"$deltaZip`"" }
Write-Host "1) Prueba:  `"$stage\Launcher.exe`""
Write-Host "2) Publica la Release:" -ForegroundColor Cyan
Write-Host "     gh release create v$Version $ghAssets --title `"v$Version`" --notes-file `"$( if ($NotesFile) { $NotesFile } else { '<notas>' } )`""
