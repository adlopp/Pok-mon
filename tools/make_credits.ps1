#Requires -Version 5
<#
  Genera CREDITS.txt juntando tools\credits_header.txt con los datos
  (Name / Version / Credits / Website) de cada Plugins\*\meta.txt.

  Uso:
      .\tools\make_credits.ps1 -OutFile .\build\CREDITS.txt

  Todo se lee y se escribe como UTF-8 (PowerShell 5.1 asume ANSI si no se dice).
#>
param(
    [Parameter(Mandatory)] [string] $OutFile
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$sb = [System.Text.StringBuilder]::new()

$headerPath = Join-Path $root "tools\credits_header.txt"
if (Test-Path -LiteralPath $headerPath) {
    [void]$sb.AppendLine((Get-Content -Raw -Encoding UTF8 -LiteralPath $headerPath).TrimEnd())
}

[void]$sb.AppendLine()
[void]$sb.AppendLine("================================================================")
[void]$sb.AppendLine(" PLUGINS Y SCRIPTS DE TERCEROS")
[void]$sb.AppendLine("================================================================")

$pluginsDir = Join-Path $root "Plugins"
Get-ChildItem -LiteralPath $pluginsDir -Directory | Sort-Object Name | ForEach-Object {
    $meta = Join-Path $_.FullName "meta.txt"
    if (-not (Test-Path -LiteralPath $meta)) { return }

    $kv = @{}
    Get-Content -Encoding UTF8 -LiteralPath $meta | ForEach-Object {
        if ($_ -match '^\s*(Name|Version|Credits|Website)\s*=\s*(.+?)\s*$') {
            $kv[$Matches[1]] = $Matches[2].Trim()
        }
    }

    $name = if ($kv.ContainsKey("Name") -and $kv["Name"]) { $kv["Name"] } else { $_.Name }
    $line = "- $name"
    if ($kv.ContainsKey("Version") -and $kv["Version"]) { $line += "  (v$($kv['Version']))" }

    [void]$sb.AppendLine()
    [void]$sb.AppendLine($line)
    if ($kv.ContainsKey("Credits") -and $kv["Credits"]) { [void]$sb.AppendLine("    Autores: $($kv['Credits'])") }
    if ($kv.ContainsKey("Website") -and $kv["Website"]) { [void]$sb.AppendLine("    Web:     $($kv['Website'])") }
}

[void]$sb.AppendLine()
[void]$sb.AppendLine("================================================================")
[void]$sb.AppendLine(" Recursos graficos / de audio adicionales:")
[void]$sb.AppendLine("   - Tilesets Gen 4 (HGSS / DPPt): WesleyFG, Kyle-Dove y la comunidad.")
[void]$sb.AppendLine("   - (Anade aqui packs de sprites, musica, SFX, etc. que uses.)")
[void]$sb.AppendLine("================================================================")
[void]$sb.AppendLine(" Si falta alguien, es un error involuntario: avisame y lo corrijo.")
[void]$sb.AppendLine("================================================================")

# UTF-8 con BOM para que se lea bien en el Bloc de notas antiguo.
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Host "-- CREDITS.txt generado" -ForegroundColor DarkGray
