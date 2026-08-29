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

### 1.2 Crea el repositorio de GitHub

Puede estar casi vacío (solo un README). Lo que importa son las *Releases*.

```powershell
gh auth login
gh repo create pokemon-ultra-yea --public --description "Fangame de Pokémon (Essentials + mkxp-z)"
```

> El repositorio **público** hace que los assets de las Releases se puedan
> descargar sin token. Si lo pones privado, el launcher no podrá bajar nada
> sin autenticación.

### 1.3 Configura el launcher

Edita [Launcher/launcher_config.json](Launcher/launcher_config.json):

```json
"repoOwner": "tu-usuario",
"repoName":  "pokemon-ultra-yea",
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
- crea `build\UltraYea-1.2.2.0.zip` y `build\manifest.json` (con SHA-256).

Para una actualización obligatoria: añade `-Mandatory`.

### 2.3 Prueba

Abre `build\UltraYea-1.2.2.0\Launcher.exe` y comprueba que el juego arranca.

### 2.4 Publica la Release

```powershell
gh release create v1.2.2.0 `
  "build\UltraYea-1.2.2.0.zip" `
  "build\manifest.json" `
  --title "v1.2.2.0" `
  --notes-file .\notes.md
```

Hecho. El launcher de los jugadores lo verá en el siguiente arranque.

> La etiqueta (`v1.2.2.0`) y el `-Version` del script deben coincidir. El
> launcher compara `1.2.2.0` con el `version.txt` local componente a
> componente, así que usa siempre el mismo formato numérico.

---

## 3. La primera vez que repartes el juego

Comparte el enlace del `.zip` de la primera Release, o el enlace permanente
a la última:

```
https://github.com/tu-usuario/pokemon-ultra-yea/releases/latest
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
  ]
}
```

- `delete` — rutas relativas a borrar en el PC del jugador (archivos que ya no
  existen en la versión nueva). El script lo deja vacío; rellénalo a mano si
  hace falta. El launcher ignora rutas que intenten salir de la carpeta del juego.
- Si no subes `manifest.json`, el launcher usa la etiqueta de la Release como
  versión y el primer `.zip` como paquete (sin verificación SHA-256).

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
