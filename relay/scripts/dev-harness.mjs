// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

// Starts wrangler dev with throwaway keys, runs smoke and pair-smoke, then deletes local state.
// Nothing is deployed. Keys stay in memory and in gitignored .dev.vars for the run only.

import { randomBytes } from "node:crypto";
import { spawn } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const relayDir = path.resolve(here, "..");
const port = 8787;
const origin = "http://127.0.0.1:" + port;
const devVars = path.join(relayDir, ".dev.vars");
const wranglerState = path.join(relayDir, ".wrangler");

function key() {
  return randomBytes(32).toString("base64url");
}

function writeVars(pair) {
  const writeKey = key();
  const readKey = key();
  let pairKey = "";
  const lines = ["WRITE_KEY=" + writeKey, "READ_KEY=" + readKey, "PAIR_TTL_SECONDS=60"];
  if (pair) {
    pairKey = key();
    lines.push("PAIR_KEY=" + pairKey);
  }
  writeFileSync(devVars, lines.join("\n") + "\n", { encoding: "utf8" });
  return { writeKey, readKey, pairKey };
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitHealth() {
  const deadline = Date.now() + 90000;
  while (Date.now() < deadline) {
    try {
      const res = await fetch(origin + "/health");
      if (res.status === 200) return;
    } catch (err) {
      // wrangler is still starting
    }
    await sleep(400);
  }
  throw new Error("wrangler did not answer /health");
}

function startWrangler() {
  const child = spawn(
    "npx",
    ["wrangler", "dev", "--config", "wrangler.example.toml", "--port", String(port), "--ip", "127.0.0.1", "--local"],
    {
      cwd: relayDir,
      shell: true,
      stdio: ["ignore", "pipe", "pipe"],
      env: Object.assign({}, process.env, { WRANGLER_SEND_METRICS: "false" }),
    }
  );
  const logPath = path.join(mkdtempSync(path.join(tmpdir(), "eyegrok-wrangler-")), "wrangler.log");
  const chunks = [];
  const take = (buf) => {
    chunks.push(buf);
    if (chunks.length > 40) chunks.shift();
  };
  child.stdout.on("data", take);
  child.stderr.on("data", take);
  child.logTail = () => Buffer.concat(chunks).toString("utf8").slice(-2000);
  child.logPath = logPath;
  return child;
}

function stopWrangler(child) {
  if (!child || child.killed) return Promise.resolve();
  return new Promise((resolve) => {
    const killer = spawn("taskkill", ["/PID", String(child.pid), "/T", "/F"], { shell: true, stdio: "ignore" });
    killer.on("exit", () => resolve());
    setTimeout(resolve, 5000);
  });
}

function runNode(script, env) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [script], {
      cwd: relayDir,
      env: Object.assign({}, process.env, env),
      stdio: "inherit",
    });
    child.on("error", reject);
    child.on("exit", (code) => {
      if (code === 0) resolve();
      else reject(new Error(path.basename(script) + " exited " + code));
    });
  });
}

async function withServer(pair, fn) {
  const keys = writeVars(pair);
  const child = startWrangler();
  try {
    await waitHealth();
    await fn(keys);
  } catch (err) {
    const tail = child.logTail ? child.logTail() : "";
    if (tail) console.error("wrangler tail chars=" + tail.length);
    throw err;
  } finally {
    await stopWrangler(child);
    await sleep(500);
  }
}

function scrub() {
  rmSync(devVars, { force: true });
  rmSync(wranglerState, { recursive: true, force: true });
}

const smoke = path.join(relayDir, "scripts", "smoke.mjs");
const pairSmoke = path.join(relayDir, "scripts", "pair-smoke.mjs");

try {
  await withServer(true, async (keys) => {
    const env = {
      RELAY_URL: origin,
      RELAY_WRITE_KEY: keys.writeKey,
      RELAY_READ_KEY: keys.readKey,
      RELAY_PAIR_KEY: keys.pairKey,
    };
    await runNode(smoke, env);
    await runNode(pairSmoke, Object.assign({ RELAY_PAIR_PHASE: "main" }, env));
  });
  await withServer(true, async (keys) => {
    await runNode(pairSmoke, {
      RELAY_URL: origin,
      RELAY_WRITE_KEY: keys.writeKey,
      RELAY_READ_KEY: keys.readKey,
      RELAY_PAIR_KEY: keys.pairKey,
      RELAY_PAIR_PHASE: "limit",
    });
  });
  await withServer(false, async (keys) => {
    const env = {
      RELAY_URL: origin,
      RELAY_WRITE_KEY: keys.writeKey,
      RELAY_READ_KEY: keys.readKey,
    };
    await runNode(smoke, env);
    await runNode(pairSmoke, Object.assign({ RELAY_PAIR_PHASE: "off" }, env));
  });
  console.log("HARNESS=ok");
} finally {
  scrub();
}
