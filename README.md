# Touhou Reiiden — fan port (東方靈異伝 ~ The Highly Responsive to Prayers)

> **Unofficial fan work. Not affiliated with or endorsed by ZUN / Team Shanghai Alice.**
> Touhou Project and *Touhou Reiiden* belong to their respective owners.
> **You need your own legal copy of the original PC-98 game.** This repository and its releases contain none of the game's data.
> Non-commercial; no monetization.

An engine-free C# (.NET 8) re-implementation of the 1997 PC-98 game. The community decompilation
[ReC98](https://github.com/nmlgc/ReC98) is used only as a behavioural reference; no ReC98 code is included.

**Status: v0.3.0, playable up to stage 15.** Logo, title/menu (rebuilt after the original), the card stages, stage bonus, game over, ranking (saved to disk),
Japanese text, music and sound effects (synthesized from your own game files), and the bosses SinGyoku (stage 5), YuugenMagan and Mima (stage 10)
and Kikuri and Elis (stage 15) work. The ending pictures, captions and the final verdict screen exist (`--cutscene 4..7`), but
**the stage 20 bosses (Konngara, Sariel) are not done yet, so the game cannot be finished.** Sound and some boss details are still being
checked against the original; expect differences.

**Photosensitivity warning:** the game contains flashing lights and intense visual effects.

## Play

1. Download the latest zip from **Releases** and unzip it anywhere (keep `ReiidenPort.exe`, `SDL2.dll` and `openal.dll` together).
2. Run `ReiidenPort.exe`. The first time, it asks for your game file: pick your **`.hdi` disk image** of Touhou Reiiden (Anex86 format, FAT12).
   The game files are copied into `%LOCALAPPDATA%\ReiidenPort\data` on your own machine.
3. Your choice is remembered; later runs open the game directly. To choose another file, hold **Shift** while starting, or run `ReiidenPort.exe --select`.

Controls: arrows move, `Z` / `Space` shoot, `X` bomb, `P` pause, `F11` fullscreen, `F10` scale mode, `Esc` quit.
Remap keys with `input.local.json` in `%LOCALAPPDATA%\ReiidenPort`.

Command line: `--select`, `--game <hdi|folder>`, `--data <folder>`, `--stage 1..20`, `--route 0|1`, `--cutscene 1..7`. Set `REIIDENPORT_HOME` to keep all data elsewhere.

Windows x64 only for now. If the window does not open, update your graphics driver (OpenGL 3.0+ is required); errors are written to `crash.local.log` in the data folder.

## Build

Requires the .NET 8 SDK.

```text
dotnet build
dotnet test
dotnet run --project src/Th01.Platform.MonoGame
dotnet run --project src/Th01.Tools -- inspect-assets "<folder with your game files>"
```

Projects: `Th01.Core` (deterministic game logic), `Th01.Assets` (format readers), `Th01.Platform.MonoGame` (window, input, launcher),
`Th01.Tools` (CLI). Tests that need the real game files skip themselves unless `TH01_DATA_PATH` points at them.

## Credits

- **[ReC98](https://github.com/nmlgc/ReC98)** by [nmlgc](https://github.com/nmlgc) and contributors: this port exists because of that decompilation and
  reverse-engineering work. It is the reference for the game's behaviour and file formats (the `ref:` comments in the code point to the
  corresponding ReC98 files). Thank you, and thank you for permitting this project to be published.
- **ZUN / Team Shanghai Alice**: creator of Touhou Reiiden. Touhou Project belongs to them.

## AI assistance

This port is developed with the help of AI coding tools (Claude Code, by Anthropic), directed and reviewed by the maintainer.

## License

Code: MIT (see `LICENSE`). Third-party components keep their own licenses (`THIRD-PARTY-NOTICES.txt`).
The original game is not covered and is not included.
