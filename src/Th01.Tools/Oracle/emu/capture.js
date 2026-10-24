// Oracle capture driver: runs the ORIGINAL REIIDEN.EXE (from a COPY of the user's .hdi) in DOSBox-X with the
// in-guest logger (tsr.js) and writes a per-tick CSV trace. Traces never go in the repo (use game-data/traces/).
//
//   node capture.js <input-script> <out.csv> [--root <game-data dir>] [--hdi <th01.hdi>] [--frame-rand N]
//
// Script directives (comment lines, ignored by the C# parser):  #@stage N   #@route R   #@columns a,b,c
//   #@stage N  starts the game in "stage select" mode (op t: debug test mode, which asks for the stage) and types N (0-based stage id) and the route.
// Layout facts (DS offsets of REIIDEN.EXE, Japanese build; derived and verified in docs/ORACLE.md section 12).
'use strict';
const fs = require('fs'), path = require('path');
const { Emu } = require('./emu.js');
const { assemble, buildCfg, CFG } = require('./tsr.js');

// ---- fields: name -> [DS offset, size, type] --------------------------------------------------------
const F = {
  rem_bombs: [0x92, 1, 'i8'], score: [0xba, 4, 'u32'], bomb_frame: [0xc2, 4, 'u32'], player_invincible: [0xd8, 2, 'u16'],
  orb_velocity_x: [0xdc, 2, 'i16'], orb_rotation_frame: [0xde, 2, 'i16'], rem_lives: [0xe0, 2, 'i16'],
  cardcombo_cur: [0xe4, 2, 'i16'], cardcombo_max: [0xe8, 2, 'i16'], orb_prev_x: [0xee, 2, 'i16'], orb_prev_y: [0xf0, 2, 'i16'],
  orb_velocity_y: [0xf4, 8, 'f64'], orb_force_v: [0xfc, 8, 'f64'], random_seed: [0xbfa, 4, 'u32'],
  frame_rand: [0x2276, 4, 'u32'], frame_since_start: [0x2280, 4, 'u32'], invincibility_time: [0x2284, 2, 'i16'],
  player_left: [0x2286, 2, 'i16'], orb_x: [0x2288, 2, 'i16'], orb_y: [0x228a, 2, 'i16'], orb_force_frame: [0x228c, 2, 'i16'],
  z_vsync_count2: [0x3e74, 2, 'u16'], stage_timer: [0x540c, 2, 'u16'], frame_since_harryup: [0x540e, 2, 'u16'],
};
const VX = [0, -4, 4, -8, 8];
// column -> [fields needed, fn(get)]
const COLUMNS = {
  player_x: [['player_left'], g => g('player_left')],
  orb_x: [['orb_x'], g => g('orb_x')], orb_y: [['orb_y'], g => g('orb_y')],
  orb_vx: [['orb_velocity_x'], g => VX[g('orb_velocity_x')] ?? 'enum' + g('orb_velocity_x')],
  orb_vy: [['orb_velocity_y'], g => Math.trunc(g('orb_velocity_y') * 256)],
  orb_vy_dbl: [['orb_velocity_y'], g => g('orb_velocity_y')],
  orb_vy_bits: [['orb_velocity_y'], g => g.bits('orb_velocity_y')],
  orb_force: [['orb_force_v'], g => Math.trunc(g('orb_force_v') * 256)],
  orb_force_dbl: [['orb_force_v'], g => g('orb_force_v')],
  orb_force_bits: [['orb_force_v'], g => g.bits('orb_force_v')],
  orb_force_frame: [['orb_force_frame'], g => g('orb_force_frame')],
  orb_rotation_frame: [['orb_rotation_frame'], g => g('orb_rotation_frame')],
  orb_prev_x: [['orb_prev_x'], g => g('orb_prev_x')], orb_prev_y: [['orb_prev_y'], g => g('orb_prev_y')],
  score: [['score'], g => g('score')], rng_seed: [['random_seed'], g => g('random_seed')],
  rem_lives: [['rem_lives'], g => g('rem_lives')], rem_bombs: [['rem_bombs'], g => g('rem_bombs')],
  frame_rand: [['frame_rand'], g => g('frame_rand')],
  player_invincible: [['player_invincible'], g => g('player_invincible') ? 1 : 0],
  invincibility_time: [['invincibility_time'], g => g('invincibility_time')],
  bomb_frame: [['bomb_frame'], g => g('bomb_frame')],
  cardcombo_cur: [['cardcombo_cur'], g => g('cardcombo_cur')], cardcombo_max: [['cardcombo_max'], g => g('cardcombo_max')],
  stage_timer: [['stage_timer'], g => g('stage_timer')],
  frame_since_harryup: [['frame_since_harryup'], g => g('frame_since_harryup')],
  z_vsync_count2: [['z_vsync_count2'], g => g('z_vsync_count2')],
  frame_since_start: [['frame_since_start'], g => g('frame_since_start')],
};
const ORDER = ['player_x', 'player_y', 'orb_x', 'orb_y', 'orb_vx', 'orb_vy', 'orb_vy_dbl', 'orb_vy_bits', 'score', 'rng_seed',
  'orb_force', 'orb_force_dbl', 'orb_force_bits', 'orb_force_frame', 'orb_rotation_frame', 'orb_prev_x', 'orb_prev_y', 'rem_lives', 'rem_bombs',
  'frame_rand', 'player_invincible', 'invincibility_time', 'bomb_frame', 'cardcombo_cur', 'cardcombo_max', 'stage_timer', 'frame_since_harryup',
  'z_vsync_count2', 'frame_since_start'];
