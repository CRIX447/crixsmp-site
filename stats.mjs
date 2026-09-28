// CRIX SMP live stats: who's online, the leaderboards and the current event.
//
//   POST /api/stats   your Minecraft server sends fresh stats (needs the key, see README "Live stats")
//   GET  /api/stats   the website reads them
//
// Needs two things set up in Vercel (Project -> Storage / Settings -> Environment Variables):
//   - an Upstash Redis database connected to this project (adds KV_REST_API_URL + KV_REST_API_TOKEN)
//   - CRIX_WEB_KEY: the same key as in the CRIX Web pack's scripts/config.js on your server
import { timingSafeEqual } from "node:crypto";

const KEY = "crix:stats";
const BOARDS = ["balance", "kills", "playtime", "shards"];

function storage() {
  const url = process.env.KV_REST_API_URL || process.env.UPSTASH_REDIS_REST_URL;
  const token = process.env.KV_REST_API_TOKEN || process.env.UPSTASH_REDIS_REST_TOKEN;
  return url && token ? { url: url.replace(/\/+$/, ""), token } : null;
}

async function redis(db, command) {
  const r = await fetch(db.url, {
    method: "POST",
    headers: { Authorization: `Bearer ${db.token}`, "Content-Type": "application/json" },
    body: JSON.stringify(command),
  });
  const j = await r.json().catch(() => ({}));
  if (!r.ok || j.error) throw new Error(j.error || `storage answered ${r.status}`);
  return j.result;
}

function reply(body, status = 200, cache = "no-store") {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": cache },
  });
}

function sameKey(a, b) {
  const x = Buffer.from(String(a)), y = Buffer.from(String(b));
  return x.length === y.length && timingSafeEqual(x, y);
}

// keep only what the website shows, with sane sizes (player names are Xbox gamertags)
const str = (v, max) => (typeof v === "string" ? v.replace(/[\u0000-\u001f]/g, "").slice(0, max) : "");
const numb = (v) => (typeof v === "number" && Number.isFinite(v) ? Math.max(0, Math.floor(v)) : 0);
function clean(body) {
  const out = { v: 1, pack: str(body.pack, 20), t: numb(body.t), count: numb(body.count), total: numb(body.total), online: [], boards: {}, event: null };
  if (Array.isArray(body.online))
    out.online = body.online.slice(0, 300).map((p) => ({ n: str(p?.n, 32), r: str(p?.r, 16), s: str(p?.s, 16) })).filter((p) => p.n);
  for (const k of BOARDS)
    out.boards[k] = Array.isArray(body.boards?.[k])
      ? body.boards[k].slice(0, 25).filter((row) => Array.isArray(row) && typeof row[0] === "string").map((row) => [str(row[0], 32), numb(row[1])])
      : [];
  if (body.event && typeof body.event === "object")
    out.event = { name: str(body.event.name, 40), status: str(body.event.status, 80), until: numb(body.event.until) };
  return out;
}

export async function GET() {
  const db = storage();
  const setup = { storage: !!db, key: !!process.env.CRIX_WEB_KEY };
  if (!db) return reply({ ok: false, reason: "no-storage", setup }, 200, "public, s-maxage=60");
  try {
    const raw = await redis(db, ["GET", KEY]);
    if (!raw) return reply({ ok: false, reason: "waiting", setup }, 200, "public, s-maxage=15");
    return reply({ ok: true, now: Date.now(), ...JSON.parse(raw) }, 200, "public, s-maxage=15, stale-while-revalidate=45");
  } catch (e) {
    return reply({ ok: false, reason: "storage-error", error: String(e.message || e), setup }, 200, "public, s-maxage=15");
  }
}

export async function POST(request) {
  const want = process.env.CRIX_WEB_KEY;
  if (!want) return reply({ ok: false, error: "CRIX_WEB_KEY isn't set on Vercel yet" }, 500);
  const got = (request.headers.get("authorization") || "").replace(/^Bearer\s+/i, "").trim();
  if (!sameKey(got, want)) return reply({ ok: false, error: "wrong key" }, 401);
  const db = storage();
  if (!db) return reply({ ok: false, error: "no storage: add Upstash Redis in Vercel -> Storage, then redeploy" }, 500);
  const text = await request.text();
  if (text.length > 200_000) return reply({ ok: false, error: "too big" }, 413);
  let body;
  try {
    body = JSON.parse(text);
  } catch {
    return reply({ ok: false, error: "not JSON" }, 400);
  }
  const data = clean(body);
  data.received = Date.now();
  try {
    await redis(db, ["SET", KEY, JSON.stringify(data)]);
  } catch (e) {
    return reply({ ok: false, error: "storage: " + String(e.message || e) }, 502);
  }
  return reply({ ok: true, received: data.received });
}
