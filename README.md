# Pokémon Ultra Yea

Fangame de Pokémon hecho con **Pokémon Essentials v21.1** sobre **mkxp-z**.

> Proyecto de fans, **gratuito y sin ánimo de lucro**. No está afiliado ni
> respaldado por Nintendo, Game Freak o The Pokémon Company. "Pokémon" y sus
> marcas y personajes pertenecen a sus respectivos dueños. No se permite vender
> este juego ni cobrar por él.

## Descargar y jugar

1. Descarga el `.zip` de la **[última versión](../../releases/latest)**.
2. Extráelo en una carpeta con permiso de escritura (Escritorio, Descargas…).
   **No** lo pongas en `Archivos de programa`.
3. Ejecuta **`Launcher.exe`**. Comprueba si hay una versión nueva, la instala y
   abre el juego. Sin conexión: pulsa **Jugar**.

Las partidas se guardan en `%AppData%\Roaming\Pokemon Ultra Yea`, **fuera** de la
carpeta del juego: actualizar nunca borra tu progreso.

## Este repositorio

Contiene **solo el launcher y las herramientas de publicación**. El juego se
distribuye como archivos adjuntos (`assets`) de cada *Release*, no en el árbol de
archivos.

| Carpeta | Qué es |
|---|---|
| [`Launcher/`](Launcher/) | Actualizador en C# / .NET 8. Compila a un único `Launcher.exe` autocontenido. |
| [`tools/`](tools/) | Scripts que empaquetan la versión jugable y generan `manifest.json`. |
| [`RELEASING.md`](RELEASING.md) | Cómo sacar una versión nueva. |

## Compilar el launcher

```powershell
winget install Microsoft.DotNet.SDK.8   # una vez
.\Launcher\build.ps1                     # -> Launcher\dist\Launcher.exe
```

## Créditos

El motor es Pokémon Essentials (Maruno, Poccil, Flameguru y la comunidad) y
mkxp-z (Ancurio, Struma, Roza y colaboradores). Los créditos completos de
plugins y recursos se generan en `CREDITS.txt` dentro de cada descarga.
