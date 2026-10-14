// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

// POST /ping against the Worker module. EMAIL is a mock. No network.
// Keys and message text are not printed.

import { timingSafeEqual } from "node:crypto";
import worker from "../src/index.js";

crypto.subtle.timingSafeEqual = (a, b) => {
  const left = Buffer.from(a);
  const right = Buffer.from(b);
  if (left.length !== right.length) throw new TypeError("length mismatch");
  return timingSafeEqual(left, right);
};

const WRITE_KEY = "w".repeat(32);
const READ_KEY = "r".repeat(32);
const PING_KEY = "p".repeat(32);
const PING_FROM = "ping@example.invalid";
const PING_TO = "REPLACE_ME@example.invalid";
const NONCE = "0123456789abcdef0123456789abcdef";

const log = console.log.bind(console);

function assert(cond, label) {
  if (!cond) {
    console.error("FAIL " + label);
    process.exit(1);
  }
  log("PASS " + label);
}

function makeEnv(overrides) {
  const sent = [];
  const env = {
    WRITE_KEY,
    READ_KEY,
    PING_KEY,
    PING_FROM,
    PING_TO,
    EMAIL: {
      async send(msg) {
        sent.push(msg);
        return { messageId: "local" };
      },
    },
    REPLIES: {
      async get() {
        return null;
      },
      async put() {},
      async delete() {},
    },
  };
  if (overrides) Object.assign(env, overrides);
  return { env, sent };
}

function withColo(req, colo) {
  if (!colo) return req;
  try {
    Object.defineProperty(req, "cf", { configurable: true, value: { colo: colo } });
  } catch (err) {
    // Some Request implementations seal cf. The Worker then reports colo as "".
  }
  return req;
}

async function call(env, { token = PING_KEY, body = null, raw = null, colo = "" } = {}) {
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
  const logs = [];
  const prev = console.log;
  console.log = (...args) => {
    logs.push(args.map(String).join(" "));
  };
  try {
    const res = await worker.fetch(
      withColo(new Request("https://relay.test/ping", { method: "POST", headers, body: payload }), colo),
      env,
      { waitUntil() {} }
    );
    const text = await res.text();
    return { status: res.status, text, cache: res.headers.get("cache-control") || "", logs };
  } finally {
    console.log = prev;
  }
}

function memoryReplies() {
  const map = new Map();
  return {
    map,
    async get(key) {
      return map.has(key) ? map.get(key) : null;
    },
    async put(key, value) {
      map.set(key, value);
    },
    async delete(key) {
      map.delete(key);
    },
  };
}

async function callAt(env, url, { method = "GET", token = "", body = null, contentType = "", colo = "" } = {}) {
  const headers = {};
  if (token) headers.Authorization = "Bearer " + token;
  if (contentType) headers["Content-Type"] = contentType;
  const init = { method, headers };
  if (body !== null) init.body = body;
  const logs = [];
  const prev = console.log;
  console.log = (...args) => {
    logs.push(args.map(String).join(" "));
  };
  try {
    const res = await worker.fetch(withColo(new Request(url, init), colo), env, {
      waitUntil() {
        throw new Error("waitUntil");
      },
    });
    const text = await res.text();
    return { status: res.status, text, cache: res.headers.get("cache-control") || "", logs };
  } finally {
    console.log = prev;
  }
}

function pingTraceOk(text, forbidden) {
  let body;
  try {
    body = JSON.parse(text);
  } catch (err) {
    return false;
  }
  const keys = Object.keys(body);
  if (keys.length !== 3 || keys[0] !== "t_ping_rx" || keys[1] !== "t_email_sent" || keys[2] !== "colo") return false;
  if (!Number.isFinite(body.t_ping_rx) || !Number.isFinite(body.t_email_sent)) return false;
  if (body.t_email_sent < body.t_ping_rx) return false;
  if (typeof body.colo !== "string") return false;
  if (text.indexOf(WRITE_KEY) !== -1 || text.indexOf(READ_KEY) !== -1 || text.indexOf(PING_KEY) !== -1) return false;
  for (let i = 0; i < forbidden.length; i++) {
    if (forbidden[i] && text.indexOf(forbidden[i]) !== -1) return false;
  }
  return true;
}

