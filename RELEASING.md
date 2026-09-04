# Publicar y actualizar Pokémon Ultra Yea

Flujo: **tú** publicas una *Release* en GitHub con el `.zip` del juego y un
`manifest.json`. El **launcher** de cada jugador detecta la Release nueva, la
descarga y la aplica.

---

## 1. Preparación (una sola vez)

### 1.1 Instala el SDK de .NET (solo tu PC)

```powershell
winget install Microsoft.DotNet.SDK.8
```

Los jugadores **no** necesitan .NET: `Launcher.exe` sale autocontenido.

### 1.2 Repositorio de GitHub

Ya está creado: **`adlopp/Pok-mon`** (público), en
<https://github.com/adlopp/Pok-mon>. Contiene solo el launcher y estas
herramientas; el juego va en las *Releases*.

> El repositorio **público** hace que los assets de las Releases se puedan
> descargar sin token. Si lo pones privado, el launcher no podrá bajar nada
> sin autenticación.
>
> Si algún día lo renombras (p. ej. a `pokemon-ultra-yea`), GitHub mantiene una
> redirección, pero actualiza `repoName` en `Launcher/launcher_config.json` y
> saca una Release nueva para que los launchers ya instalados apunten al
> nombre nuevo.

### 1.3 Configura el launcher

[Launcher/launcher_config.json](Launcher/launcher_config.json) ya apunta al
repositorio:

```json
"repoOwner": "adlopp",
"repoName":  "Pok-mon",
```

### 1.4 Compila el launcher una vez para probar

```powershell
.\Launcher\build.ps1        # -> Launcher\dist\Launcher.exe
```

---

## 2. Sacar una versión nueva

### 2.1 Deja el juego listo

1. Sube el número de versión donde lo tengas en el juego (p. ej. el
   `windowTitle` de [mkxp.json](mkxp.json), o donde muestres la versión in-game).
2. **Recompila el cache de plugins**: borra `Data/PluginScripts.rxdata` y abre
   el juego una vez para que Essentials lo regenere.
3. Escribe las notas de versión en un `notes.md` (se ponen en la Release y en
   `manifest.json`).

### 2.2 Empaqueta

```powershell
.\tools\build_release.ps1 -Version 1.2.2.0 -NotesFile .\notes.md
```

Esto:

- compila el launcher,
- copia **solo** lo jugable (lista blanca; ver más abajo),
- mete `Launcher.exe`, `launcher_config.json`, `version.txt`, `README.txt`,
  y genera `CREDITS.txt` a partir de los `meta.txt` de los plugins,
- **aborta si se cuela** `PBS/`, `Plugins/`, `.vscode/`, `Game.rxproj`,
  backups, `.psd`, etc.,