const DEFAULT_COLUMNS = ORDER.filter(c => c !== 'player_y');
const SLIM_COLUMNS = ['player_x', 'orb_x', 'orb_y', 'orb_vx', 'orb_vy', 'score', 'rng_seed', 'rem_lives', 'rem_bombs', 'frame_rand',
  'invincibility_time', 'bomb_frame', 'cardcombo_cur', 'stage_timer', 'z_vsync_count2'];

// Menu keys are injected by the logger itself, counted in guest polls (emulated time), so OP.EXE's run is identical every time:
// poll #n01 of the BIOS key buffer pushes Enter (HIT KEY); group-5 polls n04a..n04b-1 after that press Z (START).
const BOOT = { n01: Number(process.env.TH01_BOOT_N01 || 700), n04a: Number(process.env.TH01_BOOT_N04A || 300), n04b: Number(process.env.TH01_BOOT_N04B || 312) };
const SIG_OFF = 0x1e4, SIG = Buffer.from('826f8260', 'hex'); // PAUSE_TITLE in REIIDEN only (not in OP/FUUIN)

/** Merge the needed fields into snapshot ranges: even lengths, total a multiple of 16, at most 8 ranges. */
/** `mem:HEXOFF:COUNT` in #@columns: COUNT raw little-endian words from DS:HEXOFF as columns m<off>. Debug/discovery of new variables. */
function expandMem(columns) {
  const out = [];
  for (const c of columns) {
    const m = /^mem:([0-9a-fA-F]+):(\d+)$/.exec(c);
    if (!m) { out.push(c); continue; }
    for (let i = 0; i < Number(m[2]); i++) {
      const off = parseInt(m[1], 16) + 2 * i, name = 'm' + off.toString(16);
      F[name] = [off, 2, 'u16']; COLUMNS[name] = [[name], g => g(name)]; if (!ORDER.includes(name)) ORDER.push(name); out.push(name);
    }
  }
  return out;
}

function planRanges(columns) {
  const need = new Set(['z_vsync_count2']);
  for (const c of columns) for (const f of (COLUMNS[c] ? COLUMNS[c][0] : [])) need.add(f);
  const iv = [...need].map(n => [F[n][0], F[n][0] + F[n][1]]).sort((a, b) => a[0] - b[0]);
  const merged = [];
  for (const [s, e] of iv) { const l = merged[merged.length - 1]; if (l && s - l[1] <= 16) l[1] = Math.max(l[1], e); else merged.push([s, e]); }
  const ranges = merged.map(([s, e]) => [s & ~1, ((e + 1) & ~1) - (s & ~1)]);
  let total = ranges.reduce((a, r) => a + r[1], 0);
  while (total % 16) { ranges[ranges.length - 1][1] += 2; total += 2; }
  if (ranges.length > 8) throw new Error('too many snapshot ranges (' + ranges.length + '): select fewer columns');
  return ranges;
}

