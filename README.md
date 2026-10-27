# Touhou Reiiden — fan port (東方靈異伝 ~ The Highly Responsive to Prayers)

> **Unofficial fan work. Not affiliated with or endorsed by ZUN / Team Shanghai Alice.**
> Touhou Project and *Touhou Reiiden* belong to their respective owners.
> **You need your own legal copy of the original PC-98 game.** This repository and its releases contain none of the game's data.
> Non-commercial; no monetization.

An engine-free C# (.NET 8) re-implementation of the 1997 PC-98 game. The community decompilation
[ReC98](https://github.com/nmlgc/ReC98) is used only as a behavioural reference; no ReC98 code is included.

**Status: v0.6.0, the Jigoku route can be played to the end, with a launcher and the boss drawings filled in.** Launcher (English and Japanese, options, mouse support), logo, title/menu (rebuilt after the original), the card stages, stage bonus,
game over, ranking (saved to disk), Japanese text, music and sound effects (synthesized from your own game files), and the bosses
SinGyoku (stage 5), YuugenMagan and Mima (stage 10), Kikuri and Elis (stage 15) and Konngara (stage 20, Jigoku route) work, followed by the
ending pictures, captions and the final verdict screen (`--cutscene 4..7`). **Sariel (stage 20, Makai route) is not done yet, so the Makai route
still stops before the last boss.** Konngara is new and untested against the original: some of its drawing and timings are estimates, and sound and some
boss details are still being checked against the original; expect differences.

**Photosensitivity warning:** the game contains flashing lights and intense visual effects.

## Play

1. Download the latest zip from **Releases** and unzip it anywhere (keep `ReiidenPort.exe`, `SDL2.dll` and `openal.dll` together).
2. Run `ReiidenPort.exe`. It opens the launcher. The first time, pick your game with **Select game...**: your **`.hdi` disk image** of Touhou Reiiden (Anex86 format, FAT12) or a folder with the game files.
   The game files are copied into `%LOCALAPPDATA%\ReiidenPort\data` on your own machine.
3. Your choice is remembered. Press **Play** to start; change the game any time with **Change...** in the launcher (or run `ReiidenPort.exe --select`). The launcher has options for accessibility (softer flashes, less shaking), video (fullscreen, window size, integer scale, image filter), audio volumes, controls, and the text font; it can use your own `FONT.ROM` image for the original PC-98 look.

Controls: arrows move, `Z` / `Space` shoot, `X` bomb, `P` pause, `F11` fullscreen, `F10` scale mode, `Esc` quit.
Keys and gamepad buttons can be remapped in the launcher (stored in `input.local.json` in `%LOCALAPPDATA%\ReiidenPort`).

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
