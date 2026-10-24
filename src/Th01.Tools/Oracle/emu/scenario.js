// Runs one scenario twice on the original and checks that both traces are identical (determinism).
//   node scenario.js <script.input.txt> <traces dir> --root <game-data dir>
'use strict';
const fs = require('fs'), path = require('path');
const { capture: capture1 } = require('./capture.js');
// A run can start late (see capture.js 'bad start'); retry those, but never hide a real divergence between two good runs.
async function capture(o) { for (let k = 1; ; k++) { try { return await capture1(o); } catch (e) { if (!/bad start|never reached/.test(e.message) || k >= 3) throw e; console.log('  retry (' + e.message + ')'); } } }

(async () => {
  const a = process.argv.slice(2); const opt = (n, d) => { const i = a.indexOf(n); return i >= 0 ? a[i + 1] : d; };
  const script = a[0], dir = a[1], root = path.resolve(opt('--root', 'game-data'));
  const name = path.basename(script).replace(/\.input\.txt$/, '');
  fs.mkdirSync(dir, { recursive: true });
  const o = { script, root, hdi: path.join(root, 'work/th01.hdi') };
  const t0 = Date.now();
  const fa = path.join(dir, name + '.run-a.csv'), fb = path.join(dir, name + '.run-b.csv');
  const ra = await capture({ ...o, out: fa });
  await capture({ ...o, out: fb });
  const data = f => fs.readFileSync(f, 'utf8').split('\n').filter(l => l && l[0] !== '#');
  const A = data(fa), B = data(fb);
  let diff = -1; for (let i = 0; i < Math.max(A.length, B.length); i++) if (A[i] !== B[i]) { diff = i; break; }
  const secs = Math.round((Date.now() - t0) / 1000);
  if (diff < 0) {
    fs.copyFileSync(fa, path.join(dir, name + '.original.csv'));
    fs.copyFileSync(script, path.join(dir, name + '.input.txt'));
    fs.unlinkSync(fa); fs.unlinkSync(fb);
    console.log(`${name}: DETERMINISTIC, ${A.length - 1} ticks${ra.stalled ? ' (stopped early: the game left the main loop)' : ''}, 2 runs in ${secs} s`);
  } else {
    console.log(`${name}: NOT DETERMINISTIC at row ${diff}:\n  A ${A[diff]}\n  B ${B[diff]}\n  (rows: A ${A.length - 1}, B ${B.length - 1}; kept *.run-a/b.csv)`);
    process.exitCode = 1;
  }
})().catch(e => { console.error('ERROR', e.message); process.exit(1); });
