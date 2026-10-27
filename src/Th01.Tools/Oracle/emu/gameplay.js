// Gameplay (REIIDEN.EXE) measurements in the ORIGINAL (derived from title.js): menu START is injected in the guest, then every VSync since REIIDEN started is logged (tick 0 = its vsync_init). --freeze N halts the guest at VSync N so VRAM can be dumped exactly there; --script drives input per game tick.
// Title/menu (OP.EXE) measurements in the ORIGINAL, counted in VSyncs (= ticks).
// TRACE.COM wraps the VSync interrupt of whatever program installs one (INT 21h AH=25h AL=0Ah): tick 0 = that moment
// (OP.EXE's vsync_init, at the start of its main()). Per VSync it logs 4 checksums (planes B,R,G,E of the CURRENT ACCESS page).
// Scheduled keys are relative to the moment the HIT KEY screen receives Enter (vsPush).
//
//   node title.js <out.csv> --root <game-data> [--push N] [--sched start:end:group:bits,...] [--vsyncs N] [--vram prefix]
'use strict';
const fs = require('fs'), path = require('path');
const { Emu } = require('./emu.js');
const { assemble, buildCfg, CFG } = require('./tsr.js');
const { parseScript, groupsFor } = require('./capture.js');

(async () => {
  const freezeEarly = Number(process.argv.includes('--freeze') ? process.argv[process.argv.indexOf('--freeze') + 1] : 0);
  const a = process.argv.slice(2); const opt = (n, d) => { const i = a.indexOf(n); return i >= 0 ? a[i + 1] : d; };
  const out = a[0], root = path.resolve(opt('--root', 'game-data')), work = path.join(root, 'work'), run = path.join(work, 'run');
  const push = Number(opt('--push', 700)), nv = Number(opt('--vsyncs', 2500));
  const sched = (opt('--sched', '') || '').split(',').filter(Boolean).map(s => s.split(':').map(Number));
  const vramPrefix = opt('--vram', null), vramAts = vramPrefix && !freezeEarly ? String(opt('--vram-at', '25')).split(',').map(Number) : [];
  fs.mkdirSync(run, { recursive: true });
  try { require('child_process').execSync('taskkill /F /IM dosbox-x.exe', { stdio: 'ignore' }); } catch { /* none */ }
  await new Promise(r => setTimeout(r, 2500));
  fs.copyFileSync(path.join(work, 'th01.hdi'), path.join(work, 'run.hdi'));
  fs.writeFileSync(path.join(run, 'TRACE.COM'), assemble());
  const pin = opt('--pin', null) ? opt('--pin').split(':').map(s => parseInt(s, 16)) : null, fEpoch = Number(opt('--freeze-epoch', 3)), scriptFile = opt('--script', null), freeze = Number(opt('--freeze', 0));
  let inputs = null, cap = 1;
  if (scriptFile) { const sc = parseScript(fs.readFileSync(scriptFile, 'utf8')); cap = sc.ticks; inputs = Buffer.alloc(sc.ticks * 10); for (let t = 0; t < sc.ticks; t++) groupsFor(sc.at(t)).forEach((v, g) => { inputs[t * 10 + g] = v; }); }
  const { cfg, logDelta } = buildCfg({ cap, ranges: [[0x90, 0x10]], gameSigOff: 0x1e4, gameSig: Buffer.from('826f8260', 'hex'), inputs, freeze: freeze ? freeze + 1 : 0, freezeEpoch: fEpoch, pinOff: pin ? pin[0] : 0, pinVal: pin ? pin[1] : 0,
    boot: { n01: push, n04a: 300, n04b: 312 }, logCap: nv, sched });
  fs.writeFileSync(path.join(run, 'CFG.BIN'), cfg);
  fs.writeFileSync(path.join(work, 'run.conf'),
    '[dosbox]\nmachine=pc98\nmcp_server=7777\n[cpu]\ncycles=fixed 100000\n[autoexec]\nmount D run\nD:\nTRACE.COM\nimgmount C run.hdi -t hdd\nC:\nmdrv98 -M7\nop\n');
  const e = new Emu({ dosbox: path.join(root, 'tools/dosbox-x/bin/x64/Release/dosbox-x.exe'), work, conf: 'run.conf' });
  await e.start();
  try {
    await e.sleep(4000);
    const seg = (await e.mem(0, 0x200, 4)).readUInt16LE(2);
    const rd16 = (b, o) => b.readUInt16LE(o);
    let h, t0 = Date.now(), nextDump = 0, firstVs0 = null;
    for (;;) {
      await e.sleep(1000);
      h = await e.mem(seg, CFG.base, 0x100);
      if (h[CFG.started] && firstVs0 === null) firstVs0 = rd16(h, CFG.vs0);   // the epoch of the first REIIDEN run (it re-installs the VSync handler at its start; a later restart would overwrite the log)
      if (firstVs0 !== null && rd16(h, CFG.vs0) !== firstVs0) { console.log('WARNING: REIIDEN restarted before the log was read'); break; }
      const done = rd16(h, CFG.vs) - rd16(h, CFG.vs0); if (process.env.TH01_VERBOSE) console.log('poll', Date.now() - t0, 'done', done, 'vs0', rd16(h, CFG.vs0), 'tick', rd16(h, CFG.tick), 'started', h[CFG.started]);
      if (h[CFG.hooked] && vramPrefix && nextDump < vramAts.length && done >= vramAts[nextDump]) {       // optional full VRAM dumps (4 planes x 32000 bytes) at the given VSyncs (suffix _<vsync>; the screen must be static: dumps are host-timed)
        const tag = vramAts.length === 1 ? '' : '_' + vramAts[nextDump]; nextDump++; for (const [n, sg] of [['B', 0xa800], ['R', 0xb000], ['G', 0xb800], ['E', 0xe000]]) fs.writeFileSync(`${vramPrefix}${tag}_${n}.bin`, await e.mem(sg, 0, 0x7d00));
      }
      if (freeze && h[CFG.hooked] && h[CFG.epoch] === fEpoch && done >= freeze && vramPrefix) { await e.sleep(1500); for (const [n, sg] of [['B', 0xa800], ['R', 0xb000], ['G', 0xb800], ['E', 0xe000]]) fs.writeFileSync(`${vramPrefix}_${n}.bin`, await e.mem(sg, 0, 0x7d00)); fs.writeFileSync(`${vramPrefix}_T.bin`, await e.mem(0xa000, 0, 0x1000)); fs.writeFileSync(`${vramPrefix}_A.bin`, await e.mem(0xa200, 0, 0x1000)); { const T = fs.readFileSync(`${vramPrefix}_T.bin`), A = fs.readFileSync(`${vramPrefix}_A.bin`); let hs = 0; for (let i = 0; i < 2000; i++) hs = (hs + T.readUInt16LE(i * 2) + A.readUInt16LE(i * 2)) & 0xffff; const lg = await e.mem(seg + logDelta + (freeze - 1) * 2, 0, 64); console.log('host text sum', hs, 'guest log rows', freeze - 1, freeze, 'word15:', lg.readUInt16LE(30), lg.readUInt16LE(62), 'word0:', lg.readUInt16LE(0), lg.readUInt16LE(32)); } console.log('froze at', rd16(h, CFG.vs) - rd16(h, CFG.vs0)); return; }
      if (h[CFG.hooked] && h[CFG.started] && done >= nv + 5 && nextDump >= vramAts.length) break;
      if (Date.now() - t0 > 400000) break;
    }
    const vs = rd16(h, CFG.vs), vs0 = rd16(h, CFG.vs0), vsPush = rd16(h, CFG.vsPush), pushed = h[CFG.pushed];
    const n = Math.min(nv, (vs - vs0) & 0xffff);
    const rows = [];
    for (let i = 0; i < n; i += 2000) {
      const c = Math.min(2000, n - i);
      const b = await e.mem(seg + logDelta + i * 2, 0, c * 32);
      for (let k = 0; k < c; k++) rows.push([0,2,4,6,8,10,12,14,16,18,20,22,24,26,28,30].map(o => b.readUInt16LE(k * 32 + o)));
    }
    fs.writeFileSync(out, [`# epoch=${h[CFG.epoch]} vs=${vs} vs0=${vs0} pushed=${pushed} vsPush_rel=${(vsPush - vs0) & 0xffff}`, 'vsync,sumB,sumR,sumG,sumE,gametick,p0B,p0R,p0G,p0E,p1B,p1R,p1G,p1E,P,text', ...rows.map(r => r.join(','))].join('\n') + '\n');
    console.log(`logged ${rows.length} vsyncs; OP start at absolute vs=${vs0}; Enter pushed=${pushed} at relative vsync ${(vsPush - vs0) & 0xffff}`);
  } finally { e.stop(); }
})().catch(err => { console.error('ERROR', err.message); process.exit(1); });
