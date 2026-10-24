// Builds TRACE.COM: a tiny DOS TSR (8086) that hooks INT 18h AH=04h (BIOS key sense) inside the emulator.
// Why: DOSBox-X's debugger cannot be used to pause the CPU per tick (BREAK/VRT kill the process), so the
// logger and the input injector live *inside* the guest. Own code, hand-assembled here; no game bytes.
//
// Behaviour: when the caller's DS holds the game signature, the first key_sense(7) after a key_sense(0|3)
// is "tick start": the first one sets frame_rand/random_seed, then every tick copies configured DS ranges
// into a buffer and every key_sense(group) returns AH = inputTable[tick][group].
'use strict';

const CFG = {
  base: 0x400,            // header offset inside the COM segment (cfg file is read here)
  sig: 0x000, cap: 0x004, slotParas: 0x006, nin: 0x008, gsOff: 0x00a, gsVal: 0x00c, frOff: 0x010, frVal: 0x012,
  seedOff: 0x016, seedVal: 0x018, nRangeBytes: 0x01c, ranges: 0x020 /* 8 x (off,len,seg,0): seg 0 = the game's DS */, tick: 0x060, started: 0x062,
  seen7: 0x063, old18: 0x064, bufDelta: 0x06a, keepParas: 0x06c, startTick: 0x06e, pinOff: 0x070, pinVal: 0x072, n01: 0x074, n04a: 0x076, n04b: 0x078, pushed: 0x07a, cnt01: 0x07c, cnt04: 0x07e, table: 0x080,
};
const A = o => CFG.base + o; // absolute offset in the COM segment