- crea `build\UltraYea-1.2.2.0.zip` y `build\manifest.json` (con SHA-256),
- crea `build\files.json` (hash y tamaño de **cada** archivo del paquete),
- crea `build\UltraYea-1.2.2.0-delta.zip` con **solo lo que cambió** respecto a
  la versión anterior — ver [§ 8, actualizaciones delta](#8-actualizaciones-delta-diferenciales).

Para una actualización obligatoria: añade `-Mandatory`.

> **Consejo: delta válido para CUALQUIER versión.** Por defecto el delta se
> genera respecto a la *última* Release, así que solo sirve para quien ya estaba
> al día. Para que el delta sirva a cualquier jugador desde una versión base
> concreta (bajando solo los cambios, no el zip completo), guarda el `files.json`
> de esa base y pásalo con `-DeltaBase`:
>
> ```powershell
> # en la release de la base, guarda files.json + manifest.json:
> Copy-Item .\build\files.json    .\build\files-base.json
> Copy-Item .\build\manifest.json .\build\manifest-base.json
> # luego, en cada release nueva:
> .\tools\build_release.ps1 -Version 1.2.2.0 -NotesFile .\notes.md -DeltaBase .\build\files-base.json
> ```
>
> El `manifest-base.json` es opcional pero recomendable: sirve para que el build
> sepa la versión del launcher en la base y deje `Launcher.exe` fuera del delta
> cuando no haya cambiado (si falta, el delta carga con los ~138 MB del exe en
> cada release). Así el delta acumula **todos** los cambios desde esa base y
> cualquier jugador con versión ≥ base actualiza bajando solo el delta. El delta
> crece con cada versión; cuando quieras soltarlo, regenera `files-base.json`
> (y `manifest-base.json`) desde una release más nueva.

### 2.3 Prueba

Abre `build\UltraYea-1.2.2.0\Launcher.exe` y comprueba que el juego arranca.

### 2.4 Publica la Release

```powershell
gh release create v1.2.2.0 `
  "build\UltraYea-1.2.2.0.zip" `
  "build\manifest.json" `
  "build\files.json" `
  "build\UltraYea-1.2.2.0-delta.zip" `
  --title "v1.2.2.0" `
  --notes-file .\notes.md
```

(El script imprime la línea `gh release create` ya montada con los assets que
haya generado — cópiala de ahí.)

Hecho. El launcher de los jugadores lo verá en el siguiente arranque.

> La etiqueta (`v1.2.2.0`) y el `-Version` del script deben coincidir. El
> launcher compara `1.2.2.0` con el `version.txt` local componente a
> componente, así que usa siempre el mismo formato numérico.

---

## 3. La primera vez que repartes el juego

Comparte el enlace del `.zip` de la primera Release, o el enlace permanente
a la última:

```
https://github.com/adlopp/Pok-mon/releases/latest
```

Si el `.zip` es grande y quieres un espejo, puedes subir una copia a Google
Drive **solo para esa primera descarga** — las actualizaciones ya van por
GitHub y no tocan Drive, así que no te afecta el límite de cuota de Drive.

A partir de ahí, el jugador solo abre `Launcher.exe`.

---

## 4. Qué se distribuye y qué no

**SÍ va en el `.zip`:**

| | |
|---|---|
| `Game.exe` + DLLs | `RGSS104E`, `libgcc_s_seh-1`, `libgomp-1`, `libwinpthread-1`, `x64-msvcrt-ruby310`, `zlib1` |
| Runtime mkxp-z | `Ruby Library 3.3.0/`, `soundfont.sf2`, `mkxp.json`, `preload.rb`, `Game.ini` |
| `Data/` | solo `*.rxdata` y `*.dat` de runtime (sin `*Backup*`, sin `*.bak*`) |
| Contenido | `Graphics/`, `Audio/`, `Fonts/` |
| Añadidos | `Launcher.exe`, `launcher_config.json`, `version.txt`, `README.txt`, `CREDITS.txt` |

**NO va (material de desarrollo):**

- `PBS/` — fuente legible de todos tus datos (el juego corre de los `.dat`/`.rxdata`).
- `Plugins/` — código de los plugins; el juego usa `Data/PluginScripts.rxdata`.
- `Tilesets Gen4/` — dump de arte, no lo lee el juego.
- `Launcher/`, `tools/`, `release-assets/`, `build/`, `RELEASING.md`.
- `Game.rxproj`, `.vscode/`, `rubocop.yml`, `.editorconfig`, `.nomedia`.
- `Data/Scripts.rxdata.bak_*`, `Data/ScriptsBackup.rxdata`.

`tools\build_release.ps1` tiene una comprobación final que **falla** si algo de
la segunda lista aparece en el paquete.

---

## 5. Partidas guardadas

mkxp-z las guarda en `%AppData%\Roaming\Pokemon Ultra Yea` (por el
`dataPathApp` de [mkxp.json](mkxp.json)), **fuera** de la carpeta del juego. El
launcher nunca toca esa carpeta, así que actualizar no borra partidas. Tampoco
sobrescribe `version.txt` ni `launcher_config.json` del jugador.

---

## 6. `manifest.json` (referencia)

```json
{
  "version": "1.2.2.0",
  "released": "2026-09-01",
  "mandatory": false,
  "notes": "Texto del changelog.",
  "package": {
    "asset": "UltraYea-1.2.2.0.zip",
    "sha256": "…",
    "size": 268435456
  },
  "delete": [
    "Graphics/Characters/sprite_viejo.png"
  ],
  "launcherVersion": "1.1.0",
  "files": {
    "asset": "files.json",
    "sha256": "…",
    "size": 1300000
  },
  "delta": {
    "fromVersion": "1.2.1.0",
    "asset": "UltraYea-1.2.2.0-delta.zip",
    "sha256": "…",
    "size": 8400000
  }
}
```

- `delete` — rutas relativas a borrar en el PC del jugador (archivos que ya no
  existen en la versión nueva). El script lo deja vacío; rellénalo a mano si
  hace falta. El launcher ignora rutas que intenten salir de la carpeta del juego.
  (Los borrados del **delta** se calculan solos al comparar con la versión anterior.)
- `launcherVersion` — versión de producto de `Launcher.exe` (del `<Version>` del
  csproj). El build la usa para decidir si `Launcher.exe` entra en el delta (ver
  [§ 8](#launcherexe-en-el-delta)).
- `files` — apunta a `files.json`, que lleva el hash SHA-256 y el tamaño de cada
  archivo del paquete. Lo usa el launcher para verificar la instalación tras un
  delta. Lo genera el script; no lo toques.
- `delta` — paquete diferencial. `fromVersion` es la **base** desde la que se
  genera. El launcher usa el delta si la versión local del jugador es **igual o
  posterior** a `fromVersion` (porque el delta acumula todos los cambios desde
  esa base). Si la versión local es más antigua que `fromVersion`, o si falta el
  delta, baja el zip completo.
- Si no subes `manifest.json`, el launcher usa la etiqueta de la Release como
  versión y el primer `.zip` como paquete (sin verificación SHA-256, sin delta).

---

## 7. Opcional: control de versiones del proyecto

Ahora mismo no hay repositorio git del proyecto en sí. Si quieres histórico:

```powershell
git init
git add .
git commit -m "Estado inicial de Pokémon Ultra Yea"
```

El [.gitignore](.gitignore) ya excluye `build/`, `Launcher/obj|bin|dist`,
`Data/PluginScripts.rxdata`, backups y logs. Para no engordar el repo con
`Graphics/` y `Audio/` (≈460 MB), plantéate
[git-lfs](https://git-lfs.com/) para `*.png`, `*.ogg`, `*.mp3`.

---

## 8. Actualizaciones delta (diferenciales)

En vez de bajar el `.zip` completo (~424 MB) en cada actualización, el launcher
puede bajar **solo los archivos que cambiaron**. Un update típico pasa de
~424 MB a unos pocos MB.

### Cómo funciona

Cada Release lleva **tres** assets además del zip completo:

| Asset | Para qué |
|---|---|
| `UltraYea-<ver>.zip` | instalación desde cero y *plan B* si el delta no encaja |
| `files.json` | hash SHA-256 + tamaño de **cada** archivo de esa versión |
| `UltraYea-<ver>-delta.zip` | **solo** los archivos que cambiaron respecto a la versión anterior |

El launcher, al arrancar:

1. Lee `manifest.json`. Si hay bloque `delta` y tu `version.txt` local es
   **igual o posterior** a `delta.fromVersion`, usa la vía diferencial.
2. Baja `files.json` + `UltraYea-<ver>-delta.zip` (unos MB), los verifica por
   SHA-256 y descomprime el delta encima de la carpeta del juego.
3. **Comprueba** que todos los archivos de `files.json` están en disco con el
   hash correcto. Si algo falta o no cuadra (archivo tocado a mano, disco
   corrupto, delta incompleto) **aborta y baja el zip completo**. Nunca deja el
   juego a medio parchear.
4. Borra lo que ya no existe en la versión nueva (sale de comparar `files.json`).

El delta es **acumulativo**: se genera desde una versión base y contiene todos
los cambios desde esa base hasta la actual. Por eso sirve para **cualquier**
versión instalada **≥ base**, no solo para la versión exacta. Solo si vienes de
una versión **más antigua que la base** (o el delta falla) se baja el zip
completo, como siempre.

> El launcher diferencial tiene que llegar primero por una actualización
> normal. Los jugadores con un launcher viejo ignoran el bloque `delta` y bajan
> el zip completo (que ya trae el launcher nuevo). Del **siguiente** update en
> adelante, ya van por delta.

### El primer delta

`build_release.ps1` necesita un `files.json` con el que comparar para calcular
el delta. De más a menos cómodo:

```powershell
# a) delta ACUMULADO contra una base fija (recomendado: sirve a cualquier versión >= base)
.\tools\build_release.ps1 -Version 0.3.2 -NotesFile .\notes.md -DeltaBase .\build\files-base.json

# b) automático: baja el files.json de la última Release con gh (si existe)
.\tools\build_release.ps1 -Version 0.3.2 -NotesFile .\notes.md

# c) contra una carpeta ya empaquetada de la versión anterior
.\tools\build_release.ps1 -Version 0.3.2 -NotesFile .\notes.md -PrevDir .\build\UltraYea-0.3.1

# d) contra un files.json que te guardaste
.\tools\build_release.ps1 -Version 0.3.2 -NotesFile .\notes.md -PrevFilesJson .\build\files-0.3.1.json

# sin delta (solo zip completo + files.json)
.\tools\build_release.ps1 -Version 0.3.2 -NotesFile .\notes.md -NoDelta
```

Como las Releases actuales (≤ 0.3.1) **no** tienen `files.json`, para el primer
delta usa la opción **(c)** apuntando a `build\UltraYea-0.3.1\` (la carpeta que
dejó el build de esa versión) y **guárdalo** como `files-base.json` (para poder
usar `-DeltaBase` en releases futuras). A partir de ahí, la opción (b) ya
funciona sola.

Si no puede calcular el delta, el script avisa y publica solo el zip completo +
`files.json` — no rompe nada, solo que ese update no será diferencial.

### `Launcher.exe` en el delta

`Launcher.exe` pesa 138 MB y **cambia de hash en cada compilación** aunque no
toques su código (el PE lleva un GUID/timestamp nuevo). Para que no engorde
todos los deltas:

- El launcher tiene una versión de producto en
  [Launcher/UltraYeaLauncher.csproj](Launcher/UltraYeaLauncher.csproj)
  (`<Version>`). El build la guarda en `manifest.json` como `launcherVersion`.
- Si esa versión **no cambió** respecto a la Release anterior (o respecto a la
  base, cuando usas `-DeltaBase` con su `manifest-base.json`), el build deja
  `Launcher.exe` **fuera** del delta zip. El jugador se queda con su launcher
  (equivalente) y el delta baja a unos KB.
- **Cuando cambies código del launcher, sube `<Version>`** (p. ej. `1.1.0` →
  `1.2.0`). Así ese `Launcher.exe` sí entra en el siguiente delta y llega a
  todos. Si se te olvida, igualmente llega en la próxima descarga completa.
- El propio launcher nunca verifica `Launcher.exe` al aplicar un delta (se
  auto-actualiza aparte), así que dejarlo fuera no rompe la comprobación de
  integridad.
