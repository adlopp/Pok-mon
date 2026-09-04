# Pokémon Ultra Yea — instrucciones para opencode

## Aviso importante antes de tocar nada
- Este proyecto usa **RPG Maker XP / mkxp-z + Pokemon Essentials + plugins**. Hay una
  CLI de herramienta de compilación (`tools/build_release.ps1`) y un lanzador de
  actualizaciones en C# (`Launcher/`).
- **No compiles ni publiques una release por tu cuenta** salvo que el usuario lo pida
  explícitamente. La compilación requiere `dotnet` (solo en el PC del usuario) y
  `gh` (GitHub CLI) autenticado en `adlopp/Pok-mon`.
- Los cambios de código en `Launcher/` **no se aplican a los jugadores** hasta que se
  publica un `Launcher.exe` nuevo. Los jugadores con un launcher viejo ignoran el
  bloque `delta` del manifest y bajan el zip completo.

## Cada vez que se publica una versión nueva (release)
El `build_release.ps1` genera un **zip completo** (instalación desde cero) y un
**delta zip** (solo los cambios). El lanzador usa el delta para que un jugador con una
versión **igual o posterior a la base** descargue solo los cambios en lugar del zip
completo (~470 MB).

### Pasos que SIGO SIEMPRE al preparar una release

1. Guardar la base del delta (solo hace falta al fijar una nueva base):
   ```powershell
   Copy-Item .\build\files.json    .\build\files-base.json
   Copy-Item .\build\manifest.json .\build\manifest-base.json
   ```
2. Invocar el build con `-DeltaBase` (delta **acumulado** desde esa base):
   ```powershell
   .\tools\build_release.ps1 -Version <X.Y.Z.W> -NotesFile .\notes.md -DeltaBase .\build\files-base.json
   ```
   - El `manifest-base.json` (junto al `files-base.json`) deja `Launcher.exe` fuera
     del delta cuando su versión no cambió, evitando inflar el delta con ~138 MB.
   - Si por o cambio la versión del launcher en `Launcher/UltraYeaLauncher.csproj`
     (campo `<Version>`), ese `Launcher.exe` SÍ debe entrar en el delta: no hace falta
     nada especial, ocurre solo al comparar versiones.
3. Publicar la Release en GitHub con los assets que imprima el script (el zip completo,
   `manifest.json`, `files.json` y `UltraYea-<ver>-delta.zip`):
   ```powershell
   gh release create v<X.Y.Z.W> "build\UltraYea-<X.Y.Z.W>.zip" "build\manifest.json" "build\files.json" "build\UltraYea-<X.Y.Z.W>-delta.zip" --title "v<X.Y.Z.W>" --notes-file .\notes.md
   ```

### Caso especial: no existe `files-base.json` (base antigua)
Si quieres que el delta también sirva a jugadores de una versión **anterior** a la
última (p. ej. saltar de 0.3.1 a la nueva), pero no tienes el `files.json` de esa
base, puedes reconstruirlo desde su `.zip`. El script `tools/publish_delta_with_base.ps1`
hace todo el proceso (reconstruye el files-base desde el zip, sube la versión del
launcher si cambió, compila, empaqueta con `-DeltaBase` y publica):
```powershell
.\tools\publish_delta_with_base.ps1 -NewVersion 0.3.1.3 -BaseZip .\build\UltraYea-0.3.1.zip
```
Ese script recuerda **reemplazar el `Launcher.exe` local** por el compilado nuevo
antes de ejecutar tu copia, para que tu propia instalación se baje el delta.

### Nota sobre el tamaño del delta
El primer delta que incluya un `Launcher.exe` nuevo (porque cambió su código) pesará
~145 MB (lleva el exe), no unos KB. Es ineludible: hay que llevar el launcher nuevo a
los jugadores. En releases posteriores, si la versión del launcher no cambia y existe
`manifest-base.json`, el exe se excluye y el delta vuelve a ser de KB.

### Por qué se hace así (contexto)
- El delta del manifest declara `delta.fromVersion`: la versión **base** desde la que se
  generó. El launcher (`Launcher/Updater.cs`) lo aplica cuando la versión local del
  jugador es **>= fromVersion** (delta acumulado), y si no, cae al zip completo.
- **No** intentar "descargar cada archivo por separado" desde GitHub Releases: los assets
  solo se descargan completos y los archivos del juego **no** están en el repo de git
  (solo el código). Por eso el mecanismo correcto es el delta acumulado.
- Tras publicar una release nueva, comprobar que el build realmente generó un delta
  (no caer en `-NoDelta` por accidente) y que el `manifest.json` de `build/` tiene el
  bloque `delta` con `fromVersion` correcto.

Para los detalles completos, ver `RELEASING.md` (secciones 2 y 8).
