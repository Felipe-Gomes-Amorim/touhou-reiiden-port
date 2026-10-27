// Debug/measurement helper: turns the 4 plane dumps written by title.js (--vram prefix) into palette INDICES per pixel,
// lists bounding boxes of the pixels of a given index, and writes a PNG for viewing (local only, never committed).
//   node vramtool.js <prefix> [--png out.png] [--index N]... [--rows y0-y1] [--scale 1]
'use strict';
const fs = require('fs'), zlib = require('zlib');

function load(prefix) {
  const pl = ['B', 'R', 'G', 'E'].map(n => fs.readFileSync(`${prefix}_${n}.bin`));
  const idx = new Uint8Array(640 * 400);
  for (let y = 0; y < 400; y++) for (let x = 0; x < 640; x++) {
    const i = y * 80 + (x >> 3), bit = 0x80 >> (x & 7);
    idx[y * 640 + x] = (pl[0][i] & bit ? 1 : 0) | (pl[1][i] & bit ? 2 : 0) | (pl[2][i] & bit ? 4 : 0) | (pl[3][i] & bit ? 8 : 0);
  }
  return idx;
}

/** Horizontal text lines of one palette index: rows with pixels of the index, grouped into bands; each band has an x-extent. */
function bands(idx, c, x0 = 0, x1 = 639, y0 = 0, y1 = 399, gap = 2) {
  const rows = [];
  for (let y = y0; y <= y1; y++) {
    let lo = 1e9, hi = -1;
    for (let x = x0; x <= x1; x++) if (idx[y * 640 + x] === c) { if (x < lo) lo = x; if (x > hi) hi = x; }
    rows.push(hi >= 0 ? [y, lo, hi] : null);
  }
  const out = []; let cur = null, blank = 0;
  for (const r of rows) {
    if (r) { if (!cur) cur = { top: r[0], bottom: r[0], left: r[1], right: r[2] }; else { cur.bottom = r[0]; cur.left = Math.min(cur.left, r[1]); cur.right = Math.max(cur.right, r[2]); } blank = 0; }
    else if (cur && ++blank > gap) { out.push(cur); cur = null; }
  }
  if (cur) out.push(cur);
  return out.map(b => ({ ...b, width: b.right - b.left + 1, height: b.bottom - b.top + 1, centerX: (b.left + b.right) / 2 }));
}

function histogram(idx, x0 = 0, x1 = 639, y0 = 0, y1 = 399) {
  const h = new Array(16).fill(0);
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) h[idx[y * 640 + x]]++;
  return h;
}

const PAL = i => { const lvl = i & 8 ? 255 : 170; return [i & 2 ? lvl : 0, i & 4 ? lvl : 0, i & 1 ? lvl : 0]; };
function crc32(buf) { let c, crc = 0xffffffff; for (let n = 0; n < buf.length; n++) { c = (crc ^ buf[n]) & 0xff; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; crc = (crc >>> 8) ^ c; } return (crc ^ 0xffffffff) >>> 0; }
function png(w, h, raw) {
  const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc32(td)); return Buffer.concat([l, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}
function writePng(idx, file, x0 = 0, y0 = 0, w = 640, h = 400) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; for (let x = 0; x < w; x++) { const [r, g, b] = PAL(idx[(y0 + y) * 640 + x0 + x]); const o = y * (w * 3 + 1) + 1 + x * 3; raw[o] = r; raw[o + 1] = g; raw[o + 2] = b; } }
  fs.writeFileSync(file, png(w, h, raw));
}

module.exports = { load, bands, histogram, writePng };

if (require.main === module) {
  const a = process.argv.slice(2), idx = load(a[0]);
  const pi = a.indexOf('--png'); if (pi >= 0) writePng(idx, a[pi + 1]);
  console.log('histogram of palette indices:', histogram(idx).map((n, i) => `${i}:${n}`).filter(s => !s.endsWith(':0')).join(' '));
  for (let i = 0; i < a.length; i++) if (a[i] === '--index') { const c = Number(a[i + 1]); console.log('index', c, JSON.stringify(bands(idx, c))); }
}
