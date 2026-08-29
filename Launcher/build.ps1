#Requires -Version 5
<#
  Compila el launcher a un ÚNICO Launcher.exe autocontenido.
  El jugador NO necesita tener .NET instalado.

  Requisito (solo en tu PC, una vez):
      winget install Microsoft.DotNet.SDK.8

  Uso:
      .\Launcher\build.ps1
  Resultado:
      .\Launcher\dist\Launcher.exe
#>
param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if (-not $dotnet) {
    throw "No se encontró 'dotnet'. Instala el SDK con:  winget install Microsoft.DotNet.SDK.8"
}

Push-Location $here
try {
    $dist = Join-Path $here "dist"
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

    dotnet publish "UltraYeaLauncher.csproj" -c $Configuration -o $dist
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish devolvió codigo $LASTEXITCODE" }

    # El publish de single-file deja solo Launcher.exe; por si acaso, quita restos.
    Get-ChildItem $dist -File | Where-Object { $_.Name -ne "Launcher.exe" } | Remove-Item -Force -ErrorAction SilentlyContinue

    $exe = Join-Path $dist "Launcher.exe"
    $mb  = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "OK  ->  $exe  ($mb MB)" -ForegroundColor Green
}
finally {
    Pop-Location
}
