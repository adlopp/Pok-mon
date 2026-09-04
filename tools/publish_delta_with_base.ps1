#Requires -Version 5
<#
================================================================================
  Publica una release que permite a un jugador con la 0.3.1 pasar a una
  version nueva bajando SOLO el delta (no el zip completo).

  Para eso necesita, en tu PC:
    - dotnet  (SDK 8)   -> winget install Microsoft.DotNet.SDK.8
    - gh      (GitHub CLI) autenticado en adlopp/Pok-mon
      (se comprueba la autenticacion antes de publicar)

  Uso tipico (para tu caso 0.3.1 -> 0.3.1.3):
      .\tools\publish_delta_with_base.ps1
      .\tools\publish_delta_with_base.ps1 -NewVersion 0.3.1.3
      .\tools\publish_delta_with_base.ps1 -NewVersion 0.3.1.3 -BaseZip .\build\UltraYea-0.3.1.zip
      .\tools\publish_delta_with_base.ps1 -NewVersion 0.3.1.3 -SkipLauncherBuild -SkipPublish

  Parametros:
    -NewVersion   (opcional) version a publicar. Por defecto 0.3.1.3
    -BaseZip      .zip de la version BASE (la que ya tiene el jugador). Del
                  extrae/hashea para reconstruir el files-base.json.
                  Por defecto .\build\UltraYea-0.3.1.zip
    -SkipLauncherBuild   no compilar el launcher
    -SkipPublish         no publicar la Release (solo generar el paquete)
    -NotesFile           ruta a las notas. Por defecto .\notes.md

  El script NO publica sin tu confirmacion explicita (te pregunta antes).
================================================================================
#>
param(
    [string] $NewVersion = "0.3.1.3",
    [string] $BaseZip = "",
    [switch] $SkipLauncherBuild,
    [switch] $SkipPublish,
    [string] $NotesFile = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)   # ...\Pokémon Ultra Yea
Set-Location $root

# ---------------------------------------------------------------- pre-checks
if (-not $BaseZip) { $BaseZip = Join-Path $root "build\UltraYea-0.3.1.zip" }
if (-not (Test-Path -LiteralPath $BaseZip)) {
    throw "No encuentro el .zip base: $BaseZip`nPasa -BaseZip con la ruta a tu zip de la version base."
}
if (-not $NotesFile) { $NotesFile = Join-Path $root "notes.md" }

Write-Host "== Publicar delta con base en 0.3.1 -> $NewVersion ==" -ForegroundColor Cyan
Write-Host "  base zip : $BaseZip"
Write-Host "  version  : $NewVersion"

# ------------------------------------------------------ 1. reconstruir files-base.json
# El files.json de la base ya no existe en build/ (se sobrescribio). Lo
# reconstruimos hasheando cada archivo del .zip base, igual que hace
# build_release.ps1 (Get-TreeHashes). El .zip tiene una carpeta raiz
# (p. ej. "UltraYea-0.3.1\") que recortamos para las rutas relativas.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$baseFilesJson = Join-Path $root "build\files-base.json"