function traceLogOk(line, ev, forbidden) {
  if (!new RegExp("^trace tid=[0-9a-f]{8} ev=" + ev + " t=\\d+ colo=[A-Za-z0-9]*").test(line)) return false;
  if (line.indexOf(WRITE_KEY) !== -1 || line.indexOf(READ_KEY) !== -1 || line.indexOf(PING_KEY) !== -1) return false;
  if (line.indexOf(NONCE) !== -1) return false;
  for (let i = 0; i < forbidden.length; i++) {
    if (forbidden[i] && line.indexOf(forbidden[i]) !== -1) return false;
  }
  return true;
}

function b64urlToBytes(value) {
  const pad = "=".repeat((4 - (value.length % 4)) % 4);
  const b64 = value.replace(/-/g, "+").replace(/_/g, "/") + pad;
  return Buffer.from(b64, "base64");
}

const authEnv = makeEnv();
const auth = await call(authEnv.env, {
  token: "x".repeat(32),
  body: { text: "hello", nonce: NONCE },
});
assert(
  auth.status === 401 && auth.text === "{}" && auth.logs.length === 0 && authEnv.sent.length === 0,
  "POST /ping bad bearer 401"
);

const badNonceEnv = makeEnv();
const badNonce = await call(badNonceEnv.env, {
  body: { text: "hello", nonce: "not-a-nonce" },
});
assert(
  badNonce.status === 400 && badNonce.text === "{}" && badNonce.logs.length === 0 && badNonceEnv.sent.length === 0,
  "POST /ping bad nonce 400"
);

const reuseWriteEnv = makeEnv({ PING_KEY: WRITE_KEY });
const reuseWrite = await call(reuseWriteEnv.env, {
  token: WRITE_KEY,
  body: { text: "hello", nonce: NONCE },
});
assert(
  reuseWrite.status === 503 && reuseWrite.text === '{"error":"ping-off"}' && reuseWriteEnv.sent.length === 0,
  "POST /ping reused WRITE_KEY 503"
);

const reuseReadEnv = makeEnv({ PING_KEY: READ_KEY });
const reuseRead = await call(reuseReadEnv.env, {
  token: READ_KEY,
  body: { text: "hello", nonce: NONCE },
});
assert(
  reuseRead.status === 503 && reuseRead.text === '{"error":"ping-off"}' && reuseReadEnv.sent.length === 0,
  "POST /ping reused READ_KEY 503"
);

const missingTo = makeEnv();
delete missingTo.env.PING_TO;
const missingToRes = await call(missingTo.env, { body: { text: "hello", nonce: NONCE } });
assert(
  missingToRes.status === 503 &&
    missingToRes.text === '{"error":"ping-off","reason":"ping-to"}' &&
    missingToRes.text.indexOf("@") === -1 &&
    missingToRes.logs.length === 0 &&
    missingTo.sent.length === 0,
  "POST /ping missing PING_TO 503"
);

const badTo = "not-an-email";
const badToEnv = makeEnv({ PING_TO: badTo });
const badToRes = await call(badToEnv.env, { body: { text: "hello", nonce: NONCE } });
assert(
  badToRes.status === 503 &&
    badToRes.text === '{"error":"ping-off","reason":"ping-to"}' &&
    badToRes.text.indexOf(badTo) === -1 &&
    badToRes.logs.length === 0 &&
    badToEnv.sent.length === 0,
  "POST /ping invalid PING_TO 503"
);

const missingFrom = makeEnv();
delete missingFrom.env.PING_FROM;
const missingFromRes = await call(missingFrom.env, { body: { text: "hello", nonce: NONCE } });
assert(
  missingFromRes.status === 503 &&
    missingFromRes.text === '{"error":"ping-off","reason":"ping-from"}' &&
    missingFromRes.text.indexOf("@") === -1 &&
    missingFromRes.logs.length === 0 &&
    missingFrom.sent.length === 0,
  "POST /ping missing PING_FROM 503"
);

