// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

// Reply mailbox. WRITE_KEY posts a nonce's text. READ_KEY reads it once.
// Pairing stores ciphertext only. PAIR_KEY is optional and never affects /health or /reply.
// KV expirationTtl minimum is 60 seconds (Cloudflare KV put docs). Replies use 600. Pairs use 60-300.

const MAX_BODY_BYTES = 20 * 1024;
const MAX_TEXT_BYTES = 16384;
const MAX_PAIR_BODY_BYTES = 8 * 1024;
const MAX_PAIR_CT_BYTES = 4096;
const NONCE_RE = /^[0-9a-f]{32}$/;
const PAIR_ID_RE = /^[A-Za-z0-9_-]{22}$/;
const TTL_SECONDS = 600;
const MISS_LIMIT = 10;
const MISS_WINDOW_MS = 60 * 1000;

// Per-isolate miss counts. Best effort in production. Deterministic under wrangler dev.
const missBuckets = new Map();

const JSON_HEADERS = {
  "Cache-Control": "no-store",
  "Content-Type": "application/json; charset=utf-8",
};

function json(status, obj) {
  const body = status === 204 ? null : JSON.stringify(obj);
  return new Response(body, { status, headers: JSON_HEADERS });
}

function plain(status, body, contentType) {
  return new Response(body, {
    status,
    headers: {
      "Cache-Control": "no-store",
      "Content-Type": contentType,
    },
  });
}

// crypto.subtle.timingSafeEqual is a Workers Web Crypto extension.
// It throws when the buffers differ in length. On a length mismatch, compare
// the input to itself so the call still runs, then fail the check.
// https://developers.cloudflare.com/workers/runtime-apis/web-crypto/
function timingSafeEqualString(presented, secret) {
  const encoder = new TextEncoder();
  const a = encoder.encode(presented);
  const b = encoder.encode(secret);
  const lengthsMatch = a.byteLength === b.byteLength;
  const equal = lengthsMatch
    ? crypto.subtle.timingSafeEqual(a, b)
    : !crypto.subtle.timingSafeEqual(a, a);
  return lengthsMatch && equal;
}

function keysOrNull(env) {
  const writeKey = typeof env.WRITE_KEY === "string" ? env.WRITE_KEY : "";
  const readKey = typeof env.READ_KEY === "string" ? env.READ_KEY : "";
  if (writeKey.length < 32 || readKey.length < 32 || writeKey === readKey) return null;
  return { writeKey, readKey };
}

// Absent, short, or equal to WRITE or READ means pairing is off. Reply routes stay up.
function pairKeyOrNull(env, keys) {
  const pairKey = typeof env.PAIR_KEY === "string" ? env.PAIR_KEY : "";
  if (pairKey.length < 32) return null;
  if (pairKey === keys.writeKey || pairKey === keys.readKey) return null;
  return pairKey;
}

function pairTtlSeconds(env) {
  const raw = env.PAIR_TTL_SECONDS;
  let n = 300;
  if (typeof raw === "number") n = raw;
  else if (typeof raw === "string" && raw.length > 0) n = Number(raw);
  if (!Number.isFinite(n)) n = 300;
  n = Math.floor(n);
  if (n < 60) n = 60;
  if (n > 300) n = 300;
  return n;
}

function clientIp(request) {
  const ip = request.headers.get("CF-Connecting-IP");
  if (typeof ip === "string" && ip.length > 0 && ip.length < 128) return ip;
  return "local";
}

function missLimited(ip, now) {
  const slot = missBuckets.get(ip);
  if (!slot || now - slot.start >= MISS_WINDOW_MS) return false;
  return slot.count >= MISS_LIMIT;
}

function noteMiss(ip, now) {
  let slot = missBuckets.get(ip);
  if (!slot || now - slot.start >= MISS_WINDOW_MS) {
    slot = { start: now, count: 0 };
    missBuckets.set(ip, slot);
  }
  slot.count += 1;
  if (missBuckets.size > 1000) {
    for (const [key, value] of missBuckets) {
      if (now - value.start >= MISS_WINDOW_MS) missBuckets.delete(key);
    }
  }
}

function b64urlBytes(value) {
  if (typeof value !== "string" || value.length < 1 || value.length > 8192) return null;
  if (!/^[A-Za-z0-9_-]+$/.test(value)) return null;
  const pad = "=".repeat((4 - (value.length % 4)) % 4);
  const b64 = value.replace(/-/g, "+").replace(/_/g, "/") + pad;
  let bin;
  try {
    bin = atob(b64);
  } catch (err) {
    return null;
  }
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i) & 0xff;
  return out;
}

function boxOrStatus(body) {
  if (body === null || typeof body !== "object" || Array.isArray(body)) return { status: 400 };
  const keys = Object.keys(body);
  if (keys.length !== 4) return { status: 400 };
  if (body.v !== 1) return { status: 400 };
  const iv = b64urlBytes(body.iv);
  const ct = b64urlBytes(body.ct);
  const mac = b64urlBytes(body.mac);
  if (!iv || iv.length !== 16) return { status: 400 };
  if (!mac || mac.length !== 32) return { status: 400 };
  if (!ct) return { status: 400 };
  if (ct.length > MAX_PAIR_CT_BYTES) return { status: 413 };
  return { box: { v: 1, iv: body.iv, ct: body.ct, mac: body.mac } };
}

