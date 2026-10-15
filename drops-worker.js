/**
 * DROPS Loader — Cloudflare Worker (/admin/* endpoints)
 *
 * Instalação:
 *   1. Worker → Settings → Variables → Environment Variables
 *      ADMIN_SECRET = <sua chave secreta de 32+ caracteres>
 *   2. D1 → Create database → "drops-admin"
 *      → Quebra de esquema (run SQL):
 *        CREATE TABLE IF NOT EXISTS keys (
 *          id INTEGER PRIMARY KEY AUTOINCREMENT,
 *          hash TEXT NOT NULL,
 *          expires_at INTEGER NOT NULL,
 *          product TEXT NOT NULL,
 *          banned INTEGER NOT NULL DEFAULT 0,
 *          hwid TEXT NOT NULL DEFAULT '',
 *          note TEXT NOT NULL DEFAULT '',
 *          created_at INTEGER NOT NULL DEFAULT (strftime('%s','now')*1000)
 *        );
 *        CREATE INDEX IF NOT EXISTS idx_keys_hash ON keys(hash);
 *        CREATE INDEX IF NOT EXISTS idx_keys_expires ON keys(expires_at);
 *        CREATE TABLE IF NOT EXISTS trials (
 *          id INTEGER PRIMARY KEY AUTOINCREMENT,
 *          ip TEXT NOT NULL,
 *          at INTEGER NOT NULL
 *        );
 *   3. Deploy → publish
 *
 * Contrato (já está no drops-admin.html):
 *   GET  /admin/stats        → {ok, active, expired, banned, trials, codes_pending}
 *   GET  /admin/keys?limit=N → {ok, keys:[{h, expires_at, product, banned, hwid, note}]}
 *   GET  /admin/search?q=TEXTO → {ok, keys:[...]}  (note ou trecho do hash)
 *   POST /admin/ban {hash}   → {ok:true}
 *   POST /admin/unban {hash} → {ok:true}
 *   POST /admin/key {hash, expires_at, product, note} → {ok:true}
 *   POST /admin/code {hash, days, product, note} → {ok:true}
 *   GET  /admin/trials       → {ok, trials:[{ip, at}]}
 */