// 497 ASCII bytes plus a 4-byte emoji. Byte index 500 falls inside the emoji.
const prefix = "a".repeat(497);
const emoji = "\u{1F600}";
const wide = prefix + emoji;
const truncEnv = makeEnv();
const trunc = await call(truncEnv.env, { body: { text: wide, nonce: NONCE }, colo: "EWR" });
assert(trunc.status === 200 && pingTraceOk(trunc.text, [prefix, emoji]) && trunc.logs.length === 1, "POST /ping truncation 200");
assert(traceLogOk(trunc.logs[0], "ping", [prefix, emoji]) && trunc.logs[0].indexOf("colo=EWR") !== -1, "POST /ping truncation log is tid only");
assert(JSON.parse(trunc.text).colo === "EWR", "POST /ping truncation colo");
assert(truncEnv.sent.length === 1, "POST /ping truncation sent one email");
const truncMsg = truncEnv.sent[0];
const parts = truncMsg.subject.split(" ");
assert(parts.length === 4 && parts[0] === "EyeGrok" && parts[1] === "ping" && parts[2] === NONCE, "POST /ping subject shape");
const subjectBytes = b64urlToBytes(parts[3]);
const subjectText = new TextDecoder("utf-8", { fatal: true }).decode(subjectBytes);
assert(subjectBytes.byteLength <= 500 && subjectText === prefix, "POST /ping subject truncated on multibyte boundary");
const truncBody = JSON.parse(truncMsg.text);
assert(truncBody.nonce === NONCE && truncBody.text === wide, "POST /ping email body keeps full text");
assert(truncMsg.to === PING_TO && truncMsg.from.email === PING_FROM && truncMsg.from.name === "EyeGrok", "POST /ping to is PING_TO");

const okEnv = makeEnv();
const phrase = "hello";
const ok = await call(okEnv.env, { body: { text: phrase, nonce: NONCE } });
assert(ok.status === 200 && pingTraceOk(ok.text, [phrase, PING_TO, PING_FROM]) && (ok.cache || "").indexOf("no-store") !== -1, "POST /ping 200");
assert(
  ok.logs.length === 1 &&
    traceLogOk(ok.logs[0], "ping", [phrase, PING_TO, PING_FROM]) &&
    ok.logs[0].indexOf("@") === -1 &&
    okEnv.sent.length === 1,
  "POST /ping 200 log has no text"
);
const okMsg = okEnv.sent[0];
assert(okMsg.to === PING_TO, "POST /ping EMAIL to is PING_TO");
const expectSubject = "EyeGrok ping " + NONCE + " " + Buffer.from(phrase, "utf8").toString("base64url");
assert(okMsg.subject === expectSubject, "POST /ping 200 subject");
assert(okMsg.text === JSON.stringify({ nonce: NONCE, text: phrase }), "POST /ping 200 body JSON");
assert(!JSON.stringify(okMsg).includes(PING_KEY) && !JSON.stringify(okMsg).includes(WRITE_KEY), "POST /ping mail omits keys");

const failPhrase = "do-not-log";
const failEnv = makeEnv({
  EMAIL: {
    async send() {
      const err = new Error("x");
      err.code = "E_DELIVERY_FAILED";
      err.message = failPhrase + " " + PING_KEY;
      throw err;
    },
  },
});
const failed = await call(failEnv.env, { body: { text: failPhrase, nonce: NONCE } });
assert(failed.status === 502 && failed.text === "{}", "POST /ping send failure 502");
assert(
  failed.logs.length === 1 &&
    failed.logs[0] === "ping-send err=E_DELIVERY_FAILED" &&
    failed.logs[0].indexOf(failPhrase) === -1 &&
    failed.logs[0].indexOf(PING_KEY) === -1 &&
    failed.logs[0].indexOf(PING_TO) === -1 &&
    failed.logs[0].indexOf(PING_FROM) === -1 &&
    failed.logs[0].indexOf("@") === -1,
  "POST /ping send failure logs code only"
);