function presentedToken(request) {
  const header = request.headers.get("Authorization") || "";
  const prefix = "Bearer ";
  if (header.slice(0, prefix.length) !== prefix) return "";
  return header.slice(prefix.length);
}

async function postReply(request, env) {
  const declared = request.headers.get("content-length");
  if (declared !== null && declared !== "" && Number(declared) > MAX_BODY_BYTES) {
    return json(413, {});
  }

  const buf = await request.arrayBuffer();
  if (buf.byteLength > MAX_BODY_BYTES) return json(413, {});

  let body;
  try {
    body = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(buf));
  } catch (err) {
    return json(400, {});
  }
  if (body === null || typeof body !== "object" || Array.isArray(body)) return json(400, {});
  if (typeof body.nonce !== "string" || !NONCE_RE.test(body.nonce)) return json(400, {});
  if (typeof body.text !== "string") return json(400, {});

  const textBytes = new TextEncoder().encode(body.text).byteLength;
  if (textBytes < 1) return json(400, {});
  if (textBytes > MAX_TEXT_BYTES) return json(413, {});

  await env.REPLIES.put("r:" + body.nonce, body.text, { expirationTtl: TTL_SECONDS });
  return json(204, {});
}

async function getReply(nonce, env, ctx) {
  const key = "r:" + nonce;
  const value = await env.REPLIES.get(key);
  if (value === null) return json(404, {});
  ctx.waitUntil(env.REPLIES.delete(key));
  return json(200, { text: value });
}

async function readPairBody(request) {
  const declared = request.headers.get("content-length");
  if (declared !== null && declared !== "" && Number(declared) > MAX_PAIR_BODY_BYTES) {
    return { status: 413 };
  }
  const buf = await request.arrayBuffer();
  if (buf.byteLength > MAX_PAIR_BODY_BYTES) return { status: 413 };
  let body;
  try {
    body = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(buf));
  } catch (err) {
    return { status: 400 };
  }
  return { body };
}

async function postPair(request, env, pairKey) {
  if (!timingSafeEqualString(presentedToken(request), pairKey)) return json(401, {});
  const read = await readPairBody(request);
  if (read.status) return json(read.status, {});
  const body = read.body;
  if (body === null || typeof body !== "object" || Array.isArray(body)) return json(400, {});
  if (typeof body.id !== "string" || !PAIR_ID_RE.test(body.id)) return json(400, {});
  const checked = boxOrStatus(body.box);
  if (checked.status) return json(checked.status, {});
  const key = "p:" + body.id;
  const existing = await env.REPLIES.get(key);
  if (existing !== null) return json(409, {});
  await env.REPLIES.put(key, JSON.stringify(checked.box), { expirationTtl: pairTtlSeconds(env) });
  return json(201, {});
}

async function getPair(id, env, ctx, request) {
  if (!PAIR_ID_RE.test(id)) return json(400, {});
  const ip = clientIp(request);
  const now = Date.now();
  if (missLimited(ip, now)) return json(429, {});
  const key = "p:" + id;
  const value = await env.REPLIES.get(key);
  if (value === null) {
    noteMiss(ip, now);
    return json(404, {});
  }
  let box;
  try {
    box = JSON.parse(value);
  } catch (err) {
    noteMiss(ip, now);
    return json(404, {});
  }
  ctx.waitUntil(env.REPLIES.delete(key));
  return json(200, { box });
}

async function deletePair(request, env, pairKey, id) {
  if (!timingSafeEqualString(presentedToken(request), pairKey)) return json(401, {});
  if (!PAIR_ID_RE.test(id)) return json(400, {});
  await env.REPLIES.delete("p:" + id);
  return json(204, {});
}

export default {
  async fetch(request, env, ctx) {
    try {
      const keys = keysOrNull(env);
      if (!keys) return json(500, { error: "misconfigured" });

      const path = new URL(request.url).pathname;

      if (request.method === "GET" && path === "/health") {
        return plain(200, "ok", "text/plain; charset=utf-8");
      }

      if (request.method === "POST" && path === "/reply") {
        if (!timingSafeEqualString(presentedToken(request), keys.writeKey)) return json(401, {});
        return await postReply(request, env);
      }

      if (request.method === "GET" && path.startsWith("/reply/")) {
        if (!timingSafeEqualString(presentedToken(request), keys.readKey)) return json(401, {});
        const nonce = decodeURIComponent(path.slice("/reply/".length));
        if (!NONCE_RE.test(nonce)) return json(400, {});
        return await getReply(nonce, env, ctx);
      }

      if (path === "/pair" || path.startsWith("/pair/")) {
        const pairKey = pairKeyOrNull(env, keys);
        if (!pairKey) return json(503, { error: "pairing-off" });
        if (request.method === "POST" && path === "/pair") return await postPair(request, env, pairKey);
        const id = decodeURIComponent(path.slice("/pair/".length));
        if (request.method === "GET" && path.startsWith("/pair/")) return await getPair(id, env, ctx, request);
        if (request.method === "DELETE" && path.startsWith("/pair/")) return await deletePair(request, env, pairKey, id);
        return json(404, {});
      }

      return json(404, {});
    } catch (err) {
      const name = err && err.name ? err.name : "Error";
      console.log("err=" + name);
      return json(500, {});
    }
  },
};
