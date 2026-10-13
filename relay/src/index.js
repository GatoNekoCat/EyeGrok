// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

// Reply mailbox. WRITE_KEY posts a nonce's text. READ_KEY reads it once.
// KV expirationTtl minimum is 60 seconds (Cloudflare KV put docs). This uses 600.

const MAX_BODY_BYTES = 20 * 1024;
const MAX_TEXT_BYTES = 16384;
const NONCE_RE = /^[0-9a-f]{32}$/;
const TTL_SECONDS = 600;

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

      return json(404, {});
    } catch (err) {
      const name = err && err.name ? err.name : "Error";
      console.log("err=" + name);
      return json(500, {});
    }
  },
};
