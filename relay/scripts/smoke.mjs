// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

// Assertions from the plan's relay smoke test.
// Env: RELAY_URL, RELAY_WRITE_KEY, RELAY_READ_KEY. Nothing is printed from those values.

import { randomBytes } from "node:crypto";

const base = process.env.RELAY_URL || "";
const writeKey = process.env.RELAY_WRITE_KEY || "";
const readKey = process.env.RELAY_READ_KEY || "";

if (!base || !writeKey || !readKey) {
  console.error("FAIL missing RELAY_URL, RELAY_WRITE_KEY, or RELAY_READ_KEY");
  process.exit(1);
}

function nonce() {
  return randomBytes(16).toString("hex");
}

async function call(path, { method = "GET", token = "", body = null } = {}) {
  const headers = { "Cache-Control": "no-store" };
  if (token) headers.Authorization = "Bearer " + token;
  if (body !== null) headers["Content-Type"] = "application/json";
  const res = await fetch(new URL(path, base.endsWith("/") ? base : base + "/"), {
    method,
    headers,
    body: body === null ? undefined : body,
  });
  const text = await res.text();
  return { status: res.status, text, cache: res.headers.get("cache-control") };
}

function assert(cond, label) {
  if (!cond) {
    console.error("FAIL " + label);
    process.exit(1);
  }
  console.log("PASS " + label);
}

const sample = nonce();

const health = await call("/health");
assert(health.status === 200 && health.text === "ok", "GET /health 200 ok");
assert((health.cache || "").indexOf("no-store") !== -1, "GET /health Cache-Control no-store");

const bare = await call("/reply/" + sample);
assert(bare.status === 401, "GET /reply without auth 401");

const asWrite = await call("/reply/" + sample, { token: writeKey });
assert(asWrite.status === 401, "GET /reply with WRITE key 401");

const badNonce = await call("/reply", {
  method: "POST",
  token: writeKey,
  body: JSON.stringify({ nonce: "not-a-nonce", text: "ping" }),
});
assert(badNonce.status === 400, "POST /reply bad nonce 400");

const over = "a".repeat(16385);
const tooBig = await call("/reply", {
  method: "POST",
  token: writeKey,
  body: JSON.stringify({ nonce: sample, text: over }),
});
assert(tooBig.status === 413, "POST /reply text over limit 413");

const phrase = "ping";
const posted = await call("/reply", {
  method: "POST",
  token: writeKey,
  body: JSON.stringify({ nonce: sample, text: phrase }),
});
assert(posted.status === 204, "POST /reply write 204");

const read = await call("/reply/" + sample, { token: readKey });
let parsed = null;
try {
  parsed = JSON.parse(read.text);
} catch (err) {
  parsed = null;
}
assert(read.status === 200 && parsed && parsed.text === phrase, "GET /reply read 200 text matches");

const again = await call("/reply/" + sample, { token: readKey });
if (again.status === 404) {
  console.log("PASS second read 404");
} else if (again.status === 200) {
  console.log("STALE_AFTER_DELETE");
} else {
  console.error("FAIL second read status " + again.status);
  process.exit(1);
}

console.log("smoke: ok");