function assemble() {
  const out = []; const labels = {}; const fix = [];
  const b = (...v) => out.push(...v);
  const w = v => b(v & 255, (v >> 8) & 255);
  const L = n => { labels[n] = out.length; };
  const near = n => { b(0xe9, 0, 0); fix.push([out.length - 2, n, false, true]); };
  const near2 = (n, jcc) => { b(jcc ^ 1, 3); near(n); };   // jcc near, built as: j!cc +3 ; jmp near
  const rel = (op, n) => { b(op, 0); fix.push([out.length - 1, n]); };
  const csm = (pre, off) => { b(0x2e, ...pre); w(A(off)); };            // cs: <opcode bytes> disp16

  const org = 0x100;
  // ---- installer ----
  L('start');
  b(0xba); w(0); fix.push([out.length - 2, 'fname', true]);              // mov dx, fname
  b(0xb8); w(0x3d00); b(0xcd, 0x21);                                      // open
  rel(0x72, 'fail');                                                      // jc
  b(0x89, 0xc3);                                                          // mov bx,ax
  b(0xba); w(CFG.base); b(0xb9); w(0xe000); b(0xb4, 0x3f, 0xcd, 0x21);    // read cfg into header area
  b(0xb4, 0x3e, 0xcd, 0x21);                                              // close
  b(0xb8); w(0x3518); b(0xcd, 0x21);                                      // get int 18h -> es:bx
  b(0x89, 0x1e); w(A(CFG.old18)); b(0x8c, 0x06); w(A(CFG.old18 + 2));     // mov [old18],bx ; mov [old18+2],es
  b(0xba, 0, 0); fix.push([out.length - 2, 'h18', true]);                 // mov dx,h18
  b(0xb8); w(0x2518); b(0xcd, 0x21);                                      // set int 18h
  b(0x31, 0xc0, 0x8e, 0xc0);                                              // xor ax,ax ; mov es,ax
  b(0x26, 0xc7, 0x06); w(0x200); w(CFG.base);                             // es:[200h]=base  (IVT slot of INT 80h)
  b(0x8c, 0xc8, 0x26, 0xa3); w(0x202);                                    // mov ax,cs ; es:[202h]=ax
  b(0x8b, 0x16); w(A(CFG.keepParas));                                     // mov dx,[keepParas]
  b(0xb8); w(0x3100); b(0xcd, 0x21);                                      // keep resident
  L('fail'); b(0xb8); w(0x4c01); b(0xcd, 0x21);
  L('fname'); b(...Buffer.from('CFG.BIN\0'));

  // ---- INT 18h handler ----
  L('h18');
  b(0x80, 0xfc, 0x04); rel(0x74, 'sense');                                // cmp ah,4 ; je sense
  b(0x80, 0xfc, 0x01); rel(0x74, 'boot01');                               // cmp ah,1 ; je boot01
  L('chain'); b(0x2e, 0xff, 0x2e); w(A(CFG.old18));                       // jmp far cs:[old18]
  // Boot injection (makes the title/menu deterministic in *emulated* time): the n01-th BIOS key-buffer poll
  // pushes Enter into the BIOS ring buffer; after that, the menu's group-5 polls number n04a..n04b-1 report "Z".
  L('boot01');
  csm([0x80, 0x3e], CFG.pushed); b(0x00); rel(0x75, 'chain');            // cmp byte cs:[pushed],0 ; jne chain
  b(0x50, 0x2e, 0xa1); w(A(CFG.cnt01)); b(0x40, 0x2e, 0xa3); w(A(CFG.cnt01)); // push ax ; ax=++cnt01
  csm([0x3b, 0x06], CFG.n01); rel(0x75, 'k1');                            // cmp ax,cs:[n01] ; jne k1
  b(0x53, 0x06, 0x31, 0xdb, 0x8e, 0xc3);                                  // push bx ; push es ; xor bx,bx ; mov es,bx
  b(0x26, 0x8b, 0x1e, 0x26, 0x05);                                        // mov bx,es:[0526h]      (BIOS key buffer tail)
  b(0x26, 0xc7, 0x07, 0x0d, 0x1c);                                        // mov word es:[bx],1C0Dh  (Enter)
  b(0x83, 0xc3, 0x02, 0x81, 0xfb, 0x22, 0x05, 0x72, 0x03, 0xbb, 0x02, 0x05); // add bx,2 ; cmp bx,522h ; jb +3 ; mov bx,502h
  b(0x26, 0x89, 0x1e, 0x26, 0x05, 0x26, 0xff, 0x06, 0x28, 0x05);          // mov es:[526h],bx ; inc word es:[528h]
  b(0x07, 0x5b);                                                          // pop es ; pop bx
  csm([0xc6, 0x06], CFG.pushed); b(0x01);                                 // pushed=1
  csm([0xc7, 0x06], CFG.cnt04); b(0x00, 0x00);                            // cnt04=0
  L('k1'); b(0x58); near('chain');                                        // pop ax ; jmp chain
  L('sense');
  b(0x53, 0x51, 0x52, 0x56, 0x57, 0x06, 0x1e);                            // push bx cx dx si di es ds
  b(0x2e, 0x8b, 0x1e); w(A(CFG.gsOff));                                   // mov bx,cs:[gsOff]
  b(0x8b, 0x17); csm([0x3b, 0x16], CFG.gsVal); rel(0x75, 'notgame');      // mov dx,[bx] ; cmp dx,cs:[gsVal] ; jne
  b(0x8b, 0x57, 0x02); csm([0x3b, 0x16], CFG.gsVal + 2); rel(0x75, 'notgame');
  rel(0xeb, 'gamechk');
  L('notgame'); b(0x1f, 0x07, 0x5f, 0x5e, 0x5a, 0x59, 0x5b);
  csm([0x80, 0x3e], CFG.started); b(0x00); near2('chain', 0x75);          // started != 0 -> chain
  b(0x3c, 0x05); near2('chain', 0x75);                                    // al != 5 -> chain
  b(0x53, 0x2e, 0x8b, 0x1e); w(A(CFG.cnt04)); b(0x43, 0x2e, 0x89, 0x1e); w(A(CFG.cnt04)); // push bx ; bx=++cnt04
  csm([0x3b, 0x1e], CFG.n04a); rel(0x72, 'bchain');                       // bx < n04a -> bchain
  csm([0x3b, 0x1e], CFG.n04b); rel(0x73, 'bchain');                       // bx >= n04b -> bchain
  b(0x5b, 0xb4, 0x02, 0xcf);                                              // pop bx ; ah=2 (K5_Z) ; iret
  L('bchain'); b(0x5b); near('chain');
  L('gamechk');
  b(0x3c, 0x07); rel(0x74, 'g7');
  b(0x3c, 0x00); rel(0x74, 'rst'); b(0x3c, 0x03); rel(0x74, 'rst'); near('inject');
  L('rst'); csm([0xc6, 0x06], CFG.seen7); b(0x00); near('inject');   // mov byte cs:[seen7],0
  L('g7'); csm([0x80, 0x3e], CFG.seen7); b(0x00); rel(0x74, 'newf'); near('inject'); L('newf');    // cmp byte cs:[seen7],0 ; jne
  csm([0xc6, 0x06], CFG.seen7); b(0x01);                                  // seen7=1
  b(0x50);                                                                // push ax
  csm([0x80, 0x3e], CFG.started); b(0x00); rel(0x75, 'inc');              // cmp started,0 ; jne inc
  csm([0xc6, 0x06], CFG.started); b(0x01);                                // started=1
  b(0x2e, 0x8b, 0x1e); w(A(CFG.frOff)); b(0x09, 0xdb); rel(0x74, 'noFr'); // bx=frOff ; or bx,bx ; jz
  b(0x2e, 0xa1); w(A(CFG.frVal)); b(0x89, 0x07);                          // mov ax,cs:[frVal] ; mov [bx],ax
  b(0x2e, 0xa1); w(A(CFG.frVal + 2)); b(0x89, 0x47, 0x02);
  L('noFr');
  b(0x2e, 0x8b, 0x1e); w(A(CFG.seedOff)); b(0x09, 0xdb); rel(0x74, 'pin');
  b(0x2e, 0xa1); w(A(CFG.seedVal)); b(0x89, 0x07);
  b(0x2e, 0xa1); w(A(CFG.seedVal + 2)); b(0x89, 0x47, 0x02);
  L('pin'); b(0x2e, 0x8b, 0x1e); w(A(CFG.pinOff)); b(0x09, 0xdb); rel(0x74, 'snap');   // optional word pin (state the menu leaves behind)
  b(0x2e, 0xa1); w(A(CFG.pinVal)); b(0x89, 0x07); rel(0xeb, 'snap');
  L('inc'); csm([0xff, 0x06], CFG.tick);                                  // inc word cs:[tick]
  L('snap');
  b(0x2e, 0xa1); w(A(CFG.tick)); csm([0x2b, 0x06], CFG.startTick); rel(0x72, 'fdone'); // sub ax,startTick ; jb
  csm([0x3b, 0x06], CFG.cap); rel(0x73, 'fdone');                      // cmp ax,cap ; jae
  csm([0xf7, 0x26], CFG.slotParas);                                       // mul word cs:[slotParas]
  csm([0x03, 0x06], CFG.bufDelta);                                        // add ax,cs:[bufDelta]
  b(0x8c, 0xca, 0x01, 0xd0, 0x8e, 0xc0);                                  // mov dx,cs ; add ax,dx ; mov es,ax
  b(0x31, 0xff, 0x31, 0xdb, 0xfc);                                        // xor di,di ; xor bx,bx ; cld
  L('rng'); csm([0x3b, 0x1e], CFG.nRangeBytes); rel(0x73, 'fdone');       // cmp bx,cs:[nRangeBytes] ; jae
  b(0x2e, 0x8b, 0xb7); w(A(CFG.ranges));                                  // mov si,cs:[bx+ranges]
  b(0x2e, 0x8b, 0x8f); w(A(CFG.ranges + 2));                              // mov cx,cs:[bx+ranges+2]
  b(0x2e, 0x8b, 0x87); w(A(CFG.ranges + 4));                              // mov ax,cs:[bx+ranges+4]   (source segment, 0 = DS)
  b(0x09, 0xc0); rel(0x74, 'nods');                                       // or ax,ax ; jz nods
  b(0x1e, 0x8e, 0xd8, 0xd1, 0xe9, 0xf3, 0xa5, 0x1f); rel(0xeb, 'rnext');  // push ds ; mov ds,ax ; shr cx,1 ; rep movsw ; pop ds
  L('nods'); b(0xd1, 0xe9, 0xf3, 0xa5);                                   // shr cx,1 ; rep movsw
  L('rnext'); b(0x83, 0xc3, 0x08); rel(0xeb, 'rng');                      // add bx,8 ; jmp rng
  L('fdone'); b(0x58);                                                    // pop ax
  L('inject');
  b(0x3c, 0x0a); rel(0x73, 'zero');                                       // cmp al,10 ; jae zero
  b(0x8a, 0xc8, 0x32, 0xed);                                              // mov cl,al ; xor ch,ch
  b(0x2e, 0xa1); w(A(CFG.tick)); csm([0x3b, 0x06], CFG.nin); rel(0x73, 'zero2'); // tick>=nin -> zero
  b(0xbb, 0x0a, 0x00, 0xf7, 0xe3, 0x03, 0xc1, 0x8b, 0xd8);                // mov bx,10 ; mul bx ; add ax,cx ; mov bx,ax
  b(0x8a, 0xc1);                                                          // mov al,cl  (restore group)
  b(0x2e, 0x8a, 0xa7); w(A(CFG.table));                                   // mov ah,cs:[bx+table]
  rel(0xeb, 'ret');
  L('zero2'); b(0x8a, 0xc1);                                              // mov al,cl
  L('zero'); b(0xb4, 0x00);
  L('ret'); b(0x1f, 0x07, 0x5f, 0x5e, 0x5a, 0x59, 0x5b, 0xcf);            // pop ds es di si dx cx bx ; iret

  for (const f of fix) {
    const [pos, name, abs, isNear] = f;
    if (isNear) { const d = labels[name] - (pos + 2); out[pos] = d & 255; out[pos + 1] = (d >> 8) & 255; }
    else if (abs) { const v = org + labels[name]; out[pos] = v & 255; out[pos + 1] = v >> 8; }
    else {
      const d = labels[name] - (pos + 1);
      if (d < -128 || d > 127) throw new Error('short jump out of range to ' + name + ' ' + d);
      out[pos] = d & 255;
    }
  }
  if (org + out.length > CFG.base) throw new Error('code overlaps header: ' + (org + out.length).toString(16));
  return Buffer.from(out);
}