export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    // ── Autenticação ──────────────────────────────────────────────────────────
    const adminSecret = env.ADMIN_SECRET;
    const auth = request.headers.get('x-admin');

    if (!adminSecret || auth !== adminSecret) {
      return new Response(
        JSON.stringify({ ok: false, message: 'Acesso negado.' }),
        { status: 401, headers: { 'Content-Type': 'application/json' } }
      );
    }

    const url = new URL(request.url);
    const path = url.pathname;

    try {
      switch (path) {
        case '/admin/stats':
          return stats(request, ctx, env);
        case '/admin/keys':
          return keys(request, ctx, env);
        case '/admin/search':
          return search(request, ctx, env);
        case '/admin/ban':
          return ban(request, ctx, env);
        case '/admin/unban':
          return unban(request, ctx, env);
        case '/admin/key':
          return createKey(request, ctx, env);
        case '/admin/code':
          return createCode(request, ctx, env);
        case '/admin/trials':
          return trials(request, ctx, env);
        default:
          return notFound();
      }
    } catch (error) {
      return errorResponse('Ocorreu um erro interno.');
    }
  },
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function json<T = unknown>(data: T, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function notFound(): Response {
  return json({ ok: false, message: 'Endpoint não encontrado.' }, 404);
}

function errorResponse(message: string, status = 400): Response {
  return json({ ok: false, message }, status);
}

function okResponse(message = ''): Response {
  return json({ ok: true, ...(message ? { message } : {}) });
}

// ── D1 helpers ────────────────────────────────────────────────────────────────

async function withKeys(
  env: Env,
  handler: (db: D1Database) => Promise<{ rows: unknown[]; err?: string }>
): Promise<{ rows: unknown[]; err?: string }> {
  if (!env.DB) throw new Error('DB não configurada no Worker (D1 binding).');
  const db = env.DB;
  return handler(db);
}

// ── Endpoints ────────────────────────────────────────────────────────────────

async function stats(request: Request, ctx: ExecutionContext, env: Env): Promise<Response> {
  await withKeys(env, async (db) => {
    if (db) {
      await db
        .prepare(
          `CREATE TABLE IF NOT EXISTS keys (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            hash TEXT NOT NULL,
            expires_at INTEGER NOT NULL,
            product TEXT NOT NULL,
            banned INTEGER NOT NULL DEFAULT 0,
            hwid TEXT NOT NULL DEFAULT '',
            note TEXT NOT NULL DEFAULT '',
            created_at INTEGER NOT NULL DEFAULT (strftime('%s','now')*1000)
          )`
        )
        .run();
      await db
        .prepare(
          `CREATE TABLE IF NOT EXISTS trials (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            ip TEXT NOT NULL,
            at INTEGER NOT NULL
          )`
        )
        .run();
    }
  });

  const data = await withKeys(env, async (db) => {
    if (!db) return { active: 0, expired: 0, banned: 0, trials: 0, codes_pending: 0 };

    const now = Date.now();
    const keyRows = await db
      .prepare('SELECT banned, expires_at FROM keys')
      .all()
      .then((r) => r.results || [])
      .catch(() => []);

    let active = 0;
    let expired = 0;
    let banned = 0;

    for (const row of keyRows) {
      const bannedFlag = row.banned === 1 || row.banned === true;
      const expires = row.expires_at ?? 0;
      const isExpired = Number.isFinite(expires) && expires <= now;

      if (bannedFlag) banned += 1;
      else if (isExpired) expired += 1;
      else active += 1;
    }

    const trialRows = await db
      .prepare('SELECT COUNT(*) AS c FROM trials')
      .all()
      .then((r) => r.results || [])
      .catch(() => []);
    const trials = trialRows.length > 0 ? (trialRows[0].c as number) : 0;

    // Códigos pendentes = rows em keys onde o hash não existe em uma tabela de códigos ativados.
    // Como não temos tabela de códigos separada, estimamos codes_pending = 0 por padrão.
    // O painel exibe '—' se o valor for 0. O Worker pode ser expandido para rastrear códigos.
    const codesPending = 0;

    return { active, expired, banned, trials, codes_pending: codesPending };
  });

  return json({ ok: true, ...data });
}

async function keys(request: Request, ctx: ExecutionContext, env: Env): Promise<Response> {
  const url = new URL(request.url);
  const limitParam = url.searchParams.get('limit');
  const limit = limitParam ? Math.min(Math.max(Number(limitParam), 1), 500) : 100;

  const data = await withKeys(env, async (db) => {
    if (!db) return { ok: true, keys: [] };

    const rows = await db
      .prepare('SELECT hash, expires_at, product, banned, hwid, note FROM keys ORDER BY id DESC LIMIT ?')
      .bind(String(limit))
      .all()
      .then((r) => r.results || [])
      .catch(() => []);

    return {
      ok: true,
      keys: rows.map((row) => ({
        h: row.hash?.slice(0, 8) || '—',
        expires_at: row.expires_at ?? null,
        product: row.product || '—',
        banned: row.banned === 1 || row.banned === true,
        hwid: row.hwid || null,
        note: row.note || null,
      })),
    };
  });

  return json(data);
}

async function search(request: Request, ctx: ExecutionContext, env: Env): Promise<Response> {
  const url = new URL(request.url);
  const q = url.searchParams.get('q') || '';
  const limit = 100;

  const data = await withKeys(env, async (db) => {
    if (!db) return { ok: true, keys: [] };

    const pattern = `%${q}%`;
    const rows = await db
      .prepare(
        `SELECT hash, expires_at, product, banned, hwid, note FROM keys
         WHERE note LIKE ? OR hash LIKE ?
         ORDER BY id DESC
         LIMIT ?`
      )
      .bind(pattern, pattern, String(limit))
      .all()
      .then((r) => r.results || [])
      .catch(() => []);

    return {
      ok: true,
      keys: rows.map((row) => ({
        h: row.hash?.slice(0, 8) || '—',
        expires_at: row.expires_at ?? null,
        product: row.product || '—',
        banned: row.banned === 1 || row.banned === true,
        hwid: row.hwid || null,
        note: row.note || null,
      })),
    };
  });

  return json(data);
}

async function ban(request: Request, ctx: ExecutionContext, env: Env): Promise<Response> {
  const body = await request.json().catch(() => ({}));
  const hash = (body.hash ?? '').toString().trim();
  if (!hash) return errorResponse('hash obrigatória.', 400);

  let result: { ok: boolean; message?: string } = { ok: false };
  await withKeys(env, async (db) => {
    if (!db) {
      result = { ok: false, message: 'DB não configurada.' };
      return;
    }
    const updated = await db
      .prepare('UPDATE keys SET banned = 1 WHERE hash = ?')
      .bind(hash)
      .run();
    if (updated.changes === 0) {
      result = { ok: false, message: 'key não encontrada.' };
      return;
    }
    result = { ok: true };
  });

  return json(result);
}

async function unban(request: Request, ctx: ExecutionContext, env: Env): Promise<Response> {
  const body = await request.json().catch(() => ({}));
  const hash = (body.hash ?? '').toString().trim();
  if (!hash) return errorResponse('hash obrigatória.', 400);

  let result: { ok: boolean; message?: string } = { ok: false };
  await withKeys(env, async (db) => {
    if (!db) {
      result = { ok: false, message: 'DB não configurada.' };
      return;
    }
    const updated = await db
      .prepare('UPDATE keys SET banned = 0 WHERE hash = ?')
      .bind(hash)
      .run();
    if (updated.changes === 0) {
      result = { ok: false, message: 'key não encontrada.' };
      return;
    }
    result = { ok: true };
  });

  return json(result);
}

async function createKey(request: Request, ctx: ExecutionContext, env: Env): Promise<Response> {
  let body: { hash?: unknown; expires_at?: unknown; product?: unknown; note?: unknown };
  try {
    body = await request.json();
  } catch {
    return errorResponse('corpo inválido (JSON esperado).', 400);
  }

  const hash = (body.hash ?? '').toString().trim();
  const rawExpires = body.expires_at;
  const expiresAt = Number(rawExpires);
  const product = (body.product ?? '').toString().trim();
  const note = (body.note ?? '').toString().trim();

  if (!hash) return errorResponse('hash obrigatória.', 400);
  if (!Number.isFinite(expiresAt) || expiresAt <= Date.now()) {
    return errorResponse('expires_at inválido (ms epoch futuro).', 400);
  }
  if (!product) return errorResponse('product obrigatório.', 400);

  const result: { ok: boolean; message?: string } = { ok: false };
  await withKeys(env, async (db) => {
    if (!db) {
      result = { ok: false, message: 'DB não configurada.' };
      return;
    }
    const now = Date.now();
    const r = await db
      .prepare(
        `INSERT INTO keys (hash, expires_at, product, banned, hwid, note, created_at)
         VALUES (?, ?, ?, 0, '', ?, ?)`
      )
      .bind(hash, expiresAt, product, note, now)
      .run();
    if (r.meta?.success !== true) {
      result = { ok: false, message: 'falha ao inserir.' };
      return;
    }
    result = { ok: true };
  });

  return json(result);
}

async function createCode(request: Request, ctx: ExecutionContext, env: Env): Promise<Response> {
  let body: { hash?: unknown; days?: unknown; product?: unknown; note?: unknown };
  try {
    body = await request.json();
  } catch {
    return errorResponse('corpo inválido (JSON esperado).', 400);
  }

  const hash = (body.hash ?? '').toString().trim();
  const rawDays = body.days;
  const days = Number(rawDays);
  const product = (body.product ?? '').toString().trim();
  const note = (body.note ?? '').toString().trim();

  if (!hash) return errorResponse('hash obrigatória.', 400);
  if (!Number.isInteger(days) || days < 1 || days > 36500) {
    return errorResponse('days deve ser um inteiro entre 1 e 36500.', 400);
  }
  if (!product) return errorResponse('product obrigatório.', 400);

  const expiresAt = Date.now() + days * 86400000;

  const result: { ok: boolean; message?: string } = { ok: false };
  await withKeys(env, async (db) => {
    if (!db) {
      result = { ok: false, message: 'DB não configurada.' };
      return;
    }
    const now = Date.now();
    const r = await db
      .prepare(
        `INSERT INTO keys (hash, expires_at, product, banned, hwid, note, created_at)
         VALUES (?, ?, ?, 0, '', ?, ?)`
      )
      .bind(hash, expiresAt, product, note, now)
      .run();
    if (r.meta?.success !== true) {
      result = { ok: false, message: 'falha ao inserir.' };
      return;
    }
    result = { ok: true };
  });

  return json(result);
}

async function trials(request: Request, ctx: ExecutionContext, env: Env): Promise<Response> {
  const data = await withKeys(env, async (db) => {
    if (!db) return { ok: true, trials: [] };

    const rows = await db
      .prepare('SELECT ip, at FROM trials ORDER BY id DESC LIMIT 1000')
      .all()
      .then((r) => r.results || [])
      .catch(() => []);

    return {
      ok: true,
      trials: rows.map((row) => ({
        ip: row.ip || '—',
        at: row.at ?? null,
      })),
    };
  });

  return json(data);
}
