namespace Th01.Audio.Mdrv;

/// <summary>
/// Interprets the command bytes of one MDRV2 track/effect stream (format as observed in the game's own files, and cross-checked
/// with the public descriptions of the MDRV2 format: the Lua MDT-to-MML converter by HertzDevil and the MDT Parsing Tools).
/// Lengths are in ticks, 48 per quarter note (192 per whole note); the tempo byte is quarter notes per minute.
///   00..8F nn   note (high nibble = octave, low nibble = 0..B) + length       90 nn  rest       91  tie with the next note
///   E0 n .. E1 .. E2   repeat n times; E1 is skipped on the last pass      E3  last-pass exit of an E4 loop (no parameters; unused here)
///   E4 n .. E5         repeat n times      F6 n lo hi .. F7 x lo hi  same with 16-bit offsets      F9 lo hi  last-pass exit of F6
///   E6 n  detune       E7 n  gate cut (ticks)     EA n  gate Q: a note sounds n/8 of its length (8 = full)
///   E9 n  tempo        EB n  voice (FM) / SSG: bit0 noise, bit1 tone / drum id     EC n  volume (drum track: 2 or 7 bytes)
///   EF n  noise period F1 n  pan (always 3 = both sides, ignored)     F2 n l lo hi  note with a pitch sweep
///   ED, E8, FB, FC, FD  four parameters each, low-frequency oscillators (ignored)       EE (2), F0, F4, F5, F8 (1)  ignored
///   FA lo hi  call the pattern at that absolute file offset (it ends with FF; drum phrases live in the title area)
///   F3 lo hi  end of the track, jump to (address of F3) + 2 + signed offset (the loop point)      FF  end of the stream
/// ASSUMPTION: the meaning of the commands marked "ignored" is not known to this port; they are skipped by length and counted
/// in <see cref="DecodedTrack.Ignored"/>.
/// </summary>
public sealed class TrackDecoder
{
    public const int TicksPerWhole = 192;
    const int MaxSteps = 400_000, MaxTicks = TicksPerWhole * 4 * 600;

    readonly byte[] _d;
    readonly int _end;
    readonly ChannelKind _kind;
    readonly List<NoteEvent> _notes = [];
    readonly List<TempoChange> _tempos = [];
    readonly Dictionary<int, int> _ignored = [];
    readonly Stack<(int Start, int Left, bool Volta)> _loops = new();
    readonly Stack<int> _calls = new();
    const int MaxCallDepth = 4;

    readonly Dictionary<int, int> _tickAt = [];
    int _loopTarget = -1;
    int _quantize = 8;
    int _pos, _tick, _voice, _volume, _detune, _gateCut, _noisePeriod, _drum;
    bool _tone = true, _noise, _tie, _loopEnd;
    NoteEvent? _last;

    TrackDecoder(byte[] data, int start, int end, ChannelKind kind)
    {
        _d = data; _pos = start; _end = Math.Min(end, data.Length); _kind = kind;
        _volume = kind == ChannelKind.Fm ? 0x60 : 0x0C;
    }

    public static DecodedTrack Decode(byte[] data, int start, int length, int trackId, ChannelKind kind)
    {
        var t = new TrackDecoder(data, start, start + length, kind);
        t.Run();
        return new DecodedTrack
        {
            TrackId = trackId, Kind = kind, Notes = t._notes, Tempos = t._tempos, TotalTicks = t._tick,
            Loops = t._loopEnd, Ignored = t._ignored,
            LoopStartTick = t._loopTarget >= 0 && t._tickAt.TryGetValue(t._loopTarget, out int lt) ? lt : -1,
        };
    }

    static int Params(int op, ChannelKind kind, int next) => op switch
    {
        0xE0 or 0xE4 or 0xE6 or 0xE7 or 0xE9 or 0xEA or 0xEB or 0xEF or 0xF0 or 0xF1 or 0xF4 or 0xF5 or 0xF8 => 1,
        0xEC => kind == ChannelKind.Drum ? (next >= 0x80 ? 2 : 7) : 1,
        0xEE => 2,
        0xE8 or 0xED or 0xF2 or 0xFB or 0xFC or 0xFD => 4,
        0xF3 or 0xF9 or 0xFA => 2,
        0xF6 or 0xF7 => 3,
        _ => 0,
    };

    // A pattern called with FA can lie outside the track (it is addressed from the start of the file).
    int Limit => _calls.Count > 0 ? _d.Length : _end;

    byte Read() => _pos < Limit ? _d[_pos++] : (byte)0xFF;

