// Oracle capture driver: runs the ORIGINAL REIIDEN.EXE (from a COPY of the user's .hdi) in DOSBox-X with the
// in-guest logger (tsr.js) and writes a per-tick CSV trace. Traces never go in the repo (use game-data/traces/).
//
//   node capture.js <input-script> <out.csv> [--root <game-data dir>] [--hdi <th01.hdi>] [--frame-rand N] [--seed N]
//
// Layout facts (DS offsets of REIIDEN.EXE, Japanese build; derived and verified in docs/ORACLE.md section 8).
'use strict';
const fs = require('fs'), path = require('path');
const { Emu } = require('./emu.js');
const { assemble, buildCfg, CFG } = require('./tsr.js');

// ---- DS offsets ---------------------------------------------------------------------------------
const V = {
  rank: 0x90, rem_bombs: 0x92, score: 0xba, orb_velocity_x: 0xdc, orb_rotation_frame: 0xde, rem_lives: 0xe0,
  orb_prev_x: 0xee, orb_prev_y: 0xf0, orb_vy: 0xf4, orb_force: 0xfc, random_seed: 0xbfa,
  frame_rand: 0x2276, frame_since_start: 0x2280, player_invincibility_time: 0x2284, player_left: 0x2286,
  orb_x: 0x2288, orb_y: 0x228a, orb_force_frame: 0x228c,
  z_vsync_count2: 0x3e74, player_invincible: 0xd8, bomb_frame: 0xc2, cardcombo_cur: 0xe4, cardcombo_max: 0xe8,
};
// Snapshot ranges (each even length; sum multiple of 16). Slot layout is derived from this list.
const RANGES = [[0x90, 0x80], [0xbf0, 0x10], [0x2270, 0x20], [0x3e70, 0x10]];
const SIG_OFF = 0x1e4, SIG = Buffer.from('826f8260', 'hex'); // PAUSE_TITLE in REIIDEN only (not in OP/FUUIN)

// ---- GameButtons -> PC-98 key groups (platform/x86real/pc98/keyboard.hpp) ------------------------
const BTN = { Up: 1, Down: 2, Left: 4, Right: 8, Shot: 16, Bomb: 32, Pause: 64, Ok: 128, Cancel: 256 };
function groupsFor(bits) {
  const g = new Array(10).fill(0);
  if (bits & BTN.Up) g[7] |= 0x04; if (bits & BTN.Left) g[7] |= 0x08; if (bits & BTN.Right) g[7] |= 0x10;
  if (bits & BTN.Down) g[7] |= 0x20; if (bits & BTN.Shot) g[5] |= 0x02; if (bits & BTN.Bomb) g[5] |= 0x04;
  if (bits & (BTN.Pause | BTN.Cancel)) g[0] |= 0x01; if (bits & BTN.Ok) g[3] |= 0x10;
  return g;
}

