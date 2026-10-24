# TODO — what is left (state at v0.2.0)

Temporary planning note; delete it whenever it stops being useful.

## To make the game finishable
- **Konngara** (stage 20, Jigoku route): not ported. Reference: ReC98 `th01/main/boss/b20j.cpp`. Uses lasers and pellets; music `ALICE.MDT`.
- **Sariel** (stage 20, Makai route): not ported, the biggest boss (`b20m.cpp`). Second form music `SYUGEN.MDT` (`BgmTracks.SarielSecondForm`); uses SE 10.
- Once both exist, the ending flow (`GameFlow.StartEnding`: ending pictures, verdict screen, then name entry and ranking) becomes reachable by playing. Today it is only reachable with `--cutscene 4..7`.

## Boss details still missing
- **Elis**: attack cels (`BOSS5_2.BOS`), the entrance with the spheres, the slits, stars and circles are not drawn yet.
- **Kikuri**: floor waves (`TAMAYEN.PTN`) and entrance rings are not drawn; `RingsFrames = 100` is a guess.
- **Mima**: pillars and missile sprites (`BOSS3_M.PTN`) are provisional.
- Defeat animation length of Mima, Kikuri, Elis is borrowed from other bosses.
- Elis assumes the original evaluates the two arguments of `wave_teleport(random_left, random_top)` right to left (2 random draws per frame); only an oracle comparison can confirm it.

## Text and cutscenes
- Verdict screen: the rating title ("you are ...", a 3 x 6 table chosen by score) is not shown; the table was not found in the translated executable.
- Captions appear all at once; the original types them one character at a time (delay not found).
- `EndingOne` (ED1A..E) has no text linked to its pictures: the spoken dialogue lives in separate functions without picture references.
- Caption fade speed is assumed (N ticks per step x 16 steps).
- "Highest score" on the verdict screen is `max(score, best ranking entry)`; the original takes it from elsewhere.
- Name entry only offers ASCII; offering kana would need a change of `NameChars`.

## Audio
- Tempo formula is exact in units (quarter notes per minute) but the driver's real timer is unconfirmed; FM operator order, octaves and drum sounds are assumptions (the user judged it "good" by ear).
- SSG presets and LFO commands (`ED`, SSG envelope) are unknown; sound effects use only the notes.
- Music room ("Music Test" in the options menu) has its model (`MusicRoom`) but no screen yet.
- SE 10 (Sariel) and SE 13 (unused in the original) are not called.
- `MonoGameAudioOutput` latency is ~93 ms (4 blocks of 1024 frames).

## Parity with the original (needs the DOSBox-X oracle)
- Boss fade length 225 vs 239 ticks, `EntranceTicks = 70`, HARRY UP banner: proposals only, not applied.
- Captures of SinGyoku and Mima fight (script 07) failed because the emulator starts on the debug memory screen; needs a reliable way to reach the stage.
- One known divergence: `rem_bombs` on the last life (script 06).
- Elis, Kikuri, Mima patterns have never been compared with the original.
- Game over has a gap of ~47 vsyncs in the original that is not modelled.

## Platform and release
- Photosensitivity warning measured? The flashing frequency was not measured against the 3-flashes-per-second guideline.
- Fullscreen uses the "Fit" scale (fills the height; pixels are slightly uneven at non-integer scales). F10 cycles Integer / Fit / Stretch.
- Release v0.1.0 has no sprites and should be marked as outdated or removed.
- Releases are built by hand: `dotnet publish src/Th01.Platform.MonoGame -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`, then zipped with `SDL2.dll`, `openal.dll`, `LICENSE`, `THIRD-PARTY-NOTICES.txt`, `README.md`. A GitHub Actions workflow would avoid doing it manually.
- Documents the code refers to (`docs/FORMATS.md`, `docs/RECON.md`, `docs/ORACLE.md`, `docs/TASKS.md`) are not in the repository.