const replyNonce = "fedcba9876543210fedcba9876543210";
const plainNonce = "abcdefabcdefabcdefabcdefabcdefab";
const replies = memoryReplies();
const replyEnv = makeEnv({ REPLIES: replies }).env;
const origin = "https://relay.test";

const early = await callAt(replyEnv, origin + "/reply/" + replyNonce, { token: READ_KEY, colo: "EWR" });
assert(early.status === 404, "GET /reply miss 404");
const earlyBody = JSON.parse(early.text);
assert(
  Object.keys(earlyBody).join(",") === "t_served,colo" &&
    Number.isFinite(earlyBody.t_served) &&
    earlyBody.colo === "EWR" &&
    early.logs.length === 1 &&
    traceLogOk(early.logs[0], "get", []) &&
    early.logs[0].indexOf("hit=0") !== -1,
  "GET /reply miss body"
);

const rtNow = Date.now();
const rt = { t_routine_start: rtNow - 30, t_answer_ready: rtNow - 20, t_curl: rtNow - 10 };
const jsonPhrase = "json-reply";
const posted = await callAt(replyEnv, origin + "/reply", {
  method: "POST",
  token: WRITE_KEY,
  contentType: "application/json",
  colo: "IAD",
  body: JSON.stringify({ nonce: replyNonce, text: jsonPhrase, rt: rt }),
});
assert(posted.status === 204 && posted.text === "" && posted.logs.length === 1, "POST /reply with rt 204");
assert(traceLogOk(posted.logs[0], "put", [jsonPhrase]) && posted.logs[0].indexOf("colo=IAD") !== -1, "POST /reply put log");
const stored = replies.map.get("r:" + replyNonce);
assert(typeof stored === "string" && stored.indexOf(jsonPhrase) !== -1 && stored.indexOf("t_routine_start") !== -1, "POST /reply stores text and rt");
assert(stored.startsWith('{"text":'), "stored envelope text first");

const hit = await callAt(replyEnv, origin + "/reply/" + replyNonce, { token: READ_KEY, colo: "EWR" });
const hitBody = JSON.parse(hit.text);
assert(hit.status === 200 && hit.text.startsWith('{"text":'), "GET /reply hit text first");
assert(
  Object.keys(hitBody).join(",") === "text,trace" &&
    hitBody.text === jsonPhrase &&
    hitBody.trace.t_reply_rx <= hitBody.trace.t_reply_served &&
    hitBody.trace.colo_reply === "IAD" &&
    hitBody.trace.colo_get === "EWR" &&
    hitBody.trace.rt.t_routine_start === rt.t_routine_start &&
    hitBody.trace.rt.t_answer_ready === rt.t_answer_ready &&
    hitBody.trace.rt.t_curl === rt.t_curl &&
    JSON.stringify(hitBody.trace).indexOf('"text"') === -1,
  "GET /reply hit trace"
);
assert(hit.logs.length === 1 && hit.logs[0].indexOf("hit=1") !== -1 && hit.logs[0].indexOf(jsonPhrase) === -1, "GET /reply hit log");

const again = await callAt(replyEnv, origin + "/reply/" + replyNonce, { token: READ_KEY, colo: "EWR" });
const againBody = JSON.parse(again.text);
assert(again.status === 404 && againBody.colo === "EWR" && Number.isFinite(againBody.t_served), "second GET is a miss");
assert(!replies.map.has("r:" + replyNonce), "delete removed the key");

const badRt = await callAt(replyEnv, origin + "/reply", {
  method: "POST",
  token: WRITE_KEY,
  contentType: "application/json",
  body: JSON.stringify({
    nonce: replyNonce,
    text: jsonPhrase,
    rt: { a: 1, b: 2, c: 3, d: 4 },
  }),
});
assert(badRt.status === 400 && !replies.map.has("r:" + replyNonce), "POST /reply bad rt 400 stores nothing");

