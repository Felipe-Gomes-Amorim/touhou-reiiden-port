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

(async () => {
  const a = process.argv.slice(2); const opt = (n, d) => { const i = a.indexOf(n); return i >= 0 ? a[i + 1] : d; };
  const out = a[0], root = path.resolve(opt('--root', 'game-data')), work = path.join(root, 'work'), run = path.join(work, 'run');
  const push = Number(opt('--push', 60000)), nv = Number(opt('--vsyncs', 2500));
  const sched = (opt('--sched', '') || '').split(',').filter(Boolean).map(s => s.split(':').map(Number));
  const vramPrefix = opt('--vram', null), vramAts = String(opt('--vram-at', '25')).split(',').map(Number);
  fs.mkdirSync(run, { recursive: true });
  try { require('child_process').execSync('taskkill /F /IM dosbox-x.exe', { stdio: 'ignore' }); } catch { /* none */ }
  await new Promise(r => setTimeout(r, 2500));
  fs.copyFileSync(path.join(work, 'th01.hdi'), path.join(work, 'run.hdi'));
  fs.writeFileSync(path.join(run, 'TRACE.COM'), assemble());
  const { cfg, logDelta } = buildCfg({ cap: 1, ranges: [[0x90, 0x10]], gameSigOff: 0x1e4, gameSig: Buffer.from('826f8260', 'hex'), inputs: null,
    boot: { n01: push, n04a: 0, n04b: 0 }, logCap: nv, sched });
  fs.writeFileSync(path.join(run, 'CFG.BIN'), cfg);
  fs.writeFileSync(path.join(work, 'run.conf'),
    '[dosbox]\nmachine=pc98\nmcp_server=7777\n[cpu]\ncycles=fixed 100000\n[autoexec]\nmount D run\nD:\nTRACE.COM\nimgmount C run.hdi -t hdd\nC:\nmdrv98 -M7\nop\n');
  const e = new Emu({ dosbox: path.join(root, 'tools/dosbox-x/bin/x64/Release/dosbox-x.exe'), work, conf: 'run.conf' });
  await e.start();
  try {
    await e.sleep(4000);
    const seg = (await e.mem(0, 0x200, 4)).readUInt16LE(2);
    const rd16 = (b, o) => b.readUInt16LE(o);
    let h, t0 = Date.now(), nextDump = 0;
    for (;;) {
      await e.sleep(1000);
      h = await e.mem(seg, CFG.base, 0xa0);
      const done = rd16(h, CFG.vs) - rd16(h, CFG.vs0);
      if (h[CFG.hooked] && vramPrefix && nextDump < vramAts.length && done >= vramAts[nextDump]) {       // optional full VRAM dumps (4 planes x 32000 bytes) at the given VSyncs (suffix _<vsync>; the screen must be static: dumps are host-timed)
        const tag = vramAts.length === 1 ? '' : '_' + vramAts[nextDump]; nextDump++; for (const [n, sg] of [['B', 0xa800], ['R', 0xb000], ['G', 0xb800], ['E', 0xe000]]) fs.writeFileSync(`${vramPrefix}${tag}_${n}.bin`, await e.mem(sg, 0, 0x7d00));
      }
      if (h[CFG.hooked] && done >= nv + 5 && nextDump >= vramAts.length) break;
      if (Date.now() - t0 > 150000) break;
    }
    const vs = rd16(h, CFG.vs), vs0 = rd16(h, CFG.vs0), vsPush = rd16(h, CFG.vsPush), pushed = h[CFG.pushed];
    const n = Math.min(nv, (vs - vs0) & 0xffff);
    const rows = [];
    for (let i = 0; i < n; i += 2000) {
      const c = Math.min(2000, n - i);
      const b = await e.mem(seg + logDelta + i * 2, 0, c * 32);
      for (let k = 0; k < c; k++) rows.push([b.readUInt16LE(k * 32), b.readUInt16LE(k * 32 + 2), b.readUInt16LE(k * 32 + 4), b.readUInt16LE(k * 32 + 6), b.readUInt16LE(k * 32 + 8)]);
    }
    fs.writeFileSync(out, [`# vs=${vs} vs0=${vs0} pushed=${pushed} vsPush_rel=${(vsPush - vs0) & 0xffff}`, 'vsync,sumB,sumR,sumG,sumE', ...rows.map(r => r.join(','))].join('\n') + '\n');
    console.log(`logged ${rows.length} vsyncs; OP start at absolute vs=${vs0}; Enter pushed=${pushed} at relative vsync ${(vsPush - vs0) & 0xffff}`);
  } finally { e.stop(); }
})().catch(err => { console.error('ERROR', err.message); process.exit(1); });
