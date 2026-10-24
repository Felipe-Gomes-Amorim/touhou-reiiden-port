// Helpers to run DOSBox-X for the oracle: launch, memory dumps, PC-98 key injection, VRAM screenshot (debug only).
'use strict';
const fs = require('fs'), path = require('path'), cp = require('child_process'), zlib = require('zlib');
const { Mcp } = require('./mcp.js');

class Emu {
  /** @param {{dosbox:string, work:string, conf:string, port?:number}} o  work = cwd of the emulator (dumps land here) */
  constructor(o) { this.o = o; this.m = new Mcp(o.port || 7777); this.n = 0; }
  async start() {
    try { cp.execSync("taskkill /F /IM dosbox-x.exe", { stdio: "ignore" }); } catch { /* none running */ }   // stale instance would lock the disk copy
    await this.sleep(2000);
    await this.m.listen();
    this.proc = cp.spawn(this.o.dosbox, ['-conf', this.o.conf], { cwd: this.o.work, stdio: 'ignore' });
    await this.m.waitConnected();
  }
  stop() { try { this.m.close(); this.proc && this.proc.kill(); } catch { /* ignore */ } }
  sleep(ms) { return new Promise(r => setTimeout(r, ms)); }
  /** Reads guest memory through MEMDUMPBIN -> file in the work dir. */
  async mem(seg, off, len) {
    const name = `D${process.pid}_${this.n++}.BIN`;
    const r = await this.m.exec(`MEMDUMPBIN ${seg.toString(16)}:${off.toString(16)} ${len.toString(16)} ${name}`);
    if (!r.ok) throw new Error('MEMDUMPBIN failed: ' + r.text);
    const p = path.join(this.o.work, name);
    for (let i = 0; i < 50 && !fs.existsSync(p); i++) await this.sleep(20);
    let b = fs.readFileSync(p);
    for (let i = 0; i < 50 && b.length < len; i++) { await this.sleep(20); b = fs.readFileSync(p); }
    fs.unlinkSync(p);
    return b;
  }
  /** BIOS key-state table (read by INT 18h AH=04h when no TRACE hook overrides it). */
  async keys(groups) { // groups: {7:0x04,...}
    for (let g = 0; g < 16; g++) {
      const v = groups[g] || 0;
      await this.m.exec(`SM 0000:${(0x52a + g).toString(16)} ${v.toString(16)}`);
    }
  }
  async tap(groups, ms = 120) { await this.keys(groups); await this.sleep(ms); await this.keys({}); }
  /** Pushes a key into the BIOS keyboard ring buffer (INT 18h AH=00/01 users, e.g. "HIT KEY"). ax = scancode<<8 | ascii. */
  async pushKey(ax) {
    const b = await this.mem(0, 0x524, 6);                       // head, tail, count
    let tail = b.readUInt16LE(2); const count = b.readUInt16LE(4);
    const hx = v => v.toString(16);
    await this.m.exec(`SM 0000:${hx(tail)} ${hx(ax & 255)} ${hx(ax >> 8)}`);
    tail += 2; if (tail >= 0x522) tail = 0x502;
    await this.m.exec(`SM 0000:526 ${hx(tail & 255)} ${hx(tail >> 8)} ${hx((count + 1) & 255)} 0`);
  }
  /** 640x400 screenshot of VRAM page 0 with a fixed digital palette (debug only; never committed). */
  async screenshot(file) {
    const planes = [];
    for (const seg of [0xa800, 0xb000, 0xb800, 0xe000]) planes.push(await this.mem(seg, 0, 0x7d00));
    const W = 640, H = 400, raw = Buffer.alloc((W * 3 + 1) * H);
    for (let y = 0; y < H; y++) {
      raw[y * (W * 3 + 1)] = 0;
      for (let x = 0; x < W; x++) {
        const i = y * 80 + (x >> 3), bit = 0x80 >> (x & 7);
        const c = (planes[0][i] & bit ? 1 : 0) | (planes[1][i] & bit ? 2 : 0) | (planes[2][i] & bit ? 4 : 0) | (planes[3][i] & bit ? 8 : 0);
        const lvl = c & 8 ? 255 : 170, o = y * (W * 3 + 1) + 1 + x * 3;
        raw[o] = c & 2 ? lvl : 0; raw[o + 1] = c & 4 ? lvl : 0; raw[o + 2] = c & 1 ? lvl : 0;
        if (c === 8) { raw[o] = raw[o + 1] = raw[o + 2] = 85; }
      }
    }
    fs.writeFileSync(file, png(W, H, raw));
  }
}

function crc32(buf) {
  let c, crc = 0xffffffff;
  for (let n = 0; n < buf.length; n++) { c = (crc ^ buf[n]) & 0xff; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; }
  return (crc ^ 0xffffffff) >>> 0;
}
function png(w, h, raw) {
  const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc32(td)); return Buffer.concat([l, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}

module.exports = { Emu };
