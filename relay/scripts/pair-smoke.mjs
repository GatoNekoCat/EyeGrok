// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

// Pairing smoke test. Env: RELAY_URL, RELAY_WRITE_KEY, RELAY_READ_KEY, RELAY_PAIR_KEY.
// RELAY_PAIR_PHASE is main, limit, or off. Keys are never printed.

import { randomBytes } from "node:crypto";

const base = process.env.RELAY_URL || "";
const writeKey = process.env.RELAY_WRITE_KEY || "";
const readKey = process.env.RELAY_READ_KEY || "";
const pairKey = process.env.RELAY_PAIR_KEY || "";
const phase = process.env.RELAY_PAIR_PHASE || "main";

if (!base || !writeKey || !readKey) {
  console.error("FAIL missing RELAY_URL, RELAY_WRITE_KEY, or RELAY_READ_KEY");
  process.exit(1);
}

function assert(cond, label) {
  if (!cond) {
    console.error("FAIL " + label);
    process.exit(1);
  }
  console.log("PASS " + label);
}

function b64url(n) {
  return randomBytes(n).toString("base64url");
}

function newId() {
  return b64url(16);
}

function dummyBox(ctBytes) {
  return { v: 1, iv: b64url(16), ct: b64url(ctBytes), mac: b64url(32) };
}

async function call(path, { method = "GET", token = "", body = null, raw = null } = {}) {
  const headers = {};
  if (token) headers.Authorization = "Bearer " + token;
  let payload;
  if (raw !== null) {
    payload = raw;
    headers["Content-Type"] = "application/json";
  } else if (body !== null) {
    payload = JSON.stringify(body);
    headers["Content-Type"] = "application/json";
  }
  const res = await fetch(new URL(path, base.endsWith("/") ? base : base + "/"), {
    method,
    headers,
    body: payload,
  });
  const text = await res.text();
  return { status: res.status, text, cache: res.headers.get("cache-control") || "" };
}

function storeOk(res) {
  return (res.cache || "").indexOf("no-store") !== -1;
}

async function phaseOff() {
  const health = await call("/health");
  assert(health.status === 200 && health.text === "ok" && storeOk(health), "off health 200");
  const id = newId();
  const posted = await call("/pair", { method: "POST", body: { id, box: dummyBox(8) } });
  assert(posted.status === 503 && posted.text.indexOf("pairing-off") !== -1 && storeOk(posted), "off POST 503");
  const got = await call("/pair/" + id);
  assert(got.status === 503 && got.text.indexOf("pairing-off") !== -1 && storeOk(got), "off GET 503");
}

async function phaseLimit() {
  const statuses = [];
  for (let i = 0; i < 12; i++) {
    const res = await call("/pair/" + newId());
    assert(storeOk(res), "limit no-store");
    statuses.push(res.status);
  }
  const misses = statuses.slice(0, 11).every((s) => s === 404);
  assert(misses && statuses[11] === 429, "limit 11x404 then 429");
}

async function phaseMain() {
  if (pairKey.length < 32) {
    console.error("FAIL missing RELAY_PAIR_KEY");
    process.exit(1);
  }
  const id = newId();
  const box = dummyBox(24);

  const noAuth = await call("/pair", { method: "POST", body: { id, box } });
  assert(noAuth.status === 401 && storeOk(noAuth), "POST no auth 401");

  const asWrite = await call("/pair", { method: "POST", token: writeKey, body: { id, box } });
  assert(asWrite.status === 401 && storeOk(asWrite), "POST WRITE key 401");

  const asRead = await call("/pair", { method: "POST", token: readKey, body: { id, box } });
  assert(asRead.status === 401 && storeOk(asRead), "POST READ key 401");

  const badId = await call("/pair", { method: "POST", token: pairKey, body: { id: "short", box } });
  assert(badId.status === 400 && storeOk(badId), "POST bad id 400");

  const badBox = await call("/pair", {
    method: "POST",
    token: pairKey,
    body: { id, box: { v: 2, iv: "aa", ct: "aa", mac: "aa" } },
  });
  assert(badBox.status === 400 && storeOk(badBox), "POST bad box 400");

  const bigCt = await call("/pair", { method: "POST", token: pairKey, body: { id: newId(), box: dummyBox(4097) } });
  assert(bigCt.status === 413 && storeOk(bigCt), "POST ct over 4096 is 413");

  const huge = "{" + "x".repeat(9000) + "}";
  const bigBody = await call("/pair", { method: "POST", token: pairKey, raw: huge });
  assert(bigBody.status === 413 && storeOk(bigBody), "POST body over 8 KiB is 413");

  const created = await call("/pair", { method: "POST", token: pairKey, body: { id, box } });
  assert(created.status === 201 && created.text === "{}" && storeOk(created), "POST create 201");

  const dup = await call("/pair", { method: "POST", token: pairKey, body: { id, box } });
  assert(dup.status === 409 && storeOk(dup), "POST duplicate 409");

  const got = await call("/pair/" + id);
  let parsed = null;
  try { parsed = JSON.parse(got.text); } catch (err) { parsed = null; }
  assert(got.status === 200 && parsed && parsed.box && storeOk(got), "GET 200");
  assert(
    parsed.box.v === box.v && parsed.box.iv === box.iv && parsed.box.ct === box.ct && parsed.box.mac === box.mac,
    "GET box byte-equal"
  );

  const again = await call("/pair/" + id);
  assert(again.status === 404 && storeOk(again), "GET second 404");

  const other = newId();
  const otherBox = dummyBox(16);
  const made = await call("/pair", { method: "POST", token: pairKey, body: { id: other, box: otherBox } });
  assert(made.status === 201, "DELETE setup 201");
  const removed = await call("/pair/" + other, { method: "DELETE", token: pairKey });
  assert(removed.status === 204 && storeOk(removed), "DELETE 204");
  const after = await call("/pair/" + other);
  assert(after.status === 404 && storeOk(after), "GET after DELETE 404");

  const expId = newId();
  const exp = await call("/pair", { method: "POST", token: pairKey, body: { id: expId, box: dummyBox(12) } });
  assert(exp.status === 201, "expiry create 201");
  await new Promise((resolve) => setTimeout(resolve, 65000));
  const expired = await call("/pair/" + expId);
  assert(expired.status === 404 && storeOk(expired), "expiry GET 404");
}

if (phase === "off") await phaseOff();
else if (phase === "limit") await phaseLimit();
else if (phase === "main") await phaseMain();
else {
  console.error("FAIL phase");
  process.exit(1);
}
console.log("PASS phase " + phase);
