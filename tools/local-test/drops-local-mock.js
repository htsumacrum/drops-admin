// ── drops-local-mock.js ─────────────────────────────────────────────────────
// Memória: keys[], trials[], ADMIN_SECRET='segredo-teste-1234567890abcdef'
'use strict';
const http = require('http');
const url = require('url');

const PORT = 3000;
const ADMIN_SECRET = 'segredo-teste-1234567890abcdef';

let keys = [
  { hash: 'a1b2c3d4e5f60718', expires_at: Date.now() + 7*86400000, product: 'paladins', banned: 0, hwid: '1234567890abcdef', note: 'teste-001', created_at: Date.now() - 86400000 },
  { hash: 'f0e1d2c3b4a59607', expires_at: Date.now() - 3600000, product: 'paladins', banned: 1, hwid: 'abcdef1234567890', note: 'banida-002', created_at: Date.now() - 172800000 },
  { hash: '1122334455667788', expires_at: Date.now() + 30*86400000, product: 'paladins', banned: 0, hwid: null, note: 'novidade-003', created_at: Date.now() - 3600000 },
];
let trials = [
  { ip: '192.168.1.100', at: Date.now() - 300000 },
  { ip: '10.0.0.55', at: Date.now() - 120000 },
];
const STATS = { active: 2, expired: 1, banned: 1, trials: 2, codes_pending: 0 };

function send(res, status, data) {
  res.writeHead(status, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(data));
}

const server = http.createServer((req, res) => {
  const parsed = url.parse(req.url || '', true);
  const pathname = parsed.pathname;

  // health
  if (pathname === '/health') return send(res, 200, { ok: true, service: 'drops-local-mock' });

  // auth
  const auth = req.headers['x-admin'];
  if (!auth) return send(res, 401, { ok: false, message: 'Acesso negado.' });
  if (auth !== ADMIN_SECRET) return send(res, 401, { ok: false, message: 'Acesso negado.' });

  // GET /admin/stats
  if (pathname === '/admin/stats') return send(res, 200, { ok: true, ...STATS });

  // GET /admin/keys
  if (pathname === '/admin/keys') {
    const limit = Math.min(Math.max(parsed.query.limit || 100, 1), 500);
    const result = { ok: true,
      keys: keys.slice(0, limit).map(k => ({
        h: k.hash.slice(0, 8),
        expires_at: k.expires_at,
        product: k.product,
        banned: k.banned === 1,
        hwid: k.hwid || null,
        note: k.note || null,
      })) };
    return send(res, 200, result);
  }

  // GET /admin/search?q=
  if (pathname === '/admin/search') {
    const q = parsed.query.q || '';
    const rows = keys.filter(k =>
      (k.note||'').toLowerCase().includes(q.toLowerCase()) ||
      (k.hash||'').toLowerCase().includes(q.toLowerCase())
    );
    return send(res, 200, { ok: true, keys: rows.map(k => ({
      h: k.hash.slice(0, 8),
      expires_at: k.expires_at,
      product: k.product,
      banned: k.banned === 1,
      hwid: k.hwid || null,
      note: k.note || null,
    })) });
  }

  // POST /admin/ban
  if (pathname === '/admin/ban' && req.method === 'POST') {
    let body = '';
    req.on('data', c => body += c);
    req.on('end', () => {
      try { body = JSON.parse(body); } catch { body = {}; }
      const hash = (body.hash || '').toString().trim();
      const target = keys.find(k => k.hash === hash);
      if (!target) return send(res, 400, { ok: false, message: 'key não encontrada.' });
      target.banned = 1;
      send(res, 200, { ok: true });
    });
    req.on('error', e => send(res, 500, { ok: false, message: 'Erro interno.' }));
    return;
  }

  // POST /admin/unban
  if (pathname === '/admin/unban' && req.method === 'POST') {
    let body = '';
    req.on('data', c => body += c);
    req.on('end', () => {
      try { body = JSON.parse(body); } catch { body = {}; }
      const hash = (body.hash || '').toString().trim();
      const target = keys.find(k => k.hash === hash);
      if (!target) return send(res, 400, { ok: false, message: 'key não encontrada.' });
      target.banned = 0;
      send(res, 200, { ok: true });
    });
    req.on('error', e => send(res, 500, { ok: false, message: 'Erro interno.' }));
    return;
  }

  // POST /admin/key
  if (pathname === '/admin/key' && req.method === 'POST') {
    let body = '';
    req.on('data', c => body += c);
    req.on('end', () => {
      try { body = JSON.parse(body); } catch { body = {}; }
      const hash = (body.hash || '').toString().trim();
      const expiresAt = Number(body.expires_at);
      const product = (body.product || '').toString().trim();
      const note = (body.note || '').toString().trim();
      if (!hash || !Number.isFinite(expiresAt) || !product) return send(res, 400, { ok: false, message: 'parâmetros inválidos.' });
      keys.push({ hash, expires_at: expiresAt, product, banned: 0, hwid: null, note, created_at: Date.now() });
      send(res, 200, { ok: true });
    });
    req.on('error', e => send(res, 500, { ok: false, message: 'Erro interno.' }));
    return;
  }

  // POST /admin/code
  if (pathname === '/admin/code' && req.method === 'POST') {
    let body = '';
    req.on('data', c => body += c);
    req.on('end', () => {
      try { body = JSON.parse(body); } catch { body = {}; }
      const hash = (body.hash || '').toString().trim();
      const days = Number(body.days);
      const product = (body.product || '').toString().trim();
      const note = (body.note || '').toString().trim();
      if (!hash || !Number.isInteger(days) || days < 1 || days > 36500 || !product) return send(res, 400, { ok: false, message: 'parâmetros inválidos.' });
      const expiresAt = Date.now() + days * 86400000;
      keys.push({ hash, expires_at: expiresAt, product, banned: 0, hwid: null, note, created_at: Date.now() });
      send(res, 200, { ok: true });
    });
    req.on('error', e => send(res, 500, { ok: false, message: 'Erro interno.' }));
    return;
  }

  // GET /admin/trials
  if (pathname === '/admin/trials') return send(res, 200, { ok: true, trials: trials.map(t => ({ ip: t.ip, at: t.at })) });

  send(res, 404, { ok: false, message: 'Endpoint não encontrado.' });
});

server.listen(PORT, () => {
  console.log(`DROPS local mock → http://localhost:${PORT}`);
  console.log('Secret de teste: ' + ADMIN_SECRET);
});