function Get-ZipTreeHashes {
    # SHA-256 + tamano de cada archivo dentro de un .zip, con rutas relativas
    # a la carpeta raiz unica (si la hay). Mismo shape que files.json.
    param([Parameter(Mandatory)][string] $ZipPath)
    $map  = [ordered]@{}
    $sha  = [System.Security.Cryptography.SHA256]::Create()
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
        try {
            # detectar la carpeta raiz comun (primer segmento de la 1a entrada)
            $firstSegment = ""
            $e0 = $zip.Entries[0]
            if ($e0.FullName -match '^([^/\\]+)[/\\]') { $firstSegment = $Matches[1] }
            $cuts = ($firstSegment.Length + 1)
            $hasRoot = $true
            if (-not $firstSegment) { $cuts = 0; $hasRoot = $false }

            foreach ($e in $zip.Entries) {
                if ($e.FullName.EndsWith('/') -or $e.FullName.EndsWith('\')) { continue } # dirs
                $rel = $e.FullName
                if ($hasRoot) { $rel = $rel.Substring($cuts) }
                $rel = $rel.Replace('\', '/')
                if (-not $rel) { continue }

                $stream = $e.Open()
                try     { $hex = [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
                finally { $stream.Dispose() }
                $map[$rel] = [ordered]@{ sha256 = $hex; size = [int64]$e.Length }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $sha.Dispose() }
    return $map
}

Write-Host "-- reconstruyendo files-base.json desde $BaseZip ..." -ForegroundColor DarkGray
$baseFiles = Get-ZipTreeHashes -ZipPath $BaseZip
Write-Host ("   " + [string]$baseFiles.Count + " archivo(s) en la base") -ForegroundColor DarkGray

$filesManifest = [ordered]@{
    version   = ([System.IO.Path]::GetFileNameWithoutExtension($BaseZip) -replace '^UltraYea-', '')
    generated = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssK")
    files     = $baseFiles
    deleted   = @()
}
[System.IO.File]::WriteAllText($baseFilesJson, ($filesManifest | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "-- files-base.json generado: $baseFilesJson" -ForegroundColor Green

# ------------------------------------------------------ 2. bump version del launcher
# El launcher cambio de codigo (Updater.cs). Hay que subir su <Version> para que
# el delta incluya el Launcher.exe nuevo (y llegue a quien aun no lo tiene).
$csproj = Join-Path $root "Launcher\UltraYeaLauncher.csproj"
$csprojText = [System.IO.File]::ReadAllText($csproj)
$currentProj = ""
if ($csprojText -match '<Version>([^<]+)</Version>') { $currentProj = $Matches[1].Trim() }
$newProj = $currentProj
if (-not $SkipLauncherBuild) {
    # sube la version del launcher en +0.1 (1.1.0 -> 1.2.0)
    $parts = ($currentProj -split '\.') 
    if ($parts.Count -ge 2) {
        [int]$v1 = $parts[0]; [int]$v10 = $parts[1]
        $v10++
        $parts[0] = $v1.ToString(); $parts[1] = $v10.ToString()
        $newProj = ($parts -join '.')
        $csprojText = [regex]::Replace($csprojText, '<Version>[^<]+</Version>', ('<Version>' + $newProj + '</Version>'))
        [System.IO.File]::WriteAllText($csproj, $csprojText, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host ("-- versión del launcher: $currentProj -> $newProj") -ForegroundColor DarkGray
    }
}

# ------------------------------------------------------ 3. build_release (delta acumulado)
# Llamada directa con parámetros nombrados (NO usar $args + splat: es variable
# automática reservada y el splat de un array con strings vacíos corrompe los
# argumentos, p.ej. pasaba "-NotesFile" como valor de NotesFile).
$build = Join-Path $root "tools\build_release.ps1"
$buildArgs = @{
    Version    = $NewVersion
    DeltaBase  = $baseFilesJson
    NotesFile  = $NotesFile
}
if ($SkipLauncherBuild) { $buildArgs.SkipLauncherBuild = $true }

Write-Host "-- ejecutando build_release.ps1 ..." -ForegroundColor DarkGray
& $build @buildArgs
if ($LASTEXITCODE -ne 0) { throw "build_release.ps1 falló (codigo $LASTEXITCODE)." }

# ------------------------------------------------------ 4. publicar la release
if ($SkipPublish) {
    Write-Host ""
    Write-Host "PUBLICACIÓN OMITIDA (-SkipPublish). Revisa build\ y publica a mano con gh." -ForegroundColor Yellow
    exit 0
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) { throw "No se encontró 'gh'. Instala GitHub CLI:  winget install GitHub.cli" }

Write-Host "-- comprobando autenticacion de gh ..." -ForegroundColor DarkGray
$oldEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"   # evitar que el stderr de gh (esperado) aborte
gh auth status 2>$null | Out-Null
$authCode = $LASTEXITCODE
$ErrorActionPreference = $oldEAP
if ($authCode -ne 0) { throw "gh no está autenticado. Ejecuta:  gh auth login" }

# comprobar que el tag no exista ya
$tag = "v$NewVersion"
$ErrorActionPreference = "Continue"
gh release view $tag --repo adlopp/Pok-mon 2>$null | Out-Null
$viewCode = $LASTEXITCODE
$ErrorActionPreference = $oldEAP
if ($viewCode -eq 0) {
    throw "Ya existe la Release '$tag' en adlopp/Pok-mon. Usa -NewVersion con una versión distinta (p. ej. 0.3.1.4)."
}

$zipAsset      = Join-Path $root ("build\UltraYea-" + $NewVersion + ".zip")
$manifestAsset = Join-Path $root "build\manifest.json"
$filesAsset    = Join-Path $root "build\files.json"
$deltaAsset    = Join-Path $root ("build\UltraYea-" + $NewVersion + "-delta.zip")

foreach ($f in @($zipAsset, $manifestAsset, $filesAsset)) {
    if (-not (Test-Path -LiteralPath $f)) { throw "Falta el asset generado: $f" }
}

Write-Host ""
Write-Host "Se publicará la Release en adlopp/Pok-mon:" -ForegroundColor Cyan
Write-Host "  tag     : $tag"
Write-Host "  assets  :"
$zipMb = [math]::Round((Get-Item $zipAsset).Length / 1048576, 1)
$linea = '    {0}      ({1} MB)' -f $zipAsset, $zipMb
Write-Host $linea
Write-Host ('    ' + $manifestAsset)
Write-Host ('    ' + $filesAsset)
if (Test-Path -LiteralPath $deltaAsset) {
    $deltaMb = [math]::Round((Get-Item $deltaAsset).Length / 1048576, 1)
    $linea2 = '    {0}   ({1} MB)' -f $deltaAsset, $deltaMb
    Write-Host $linea2
} else {
    Write-Host '    (NO hay delta.zip: revisa la salida del build, quizá no se generó)' -ForegroundColor Yellow
}
Write-Host "  notas   : $NotesFile"
Write-Host ""

$confirm = Read-Host "¿Publicar ahora? [s/N]"
if ($confirm -notmatch '^[sSyY]') {
    Write-Host 'Cancelado. No se publicó nada. El paquete quedó listo en build\.' -ForegroundColor Yellow
    exit 0
}

if (-not (Test-Path -LiteralPath $deltaAsset)) { throw "No hay delta.zip; el jugador bajaría el zip completo. Aborta y revisa." }

$oldEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"   # el stderr/progreso de gh no debe abortar tras publicar
gh release create $tag $zipAsset $manifestAsset $filesAsset $deltaAsset `
    --repo adlopp/Pok-mon `
    --title $tag `
    --notes-file $NotesFile
$createCode = $LASTEXITCODE
$ErrorActionPreference = $oldEAP
if ($createCode -ne 0) { throw "gh release create falló (codigo $createCode)." }

Write-Host ""
Write-Host "PUBLICADO: https://github.com/adlopp/Pok-mon/releases/tag/$tag" -ForegroundColor Green

# ------------------------------------------------------ 5. recordatorio para ti
Write-Host ""
Write-Host 'IMPORTANTE (para TU copia de 0.3.1):' -ForegroundColor Yellow
Write-Host '  1) La release ya trae el Launcher.exe nuevo dentro del zip y del delta.'
Write-Host '  2) Para que tu copia actual baje solo el delta, reemplaza TU tu Launcher.exe'
Write-Host '     por el nuevo compilado:' -ForegroundColor White
Write-Host '        Copy-Item .\Launcher\dist\Launcher.exe .\Launcher.exe -Force'
Write-Host '  3) Ejecuta el launcher; vera 0.3.1 == base y bajara solo el delta.'
Write-Host '  4) A partir de aqui, cada release usa -DeltaBase (ver AGENTS.md).'
