// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

// Reply mailbox. WRITE_KEY posts a nonce's text. READ_KEY reads it once.
// Pairing stores ciphertext only. PAIR_KEY is optional and never affects /health or /reply.
// POST /ping sends one email through the EMAIL binding. It does not touch KV or the miss limiter.
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

function coloOf(request) {
  const cf = request && request.cf;
  const colo = cf && typeof cf.colo === "string" ? cf.colo : "";
  if (/^[A-Za-z0-9]{1,8}$/.test(colo)) return colo;
  return "";
}

// tid is 8 hex chars. The line never includes message text or a key.
function logTrace(nonce, ev, t, colo, hit) {
  const tid = typeof nonce === "string" && /^[0-9a-f]{8}/.test(nonce) ? nonce.slice(0, 8) : "--------";
  let line = "trace tid=" + tid + " ev=" + ev + " t=" + t + " colo=" + (colo || "");
  if (hit === 0 || hit === 1) line += " hit=" + hit;
  console.log(line);
}

const RT_WINDOW_MS = 24 * 60 * 60 * 1000;
const RT_KEY_RE = /^[A-Za-z_][A-Za-z0-9_]{0,40}$/;

// rt is optional. Absent is fine. Present must be at most 3 finite numbers
// within a day of the Worker clock. A bad rt rejects the whole POST.
function copyRt(rt, now) {
  if (rt === undefined) return { ok: true, rt: null };
  if (rt === null || typeof rt !== "object" || Array.isArray(rt)) return { ok: false };
  const keys = Object.keys(rt);
  if (keys.length > 3) return { ok: false };
  const out = {};
  for (let i = 0; i < keys.length; i++) {
    const key = keys[i];
    if (!RT_KEY_RE.test(key) || key === "text") return { ok: false };
    const n = rt[key];
    if (typeof n !== "number" || !Number.isFinite(n)) return { ok: false };
    if (Math.abs(n - now) > RT_WINDOW_MS) return { ok: false };
    out[key] = n;
  }
  return { ok: true, rt: keys.length === 0 ? null : out };
}

function replyEnvelope(text, tReplyRx, colo, rt) {
  const tr = { t_reply_rx: tReplyRx };
  if (colo) tr.colo_reply = colo;
  if (rt) tr.rt = rt;
  return JSON.stringify({ text: text, tr: tr });
}

// New rows are {"text","tr"}. Older rows are the raw text. A JSON value with
// no string text stays the raw text, so a reply that merely starts with "{" is kept.
function unpackReply(value) {
  if (typeof value !== "string") return { text: "", tr: null };
  if (value.length > 0 && value.charCodeAt(0) === 0x7b) {
    try {
      const parsed = JSON.parse(value);
      if (parsed && typeof parsed === "object" && !Array.isArray(parsed) && typeof parsed.text === "string") {
        const tr = parsed.tr && typeof parsed.tr === "object" && !Array.isArray(parsed.tr) ? parsed.tr : null;
        return { text: parsed.text, tr: tr };
      }
    } catch (err) {
      // not an envelope
    }
  }
  return { text: value, tr: null };
}

function traceForHit(tr, tServed, coloGet) {
  const trace = {};
  if (tr) {
    if (typeof tr.t_reply_rx === "number" && Number.isFinite(tr.t_reply_rx)) trace.t_reply_rx = tr.t_reply_rx;
    if (typeof tr.colo_reply === "string" && /^[A-Za-z0-9]{1,8}$/.test(tr.colo_reply)) trace.colo_reply = tr.colo_reply;
    if (tr.rt && typeof tr.rt === "object" && !Array.isArray(tr.rt)) {
      const rt = {};
      const keys = Object.keys(tr.rt);
      for (let i = 0; i < keys.length && i < 3; i++) {
        const key = keys[i];
        const n = tr.rt[key];
        if (key !== "text" && RT_KEY_RE.test(key) && typeof n === "number" && Number.isFinite(n)) rt[key] = n;
      }
      if (Object.keys(rt).length > 0) trace.rt = rt;
    }
  }
  trace.t_reply_served = tServed;
  trace.colo_get = coloGet;
  return trace;
}

function isTextPlain(request) {
  const raw = request.headers.get("content-type") || "";
  const semi = raw.indexOf(";");
  const type = (semi >= 0 ? raw.slice(0, semi) : raw).trim().toLowerCase();
  return type === "text/plain";
}

