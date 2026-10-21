// Minimal server for the DOSBox-X MCP control channel (docs/ORACLE.md section 3a).
// DOSBox-X is the *client*: it connects to 127.0.0.1:<port>; we send "REQ <id> EXEC <cmd>" lines and
// receive "BEGIN <id> OK|ERR ... END <id>" blocks.
'use strict';
const net = require('net');

class Mcp {
  constructor(port = 7777) {
    this.port = port;
    this.id = 0;
    this.pending = new Map();
    this.buf = '';
    this.sock = null;
    this.unsolicited = [];
  }

  listen() {
    return new Promise(resolve => {
      this.server = net.createServer(s => {
        this.sock = s;
        s.setNoDelay(true);
        s.on('data', d => this._onData(d.toString('latin1')));
        s.on('error', () => {});
        s.on('close', () => { this.sock = null; for (const p of this.pending.values()) p.reject(new Error('connection closed')); this.pending.clear(); });
        if (this._conn) this._conn();
      });
      this.server.listen(this.port, '127.0.0.1', resolve);
    });
  }

  waitConnected() { return new Promise(r => { if (this.sock) r(); else this._conn = r; }); }

  _onData(text) {
    this.buf += text;
    for (;;) {
      const m = /BEGIN (\d+) (OK|ERR)\n([\s\S]*?)END \1\n/.exec(this.buf);
      if (!m) break;
      this.buf = this.buf.slice(m.index + m[0].length);
      const p = this.pending.get(Number(m[1]));
      if (p) { this.pending.delete(Number(m[1])); p.resolve({ ok: m[2] === 'OK', text: m[3] }); }
    }
  }

  exec(cmd, timeoutMs = 10000) {
    return new Promise((resolve, reject) => {
      if (!this.sock) return reject(new Error('not connected'));
      const id = ++this.id;
      const t = setTimeout(() => { this.pending.delete(id); reject(new Error('timeout: ' + cmd)); }, timeoutMs);
      this.pending.set(id, { resolve: r => { clearTimeout(t); resolve(r); }, reject });
      this.sock.write(`REQ ${id} EXEC ${cmd}\n`);
    });
  }

  close() { try { this.sock && this.sock.destroy(); this.server && this.server.close(); } catch { /* ignore */ } }
}

module.exports = { Mcp };