/**
 * cfg: {cap, ranges:[[off,len],...], gameSigOff, gameSig:Buffer(4), frameRandOff, frameRand, seedOff, seed,
 *       inputs: Uint8Array[nin*10]}  -> Buffer for CFG.BIN (loaded at COM:0x400)
 */
function buildCfg(cfg) {
  const nin = cfg.inputs ? cfg.inputs.length / 10 : 0;
  const snap = cfg.ranges.reduce((s, r) => s + r[1], 0);
  if (snap % 16) throw new Error('sum of range lengths must be a multiple of 16');
  const slotParas = snap / 16;
  const tableEnd = CFG.table + nin * 10;
  const bufDelta = (A(tableEnd) + 15) >> 4;
  const keep = bufDelta + cfg.cap * slotParas + 1;
  if (keep > 0xf000) throw new Error('buffer too large');
  const b = Buffer.alloc(tableEnd);
  b.write('T5OR', CFG.sig, 'latin1');
  b.writeUInt16LE(cfg.cap, CFG.cap); b.writeUInt16LE(slotParas, CFG.slotParas); b.writeUInt16LE(nin, CFG.nin);
  b.writeUInt16LE(cfg.gameSigOff, CFG.gsOff); cfg.gameSig.copy(b, CFG.gsVal);
  b.writeUInt16LE(cfg.frameRandOff || 0, CFG.frOff); b.writeUInt32LE((cfg.frameRand || 0) >>> 0, CFG.frVal);
  b.writeUInt16LE(cfg.seedOff || 0, CFG.seedOff); b.writeUInt32LE((cfg.seed || 0) >>> 0, CFG.seedVal);
  b.writeUInt16LE(cfg.ranges.length * 8, CFG.nRangeBytes);
  cfg.ranges.forEach(([o, l, sg], i) => { b.writeUInt16LE(o, CFG.ranges + i * 8); b.writeUInt16LE(l, CFG.ranges + i * 8 + 2); b.writeUInt16LE(sg || 0, CFG.ranges + i * 8 + 4); });
  b.writeUInt16LE(cfg.startTick || 0, CFG.startTick); b.writeUInt16LE(cfg.pinOff || 0, CFG.pinOff); if (cfg.boot) { b.writeUInt16LE(cfg.boot.n01, CFG.n01); b.writeUInt16LE(cfg.boot.n04a, CFG.n04a); b.writeUInt16LE(cfg.boot.n04b, CFG.n04b); } b.writeUInt16LE(cfg.pinVal || 0, CFG.pinVal); b.writeUInt16LE(bufDelta, CFG.bufDelta); b.writeUInt16LE(keep, CFG.keepParas);
  if (cfg.inputs) Buffer.from(cfg.inputs).copy(b, CFG.table);
  return { cfg: b, slotParas, bufDelta, keep };
}

module.exports = { assemble, buildCfg, CFG };