    void Run()
    {
        int skipDepth = -1; // >= 0 while skipping the volta part up to the matching E2
        for (int steps = 0; steps < MaxSteps && _pos < Limit && _tick < MaxTicks; steps++)
        {
            int opPos = _pos;
            _tickAt.TryAdd(opPos, _tick);
            int op = Read();
            if (op == 0xFF)
            {
                if (_calls.Count == 0) return;
                _pos = _calls.Pop();   // end of a pattern: back to the caller
                continue;
            }
            if (op < 0x90)
            {
                int len = Read();
                if (skipDepth < 0) Note(op, len);
                continue;
            }
            if (op == 0x90) { int len = Read(); if (skipDepth < 0) { _tick += len; _tie = false; } continue; }
            if (op == 0x91) { _tie = true; continue; }
            if (op < 0xE0) continue; // 92..DF: unknown, no parameters assumed

            int next = _pos < Limit ? _d[_pos] : 0;
            int n = Params(op, _kind, next);
            int p0 = n > 0 ? Read() : 0;
            int p1 = n > 1 ? Read() : 0;
            int p2 = n > 2 ? Read() : 0;
            int p3 = n > 3 ? Read() : 0;
            for (int i = 4; i < n; i++) Read();

            if (skipDepth >= 0)
            {
                if (op == 0xE0) skipDepth++;
                else if (op == 0xE2) { if (skipDepth == 0) { skipDepth = -1; _loops.Pop(); } else skipDepth--; }
                continue;
            }

            switch (op)
            {
                case 0xE0: _loops.Push((_pos, Math.Max(1, p0), true)); break;
                case 0xE4:
                case 0xF6: _loops.Push((_pos, Math.Max(1, p0), false)); break;
                case 0xE1:
                    if (_loops.Count > 0 && _loops.Peek() is { Volta: true, Left: 1 }) skipDepth = 0;
                    break;
                case 0xE2:
                case 0xE5:
                case 0xF7:
                    if (_loops.Count == 0) break;
                    var top = _loops.Pop();
                    if (top.Left > 1) { _loops.Push((top.Start, top.Left - 1, top.Volta)); _pos = top.Start; }
                    break;
                case 0xE6: _detune = (sbyte)p0; break;
                case 0xE7: _gateCut = p0; break;
                case 0xEA: _quantize = Math.Clamp(p0, 1, 8); break;
                case 0xE9: _tempos.Add(new TempoChange(_tick, p0)); break;
                case 0xEB:
                    if (_kind == ChannelKind.Fm) _voice = p0;
                    else if (_kind == ChannelKind.Drum) _drum = p0;
                    else { _noise = (p0 & 1) != 0; _tone = (p0 & 2) != 0 || !_noise; }
                    break;
                case 0xEC: if (_kind != ChannelKind.Drum) _volume = p0; break;
                case 0xEF: _noisePeriod = p0; break;
                case 0xF2: Sweep(p0, p1, (short)(p2 | (p3 << 8))); break;
                case 0xFA:   // pattern call: the 16-bit operand is an absolute file offset; the pattern ends with FF
                    if (_calls.Count < MaxCallDepth && (p0 | (p1 << 8)) is var pattern && pattern > 0 && pattern < _d.Length)
                    {
                        _calls.Push(_pos);
                        _pos = pattern;
                    }
                    break;
                case 0xF9:   // last pass of the enclosing repeat: jump (hi byte address + offset), skipping the rest of the body
                    if (_loops.Count > 0 && _loops.Peek().Left <= 1)
                    {
                        int target = _pos - 1 + (short)(p0 | (p1 << 8));
                        if (target > _pos && target < _end) _pos = target;
                    }
                    break;
                case 0xF3: _loopEnd = true; _loopTarget = opPos + 2 + (short)(p0 | (p1 << 8)); return;
                default: _ignored[op] = _ignored.GetValueOrDefault(op) + 1; break;
            }
        }
    }

    void Note(int op, int len)
    {
        int semitone = (op >> 4) * 12 + Math.Min(op & 0xF, 11);
        if (_tie && _last is { } prev && prev.Semitone == semitone && prev.SweepDelta == 0)
        {
            // Same pitch tied: extend the previous note instead of starting a new one.
            _last = prev with { DurationTicks = _tick + Math.Max(1, len * _quantize / 8 - _gateCut) - prev.StartTick };
            _notes[^1] = _last;
        }
        else
        {
            _last = Make(semitone, len, 0);
            _notes.Add(_last);
        }
        _tie = false;
        _tick += len;
    }

    void Sweep(int note, int len, int delta)
    {
        var ev = Make(Math.Min(note >> 4, 8) * 12 + Math.Min(note & 0xF, 11), len, delta);
        _notes.Add(ev);
        _last = ev;
        _tick += len;
    }

    NoteEvent Make(int semitone, int len, int sweep) =>
        new(_tick, Math.Max(1, len * _quantize / 8 - _gateCut), semitone, _volume, _voice, _detune, _tone, _noise, _noisePeriod, sweep, _drum);
}