const plainPhrase = "plain-reply";
const plainPost = await callAt(replyEnv, origin + "/reply/" + plainNonce, {
  method: "POST",
  token: WRITE_KEY,
  contentType: "text/plain; charset=utf-8",
  colo: "IAD",
  body: plainPhrase,
});
assert(plainPost.status === 204 && plainPost.logs.length === 1 && plainPost.logs[0].indexOf(plainPhrase) === -1, "POST /reply/<nonce> text/plain 204");
const plainHit = await callAt(replyEnv, origin + "/reply/" + plainNonce, { token: READ_KEY, colo: "EWR" });
const plainBody = JSON.parse(plainHit.text);
assert(
  plainHit.status === 200 &&
    plainHit.text.startsWith('{"text":') &&
    plainBody.text === plainPhrase &&
    plainBody.trace.rt === undefined &&
    plainBody.trace.colo_reply === "IAD",
  "GET text/plain reply"
);
const plainAgain = await callAt(replyEnv, origin + "/reply/" + plainNonce, { token: READ_KEY });
assert(plainAgain.status === 404, "second GET after text/plain is a miss");

const plainAuth = await callAt(replyEnv, origin + "/reply/" + plainNonce, {
  method: "POST",
  token: "x".repeat(32),
  contentType: "text/plain",
  body: plainPhrase,
});
assert(plainAuth.status === 401 && !replies.map.has("r:" + plainNonce), "POST /reply/<nonce> bad bearer 401");

const plainType = await callAt(replyEnv, origin + "/reply/" + plainNonce, {
  method: "POST",
  token: WRITE_KEY,
  contentType: "application/json",
  body: plainPhrase,
});
assert(plainType.status === 400 && !replies.map.has("r:" + plainNonce), "POST /reply/<nonce> json type 400");

const plainEmpty = await callAt(replyEnv, origin + "/reply/" + plainNonce, {
  method: "POST",
  token: WRITE_KEY,
  contentType: "text/plain",
  body: "",
});
assert(plainEmpty.status === 400, "POST /reply/<nonce> empty 400");

const plainBig = await callAt(replyEnv, origin + "/reply/" + plainNonce, {
  method: "POST",
  token: WRITE_KEY,
  contentType: "text/plain",
  body: "b".repeat(16385),
});
assert(plainBig.status === 413 && !replies.map.has("r:" + plainNonce), "POST /reply/<nonce> over text limit 413");

const plainHuge = "c".repeat(20 * 1024 + 1);
const plainOver = await callAt(replyEnv, origin + "/reply/" + plainNonce, {
  method: "POST",
  token: WRITE_KEY,
  contentType: "text/plain",
  body: plainHuge,
});
assert(plainOver.status === 413, "POST /reply/<nonce> over body limit 413");

const badPath = await callAt(replyEnv, origin + "/reply/not-a-nonce", {
  method: "POST",
  token: WRITE_KEY,
  contentType: "text/plain",
  body: plainPhrase,
});
assert(badPath.status === 400, "POST /reply/<nonce> bad nonce 400");

replies.map.set("r:" + replyNonce, "legacy-text");
const legacy = await callAt(replyEnv, origin + "/reply/" + replyNonce, { token: READ_KEY, colo: "LHR" });
const legacyBody = JSON.parse(legacy.text);
assert(
  legacy.status === 200 &&
    legacyBody.text === "legacy-text" &&
    legacyBody.trace.t_reply_rx === undefined &&
    Number.isFinite(legacyBody.trace.t_reply_served) &&
    legacyBody.trace.colo_get === "LHR",
  "GET legacy raw text"
);
const legacyAgain = await callAt(replyEnv, origin + "/reply/" + replyNonce, { token: READ_KEY });
assert(legacyAgain.status === 404, "second GET after legacy hit is a miss");

log("smoke: ok");