function parseScript(text) {                      // same format as Th01.Tools InputScript
  let seed = 1, ticks = 0; const ch = [];
  for (let line of text.split(/\r?\n/)) {
    line = line.replace(/#.*/, '').trim(); if (!line) continue;
    const p = line.split(/\s+/);
    if (p[0].toLowerCase() === 'seed') seed = Number(p[1]) >>> 0;
    else if (p[0].toLowerCase() === 'ticks') ticks = Number(p[1]);
    else {
      let bits = 0;
      for (const n of p.slice(1)) { if (n.toLowerCase() === 'none') continue; const k = Object.keys(BTN).find(x => x.toLowerCase() === n.toLowerCase()); if (!k) throw new Error('unknown button ' + n); bits |= BTN[k]; }
      ch.push([Number(p[0]), bits]);
    }
  }
  if (!ticks) throw new Error("script needs 'ticks N'");
  const at = t => { let b = 0; for (const [tt, bb] of ch) { if (tt > t) break; b = bb; } return b; };
  return { seed, ticks, at };
}

function decodeSlot(s) {                          // s: Buffer of one slot (ranges concatenated)
  const at = (o, n) => { let acc = 0; for (const [b, l] of RANGES) { if (o >= b && o + n <= b + l) return acc + (o - b); acc += l; } throw new Error("offset not captured: " + o.toString(16)); };
  const u16 = o => s.readUInt16LE(at(o, 2)), i16 = o => s.readInt16LE(at(o, 2)), u32 = o => s.readUInt32LE(at(o, 4));
  const dbl = o => s.readBigInt64LE(at(o, 8)), dblv = o => s.readDoubleLE(at(o, 8));
  const VX = [0, -4, 4, -8, 8];                  // orb_velocity_x_t -> pixels per tick (port convention)
  const vy = dblv(V.orb_vy), force = dblv(V.orb_force);
  return {
    player_x: i16(V.player_left), orb_x: i16(V.orb_x), orb_y: i16(V.orb_y), orb_vx: VX[i16(V.orb_velocity_x)] ?? ("enum" + i16(V.orb_velocity_x)),
    orb_vy: Math.trunc(vy * 256), orb_vy_dbl: vy, orb_vy_bits: dbl(V.orb_vy),          // Q8 truncated toward zero, like the port
    score: u32(V.score), rng_seed: u32(V.random_seed),
    orb_force: Math.trunc(force * 256), orb_force_dbl: force, orb_force_bits: dbl(V.orb_force), orb_force_frame: i16(V.orb_force_frame),
    orb_rotation_frame: i16(V.orb_rotation_frame), orb_prev_x: i16(V.orb_prev_x), orb_prev_y: i16(V.orb_prev_y),
    rem_lives: i16(V.rem_lives), rem_bombs: s.readInt8(at(V.rem_bombs, 1)), frame_rand: u32(V.frame_rand),
    player_invincible: u16(V.player_invincible) ? 1 : 0, invincibility_time: i16(V.player_invincibility_time),
    z_vsync_count2: u16(V.z_vsync_count2), frame_since_start: u32(V.frame_since_start),
    bomb_frame: u32(V.bomb_frame), cardcombo_cur: i16(V.cardcombo_cur), cardcombo_max: i16(V.cardcombo_max),
  };
}

const COLS = ["tick", "input_bits", "player_x", "player_y", "orb_x", "orb_y", "orb_vx", "orb_vy", "orb_vy_dbl", "orb_vy_bits", "score", "rng_seed",
  "orb_force", "orb_force_dbl", "orb_force_bits", "orb_force_frame", "orb_rotation_frame", "orb_prev_x", "orb_prev_y", "rem_lives", "rem_bombs",
  "frame_rand", "player_invincible", "invincibility_time", "bomb_frame", "cardcombo_cur", "cardcombo_max", "z_vsync_count2", "frame_since_start"];

async function capture(opts) {
  const root = opts.root, work = path.join(root, 'work'), out = path.join(work, 'run');
  fs.mkdirSync(out, { recursive: true });
  const script = parseScript(fs.readFileSync(opts.script, 'utf8'));
  // fresh COPY of the disk every run (the game writes REIIDEN.CFG / score files)
  fs.copyFileSync(opts.hdi, path.join(work, 'run.hdi'));
  const inputs = Buffer.alloc(script.ticks * 10);
  for (let t = 0; t < script.ticks; t++) groupsFor(script.at(t)).forEach((v, g) => { inputs[t * 10 + g] = v; });
  fs.writeFileSync(path.join(out, 'TRACE.COM'), assemble());
  const { cfg, slotParas, bufDelta } = buildCfg({
    cap: script.ticks, ranges: RANGES, gameSigOff: SIG_OFF, gameSig: SIG, inputs,
    frameRandOff: V.frame_rand, frameRand: opts.frameRand ?? script.seed, seedOff: V.random_seed, seed: script.seed });
  fs.writeFileSync(path.join(out, 'CFG.BIN'), cfg);
  const conf = path.join(work, 'run.conf');
  fs.writeFileSync(conf, `[dosbox]\nmachine=pc98\nmcp_server=7777\n[cpu]\ncycles=fixed 100000\n[autoexec]\nmount D run\nD:\nTRACE.COM\nimgmount C run.hdi -t hdd\nC:\nmdrv98 -M7\nop\n`);
  const e = new Emu({ dosbox: path.join(root, 'tools/dosbox-x/bin/x64/Release/dosbox-x.exe'), work, conf: 'run.conf' });
  const t0 = Date.now();
  await e.start();
  try {
    await e.sleep(14000); await e.pushKey(0x1c0d);          // title: "HIT KEY"
    await e.sleep(3500); await e.tap({ 5: 0x02 });           // menu: START (not captured: before tick 0)
    const ptr = await e.mem(0, 0x200, 4); const seg = ptr.readUInt16LE(2);
    let hdr, tTick0 = 0;
    for (let i = 0; i < 120; i++) {                           // wait for the game, then for the script to finish
      await e.sleep(500); hdr = await e.mem(seg, CFG.base, 0x40);
      if (hdr[CFG.started] && !tTick0) tTick0 = Date.now();
      if (hdr.readUInt16LE(CFG.tick) >= script.ticks) break;
    }
    const tick = hdr.readUInt16LE(CFG.tick);
    if (tick < script.ticks) throw new Error(`capture incomplete: tick ${tick} < ${script.ticks}`);
    const tEnd = Date.now();
    const slotBytes = slotParas * 16, rows = [];
    // read in chunks (MEMDUMPBIN wraps at 64 KB offsets) -> one dump per segment window
    const per = Math.floor(0x10000 / slotBytes);
    for (let t0r = 0; t0r < script.ticks; t0r += per) {
      const n = Math.min(per, script.ticks - t0r);
      const b = await e.mem(seg + bufDelta + t0r * slotParas, 0, n * slotBytes);
      for (let i = 0; i < n; i++) rows.push(b.subarray(i * slotBytes, (i + 1) * slotBytes));
    }
    const lines = [`# source=original`, `# build=th01.hdi REIIDEN.EXE (japanese)`, `# seed=${script.seed}`, `# frame_rand=${opts.frameRand ?? script.seed}`,
      `# capture_ms=${tEnd - tTick0}`, COLS.join(',')];
    rows.forEach((s, t) => {
      const v = decodeSlot(s);
      lines.push(COLS.map(c => c === 'tick' ? t : c === 'input_bits' ? script.at(t) : c === 'player_y' ? '' : String(v[c])).join(','));
    });
    fs.mkdirSync(path.dirname(opts.out), { recursive: true });
    fs.writeFileSync(opts.out, lines.join('\n') + '\n');
    return { ticks: script.ticks, ms: tEnd - tTick0, total: Date.now() - t0 };
  } finally { e.stop(); }
}

module.exports = { capture, V, RANGES, COLS, parseScript, decodeSlot, groupsFor };

if (require.main === module) {
  const a = process.argv.slice(2); const opt = (n, d) => { const i = a.indexOf(n); return i >= 0 ? a[i + 1] : d; };
  const root = path.resolve(opt('--root', path.join(__dirname, '../../../../../game-data')));
  const o = { script: a[0], out: a[1], root, hdi: opt('--hdi', path.join(root, 'work/th01.hdi')), frameRand: opt('--frame-rand', null) === null ? undefined : Number(opt('--frame-rand')) >>> 0 };
  if (!o.script || !o.out) { console.error('usage: node capture.js <input-script> <out.csv> [--root dir] [--hdi file] [--frame-rand N]'); process.exit(2); }
  capture(o).then(r => { console.log(`captured ${r.ticks} ticks in ${r.ms} ms (${(r.ticks / (r.ms / 1000)).toFixed(1)} ticks/s wall-clock; whole run ${r.total} ms)`); process.exit(0); },
    err => { console.error('ERROR', err.message); process.exit(1); });
}
