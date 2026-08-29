# Launcher de Pokémon Ultra Yea

Pequeño actualizador en C# / .NET 8 (WinForms). Al abrirlo:

1. Lee `version.txt` (la versión instalada, junto a `Launcher.exe`).
2. Consulta la **última Release** del repositorio de GitHub indicado en
   `launcher_config.json` (`https://api.github.com/repos/OWNER/REPO/releases/latest`).
3. Si hay `manifest.json` entre los assets, lo usa para saber versión, SHA-256,
   notas y archivos a borrar. Si no, usa la etiqueta de la Release y el primer `.zip`.
4. Si la versión remota es mayor: muestra el changelog y el botón **Actualizar y jugar**.
   Descarga el `.zip` con barra de progreso, verifica el SHA-256, lo extrae y copia
   los archivos sobre la carpeta del juego (sin tocar `version.txt`,
   `launcher_config.json` ni las partidas guardadas, que viven en
   `%AppData%\Roaming\Pokemon Ultra Yea`).
5. Escribe la nueva `version.txt` y abre `Game.exe`.

Si el propio `Launcher.exe` cambia, se renombra a `Launcher.exe.old` (permitido en
Windows aunque esté en ejecución), se coloca el nuevo, y el `.old` se borra en el
siguiente arranque.

## Compilar

Requisito, solo en tu PC y una sola vez:

```powershell
winget install Microsoft.DotNet.SDK.8
```

Luego:

```powershell
.\Launcher\build.ps1
# -> Launcher\dist\Launcher.exe   (autocontenido, ~35-45 MB, sin dependencias)
```

`tools\build_release.ps1` ya llama a esto automáticamente.

## Archivos

| Archivo | Qué es |
|---|---|
| `UltraYeaLauncher.csproj` | Proyecto. Publica single-file, self-contained, win-x64. |
| `app.manifest` | `asInvoker` (nunca pide admin) + DPI PerMonitorV2. |
| `Program.cs` | Punto de entrada; limpia el `Launcher.exe.old`. |
| `LauncherConfig.cs` | Carga `launcher_config.json` (admite `//` y comas finales). |
| `GitHubModels.cs` | Tipos de la API de GitHub y de `manifest.json`. |
| `GitHubClient.cs` | Llama a la API de Releases. |
| `VersionUtil.cs` | Comparación de versiones tipo `1.2.1.1` vs `1.2.2.0`. |
| `Updater.cs` | Descarga, verifica, extrae, aplica, auto-actualiza. |
| `MainForm.cs` | Interfaz. |
| `launcher_config.json` | **Plantilla que se distribuye con el juego.** Edita `repoOwner`/`repoName`. |

## No se distribuye el código del launcher

En el `.zip` del juego solo va `Launcher.exe` + `launcher_config.json`. Esta carpeta
`Launcher/` es parte del proyecto de desarrollo.