// ---- GameButtons -> PC-98 key groups (platform/x86real/pc98/keyboard.hpp) ------------------------
const BTN = { Up: 1, Down: 2, Left: 4, Right: 8, Shot: 16, Bomb: 32, Pause: 64, Ok: 128, Cancel: 256 };
function groupsFor(bits) {
  const g = new Array(10).fill(0);
  if (bits & BTN.Up) g[7] |= 0x04; if (bits & BTN.Left) g[7] |= 0x08; if (bits & BTN.Right) g[7] |= 0x10;
  if (bits & BTN.Down) g[7] |= 0x20; if (bits & BTN.Shot) g[5] |= 0x02; if (bits & BTN.Bomb) g[5] |= 0x04;
  if (bits & (BTN.Pause | BTN.Cancel)) g[0] |= 0x01; if (bits & BTN.Ok) g[3] |= 0x10;
  return g;
}

function parseScript(text) {                      // same format as Th01.Tools InputScript (+ #@ directives)
  let seed = 1, ticks = 0; const ch = [], dir = {};
  for (let line of text.split(/\r?\n/)) {
    const d = /^\s*#@\s*(\w+)\s+(.*?)\s*$/.exec(line); if (d) dir[d[1]] = d[2];
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
  return { seed, ticks, at, dir };
}

function makeDecoder(ranges) {
  const place = (o, n) => { let acc = 0; for (const [b, l] of ranges) { if (o >= b && o + n <= b + l) return acc + (o - b); acc += l; } throw new Error('offset not captured: ' + o.toString(16)); };
  return s => {
    const get = name => {
      const [o, n, t] = F[name], p = place(o, n);
      switch (t) { case 'i8': return s.readInt8(p); case 'i16': return s.readInt16LE(p); case 'u16': return s.readUInt16LE(p);
        case 'u32': return s.readUInt32LE(p); case 'f64': return s.readDoubleLE(p); }
    };
    get.bits = name => s.readBigInt64LE(place(F[name][0], 8));
    return get;
  };
}

const TXT = c => ({ ascii: c.charCodeAt(0), scan: c === '\r' ? 0x1c : c >= '1' && c <= '9' ? 0x01 + Number(c) : c === '0' ? 0x0b : 0 });

async function capture(opts) {
  const root = opts.root, work = path.join(root, 'work'), out = path.join(work, 'run');
  fs.mkdirSync(out, { recursive: true });
  const script = parseScript(fs.readFileSync(opts.script, 'utf8'));
  const columns = expandMem(script.dir.columns ? script.dir.columns.split(',').map(s => s.trim()) : (script.ticks > 1200 ? SLIM_COLUMNS : DEFAULT_COLUMNS));
  const raw = opts.raw;                                   // {startTick, count, ranges}: raw DS slots instead of a CSV (debugging nondeterminism)
  const ranges = raw ? raw.ranges : planRanges(columns), decode = makeDecoder(raw ? [[0, 2]] : ranges);
  try { require('child_process').execSync('taskkill /F /IM dosbox-x.exe', { stdio: 'ignore' }); } catch { /* none running */ }
  await new Promise(r => setTimeout(r, 2500));
  fs.copyFileSync(opts.hdi, path.join(work, 'run.hdi'));   // fresh COPY every run (the game writes CFG/score files)
  const inputs = Buffer.alloc(script.ticks * 10);
  for (let t = 0; t < script.ticks; t++) groupsFor(script.at(t)).forEach((v, g) => { inputs[t * 10 + g] = v; });
  fs.writeFileSync(path.join(out, 'TRACE.COM'), assemble());
  const { cfg, slotParas, bufDelta, keep } = buildCfg({
    cap: raw ? raw.count : script.ticks, startTick: raw ? raw.startTick : 0, ranges, gameSigOff: SIG_OFF, gameSig: SIG, inputs,
    boot: opts.hostMenu ? undefined : BOOT, pinOff: process.env.TH01_PIN_5510 ? 0x5510 : 0, pinVal: 0,   // card_flip_cycle (or similar): the menu leaves a run-dependent value here, which changes item drops (docs/ORACLE.md section 17)
    frameRandOff: F.frame_rand[0], frameRand: opts.frameRand ?? script.seed, seedOff: F.random_seed[0], seed: script.seed });
  fs.writeFileSync(path.join(out, 'CFG.BIN'), cfg);
  const stageSel = script.dir.stage !== undefined;
  fs.writeFileSync(path.join(work, 'run.conf'),
    `[dosbox]\nmachine=pc98\nmcp_server=7777\n[cpu]\ncycles=fixed 100000\n[autoexec]\nmount D run\nD:\nTRACE.COM\nimgmount C run.hdi -t hdd\nC:\nmdrv98 -M7\nop${stageSel ? ' t' : ''}\n`);
  const e = new Emu({ dosbox: path.join(root, 'tools/dosbox-x/bin/x64/Release/dosbox-x.exe'), work, conf: 'run.conf' });
  const t0 = Date.now();
  await e.start();
  try {
    if (opts.hostMenu) {                                    // legacy: host-timed keys (nondeterministic in emulated time)
      await e.sleep(14000); await e.pushKey(0x1c0d);          // title: "HIT KEY"
      await e.sleep(3500); await e.tap({ 5: 0x02 });           // menu: START (not captured: before tick 0)
    }
    await e.sleep(4000);                                     // let autoexec install TRACE.COM
    const ptr = await e.mem(0, 0x200, 4); const seg = ptr.readUInt16LE(2);
    if (stageSel) {                                          // stage select prompts: stage id, then route (typed after the menu's START)
      for (let i = 0; i < 90; i++) { const h = await e.mem(seg, CFG.base, 0x80); if (h[CFG.pushed] && h.readUInt16LE(CFG.cnt04) >= BOOT.n04b) break; await e.sleep(1000); }
      let first = true;
      for (const text of [String(script.dir.stage), String(script.dir.route ?? 0)]) {
        await e.sleep(first ? 12000 : 6000); first = false;      // the prompt flushes the key buffer: type only once it is up
        for (const ch of text) { const k = TXT(ch); await e.pushKey((k.scan << 8) | k.ascii); await e.sleep(300); }
        await e.pushKey(0x1c0d); await e.sleep(500);               // Enter
      }
    }
    let hdr, tTick0 = 0;
    const capEnd = raw ? raw.startTick + raw.count : script.ticks;
    const deadline = Date.now() + (stageSel ? 30000 : 60000) + script.ticks * 60;
    let lastTick = -1, lastMove = Date.now(), stalled = false;
    while (Date.now() < deadline) {
      await e.sleep(2000); hdr = await e.mem(seg, CFG.base, 0x80);   // poll rarely: host interaction during the run correlates with rare nondeterminism
      if (hdr[CFG.started] && !tTick0) tTick0 = Date.now();
      const tk = hdr.readUInt16LE(CFG.tick);
      if (tk >= capEnd) break;
      if (tk !== lastTick) { lastTick = tk; lastMove = Date.now(); }
      else if (hdr[CFG.started] && Date.now() - lastMove > 30000) { stalled = true; break; }  // game left the main loop (e.g. game over screens)
    }
    const tick = hdr.readUInt16LE(CFG.tick);
    if (!hdr[CFG.started]) { try { await e.screenshot(path.join(work, 'capture_fail.png')); const tv = await e.mem(0xa000, 0, 4000); for (let y = 0; y < 25; y++) { let l = ''; for (let x = 0; x < 80; x++) { const c = tv[(y * 80 + x) * 2], hi = tv[(y * 80 + x) * 2 + 1]; l += hi === 0 && c >= 0x20 && c < 0x7f ? String.fromCharCode(c) : (hi === 0 && c === 0 ? ' ' : '.'); } if (l.trim()) console.log('TEXT', l.trimEnd()); } } catch { /* debug aid only */ } throw new Error('capture failed: the game never reached the main loop'); }
    if (tick < capEnd && !stalled) throw new Error(`capture incomplete: tick ${tick} < ${capEnd}`);
    const nRows = raw ? raw.count : Math.min(script.ticks, tick + 1);
    const tEnd = Date.now();
    if (process.env.TH01_SHOT) await e.screenshot(process.env.TH01_SHOT);   // debug only: VRAM -> PNG, never committed
    const slotBytes = slotParas * 16, rows = [];
    const per = Math.floor(0x10000 / slotBytes);             // MEMDUMPBIN wraps at 64 KB offsets: one window per chunk
    if (per === 0) {                                         // a single slot is bigger than 64 KB (raw VRAM dumps): read it piecewise
      for (let i = 0; i < nRows; i++) {
        const parts = [];
        for (let o = 0; o < slotBytes; o += 0x10000) parts.push(await e.mem(seg + bufDelta + i * slotParas + (o >> 4), 0, Math.min(0x10000, slotBytes - o)));
        rows.push(Buffer.concat(parts));
      }
    }
    for (let t0r = 0; per > 0 && t0r < nRows; t0r += per) {
      const n = Math.min(per, nRows - t0r);
      const b = await e.mem(seg + bufDelta + t0r * slotParas, 0, n * slotBytes);
      for (let i = 0; i < n; i++) rows.push(b.subarray(i * slotBytes, (i + 1) * slotBytes));
    }
    if (raw) return { slots: rows, slotBytes };
    const cols = ['tick', 'input_bits', ...ORDER.filter(c => columns.includes(c) || c === 'player_y' || c === 'z_vsync_count2')];
    const lines = [`# source=original`, `# build=th01.hdi REIIDEN.EXE (japanese)`, `# seed=${script.seed}`, `# frame_rand=${opts.frameRand ?? script.seed}`,
      `# stage=${script.dir.stage ?? 'default (stage 1 via the title menu)'}`, `# capture_ms=${tEnd - tTick0}`, `# rows=${nRows}${stalled ? ' (stopped: the game left the main loop)' : ''}`, `# snapshot_bytes=${slotBytes}`, '# z_vsync_count2 and frame_since_start are relative to row 0', cols.join(',')];
    // Sanity: a stage-1 start must show the Orb at its start position at tick 0; otherwise the logger started late (the menu's Z leaked).
    if (script.dir.stage === undefined && columns.includes('orb_x') && columns.includes('orb_y')) {
      const g0 = decode(rows[0]);
      if (COLUMNS.orb_x[1](g0) !== 600 || COLUMNS.orb_y[1](g0) !== 280) throw new Error('bad start: tick 0 is not the stage start (orb ' + COLUMNS.orb_x[1](g0) + ',' + COLUMNS.orb_y[1](g0) + ')');
    }
    // Free-running counters are written relative to row 0: their absolute value depends on how long the menu took.
    const base0 = decode(rows[0]);
    const REL = { z_vsync_count2: 0xffff, frame_since_start: 0xffffffff };
    rows.forEach((s, t) => {
      const get = decode(s);
      lines.push(cols.map(c => {
        if (c === 'tick') return t;
        if (c === 'input_bits') return script.at(t);
        if (c === 'player_y') return '';
        const v = COLUMNS[c][1](get);
        return c in REL ? String((v - COLUMNS[c][1](base0)) & REL[c] >>> 0) : String(v);
      }).join(','));
    });
    fs.mkdirSync(path.dirname(opts.out), { recursive: true });
    fs.writeFileSync(opts.out, lines.join('\n') + '\n');
    return { ticks: nRows, stalled, ms: tEnd - tTick0, total: Date.now() - t0, slotBytes, keep };
  } finally { e.stop(); }
}

module.exports = { capture, F, COLUMNS, parseScript, groupsFor, planRanges };

if (require.main === module) {
  const a = process.argv.slice(2); const opt = (n, d) => { const i = a.indexOf(n); return i >= 0 ? a[i + 1] : d; };
  const root = path.resolve(opt('--root', path.join(__dirname, '../../../../../game-data')));
  const fr = opt('--frame-rand', null);
  const o = { script: a[0], out: a[1], root, hdi: opt('--hdi', path.join(root, 'work/th01.hdi')), frameRand: fr === null ? undefined : Number(fr) >>> 0 };
  if (!o.script || !o.out) { console.error('usage: node capture.js <input-script> <out.csv> [--root dir] [--hdi file] [--frame-rand N]'); process.exit(2); }
  capture(o).then(r => { console.log(`captured ${r.ticks} ticks in ${r.ms} ms (${(r.ticks / (r.ms / 1000)).toFixed(1)} ticks/s wall-clock; run ${r.total} ms; ${r.slotBytes} B/tick)`); process.exit(0); },
    err => { console.error('ERROR', err.message); process.exit(1); });
}
