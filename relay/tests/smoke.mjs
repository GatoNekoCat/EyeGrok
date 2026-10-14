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

async function call(env, { token = PING_KEY, body = null, raw = null } = {}) {
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
      new Request("https://relay.test/ping", { method: "POST", headers, body: payload }),
      env,
      { waitUntil() {} }
    );
    const text = await res.text();
    return { status: res.status, text, cache: res.headers.get("cache-control") || "", logs };
  } finally {
    console.log = prev;
  }
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

// 497 ASCII bytes plus a 4-byte emoji. Byte index 500 falls inside the emoji.
const prefix = "a".repeat(497);
const emoji = "\u{1F600}";
const wide = prefix + emoji;
const truncEnv = makeEnv();
const trunc = await call(truncEnv.env, { body: { text: wide, nonce: NONCE } });
assert(trunc.status === 200 && trunc.text === "{}" && trunc.logs.length === 0, "POST /ping truncation 200");
assert(truncEnv.sent.length === 1, "POST /ping truncation sent one email");
const truncMsg = truncEnv.sent[0];
const parts = truncMsg.subject.split(" ");
assert(parts.length === 4 && parts[0] === "EyeGrok" && parts[1] === "ping" && parts[2] === NONCE, "POST /ping subject shape");
const subjectBytes = b64urlToBytes(parts[3]);
const subjectText = new TextDecoder("utf-8", { fatal: true }).decode(subjectBytes);
assert(subjectBytes.byteLength <= 500 && subjectText === prefix, "POST /ping subject truncated on multibyte boundary");
const truncBody = JSON.parse(truncMsg.text);
assert(truncBody.nonce === NONCE && truncBody.text === wide, "POST /ping email body keeps full text");
assert(truncMsg.to === undefined && truncMsg.from.email === PING_FROM && truncMsg.from.name === "EyeGrok", "POST /ping from binding sender");

const okEnv = makeEnv();
const phrase = "hello";
const ok = await call(okEnv.env, { body: { text: phrase, nonce: NONCE } });
assert(ok.status === 200 && ok.text === "{}" && (ok.cache || "").indexOf("no-store") !== -1, "POST /ping 200");
assert(ok.logs.length === 0 && okEnv.sent.length === 1, "POST /ping 200 logs nothing");
const okMsg = okEnv.sent[0];
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
    failed.logs[0].indexOf(PING_KEY) === -1,
  "POST /ping send failure logs code only"
);

log("smoke: ok");