function missLimited(ip, now) {
  const slot = missBuckets.get(ip);
  if (!slot || now - slot.start >= MISS_WINDOW_MS) return false;
  // 429 only once more than 10 misses are already recorded. The 11th miss is still 404.
  return slot.count > MISS_LIMIT;
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

async function putReply(env, nonce, text, tReplyRx, colo, rt) {
  await env.REPLIES.put("r:" + nonce, replyEnvelope(text, tReplyRx, colo, rt), { expirationTtl: TTL_SECONDS });
  logTrace(nonce, "put", tReplyRx, colo);
}

async function postReply(request, env) {
  const tReplyRx = Date.now();
  const colo = coloOf(request);
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

  const checked = copyRt(body.rt, Date.now());
  if (!checked.ok) return json(400, {});

  await putReply(env, body.nonce, body.text, tReplyRx, colo, checked.rt);
  return json(204, {});
}

// Body is the reply text. Same auth and size limits as JSON POST /reply. No rt.
async function postReplyPlain(request, env, nonce) {
  const tReplyRx = Date.now();
  const colo = coloOf(request);
  if (!isTextPlain(request)) return json(400, {});

  const declared = request.headers.get("content-length");
  if (declared !== null && declared !== "" && Number(declared) > MAX_BODY_BYTES) {
    return json(413, {});
  }

  const buf = await request.arrayBuffer();
  if (buf.byteLength > MAX_BODY_BYTES) return json(413, {});

  let text;
  try {
    text = new TextDecoder("utf-8", { fatal: true }).decode(buf);
  } catch (err) {
    return json(400, {});
  }
  const textBytes = new TextEncoder().encode(text).byteLength;
  if (textBytes < 1) return json(400, {});
  if (textBytes > MAX_TEXT_BYTES) return json(413, {});

  await putReply(env, nonce, text, tReplyRx, colo, null);
  return json(204, {});
}

async function getReply(nonce, env, ctx, request) {
  const key = "r:" + nonce;
  const value = await env.REPLIES.get(key);
  const tServed = Date.now();
  const colo = coloOf(request);
  if (value === null) {
    logTrace(nonce, "get", tServed, colo, 0);
    return json(404, { t_served: tServed, colo: colo });
  }
  const unpacked = unpackReply(value);
  logTrace(nonce, "get", tServed, colo, 1);
  // Await so a second read in this location misses. Another location can still
  // serve a cached copy for up to about 60 s. The 600 s TTL is the backstop.
  try {
    await env.REPLIES.delete(key);
  } catch (err) {
    console.log("reply-del err=" + ((err && err.name) || "Error"));
  }
  void ctx;
  return json(200, { text: unpacked.text, trace: traceForHit(unpacked.tr, tServed, colo) });
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

// Walk back continuation bytes so the cut stays on a UTF-8 character boundary.
function subjectText(text) {
  let bytes = new TextEncoder().encode(text);
  if (bytes.byteLength > 500) {
    let end = 500;
    while (end > 0 && (bytes[end] & 0xc0) === 0x80) end--;
    bytes = bytes.slice(0, end);
  }
  let bin = "";
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function pingFromOrEmpty(env) {
  const from = typeof env.PING_FROM === "string" ? env.PING_FROM : "";
  if (from.length < 3 || from.indexOf("@") < 1) return "";
  return from;
}

async function postPing(request, env) {
  const tPingRx = Date.now();
  const colo = coloOf(request);
  const pingKey = typeof env.PING_KEY === "string" ? env.PING_KEY : "";
  if (pingKey.length < 32 || pingKey === env.WRITE_KEY || pingKey === env.READ_KEY) {
    return json(503, { error: "ping-off" });
  }
  if (!env.EMAIL) return json(503, { error: "ping-off" });
  const from = pingFromOrEmpty(env);
  if (!from) return json(503, { error: "ping-off" });
  if (!timingSafeEqualString(presentedToken(request), pingKey)) return json(401, {});

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

  try {
    // Leave `to` unset. Email Service uses the binding's destination_address.
    await env.EMAIL.send({
      from: { email: from, name: "EyeGrok" },
      subject: "EyeGrok ping " + body.nonce + " " + subjectText(body.text),
      text: JSON.stringify({ nonce: body.nonce, text: body.text }),
    });
  } catch (err) {
    const code = err && err.code ? err.code : (err && err.name) || "Error";
    console.log("ping-send err=" + code);
    return json(502, {});
  }
  const tEmailSent = Date.now();
  logTrace(body.nonce, "ping", tEmailSent, colo);
  return json(200, { t_ping_rx: tPingRx, t_email_sent: tEmailSent, colo: colo });
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

      if (request.method === "POST" && path === "/ping") return await postPing(request, env);

      if (request.method === "POST" && path === "/reply") {
        if (!timingSafeEqualString(presentedToken(request), keys.writeKey)) return json(401, {});
        return await postReply(request, env);
      }

      if (request.method === "POST" && path.startsWith("/reply/")) {
        if (!timingSafeEqualString(presentedToken(request), keys.writeKey)) return json(401, {});
        const nonce = decodeURIComponent(path.slice("/reply/".length));
        if (!NONCE_RE.test(nonce)) return json(400, {});
        return await postReplyPlain(request, env, nonce);
      }

      if (request.method === "GET" && path.startsWith("/reply/")) {
        if (!timingSafeEqualString(presentedToken(request), keys.readKey)) return json(401, {});
        const nonce = decodeURIComponent(path.slice("/reply/".length));
        if (!NONCE_RE.test(nonce)) return json(400, {});
        return await getReply(nonce, env, ctx, request);
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
