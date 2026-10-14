# Relay

v0.1 requires this relay. There is no shared EyeGrok relay. The human runs it on their own free Cloudflare account.

The headset posts to this relay at `POST /ping`. That call emails the bot's own Grok Bot inbox. The bot routine has an email trigger and POSTs its reply to `/reply`. The app polls this Worker with a separate read key.

The Worker is one KV mailbox. It has no runtime dependencies. `wrangler` is a dev tool and is not shipped. `POST /ping` sends one email through the `EMAIL` binding and does not read or write KV.

Keys never go in files or chat. `WRITE_KEY`, `READ_KEY`, `PING_KEY`, and optional `PAIR_KEY` are generated in the shell and piped into `wrangler secret put`. Each is at least 32 characters. The read key is what the app stores. The write key is what the Bot routine uses. `WRITE_KEY` and `READ_KEY` must differ. `PING_KEY` is for `POST /ping` only and must differ from `WRITE_KEY` and `READ_KEY`.

## Cloudflare account

Use a free Cloudflare account [F]. Workers Free is 100,000 requests/day and 10 ms CPU per request. The quota resets at 00:00 UTC. Over the limit, Workers returns error 1027 [F].

## Setup

From `relay/`:

```
npm ci
npx wrangler login
npx wrangler kv namespace create REPLIES
```

Copy `wrangler.example.toml` to `wrangler.toml` (gitignored) and paste the namespace id over both `REPLACE_ME` values. `wrangler.toml` is never committed.

Generate `WRITE_KEY`, `READ_KEY`, `PING_KEY`, and optional `PAIR_KEY` in the shell (32 random bytes each, base64url) and pipe them. Each is at least 32 characters. Do not echo them and do not write them to a file. The relay itself is required. `PAIR_KEY` is the optional secret, not the Worker. Pairing stays off until it is set, and `/health` and `/reply` keep working either way. It must differ from the write key and the read key. `PING_KEY` is optional too. `/ping` stays off until `PING_KEY` is set, the `EMAIL` `send_email` binding is present, and `PING_FROM` is a usable sender address. `PING_KEY` must differ from `WRITE_KEY` and `READ_KEY`.

```
# in a shell where the values are variables, not history if you can avoid it
npx wrangler secret put WRITE_KEY
npx wrangler secret put READ_KEY
npx wrangler secret put PAIR_KEY
npx wrangler secret put PING_KEY
```

`PAIR_TTL_SECONDS` is an optional var, not a secret. The Worker clamps it to 60 through 300. The default is 300. Local tests set 60 in `.dev.vars`.

`PING_FROM` is an optional var, not a secret. It is the address the `EMAIL` `send_email` binding sends from. Onboard the sender domain in Cloudflare Email Routing before that address can send. In `wrangler.toml`, set `destination_address` on the `EMAIL` binding to the Bot inbox. The example file uses placeholders only: `ping@example.invalid` and `REPLACE_ME@example.invalid`. Replace those placeholders in your local `wrangler.toml`. Do not commit the real address. The Worker leaves `to` unset, and Email Service uses `destination_address` [F].

Deploy, then run the smoke test with `RELAY_URL`, `RELAY_WRITE_KEY`, and `RELAY_READ_KEY` set in the environment:

```
npx wrangler deploy
node scripts/smoke.mjs
```

`/ping` is covered by the local mock test `node relay/tests/smoke.mjs`.

`RELAY_URL` looks like `https://REPLACE_ME.workers.dev` only as a placeholder. Use the hostname `deploy` prints. Do not commit it.

Local check, no login: `npx wrangler dev` serves the Worker on localhost. Point `RELAY_URL` at that origin and use the same env vars. Local KV does not reproduce Cloudflare's cross-location delay. The deployed measurements are P0-e(a) and P0-e(b).

From `relay/`, `node tests/smoke.mjs` checks `POST /ping` with a mock `EMAIL`. That run does not deploy.

## API

Every response sends `Cache-Control: no-store`. `/health` is `text/plain`. Everything else is `application/json`. There is no CORS.

| Route | Auth | Behavior |
|---|---|---|
| `GET /health` | none | `200` `ok`. No data. If a key is missing, shorter than 32 characters, or the two keys are equal, `500` `{"error":"misconfigured"}`. The body never contains a key. |
| `POST /reply` | `Authorization: Bearer` write key | JSON `{"nonce","text"}` plus optional `rt`. `nonce` must match `^[0-9a-f]{32}$`, otherwise `400`. `text` must be a string of 1 to 16384 UTF-8 bytes, otherwise `400` or `413`. A body over 20 KiB is `413`. `rt`, when present, is an object of at most 3 keys. Each value is a finite number within one day of the Worker clock, otherwise `400` and nothing is stored. On success, `REPLIES.put("r:"+nonce, {"text","tr"}, {expirationTtl: 600})` and `204`. `tr` holds `t_reply_rx`, `colo_reply` when the request has a colo, and `rt` when it was sent. Overwrite is allowed. KV allows 1 write per second to the same key [F]. |
| `POST /reply/<nonce>` | write key | `Content-Type: text/plain` (charset optional). The body is the reply text, not JSON. Same nonce, auth, and size limits as `POST /reply`. Empty text is `400`. Any other content type is `400`. Success is `204` and the same KV envelope, with no `rt`. |
| `GET /reply/<nonce>` | read key | A bad nonce is `400`. A hit is `200` `{"text","trace"}` with `text` first. `trace` holds `t_reply_rx`, `colo_reply`, `rt`, `t_reply_served`, and `colo_get`. No key inside `trace` is named `text`. The Worker then awaits `delete` on that key, so a second read in the same location is a miss. Another location can still serve a cached copy for up to about 60 seconds. The 600 second TTL is the backstop. A value that is not the envelope is returned as the text. A miss is `404` `{"t_served","colo"}`. |
| `POST /ping` | `Authorization: Bearer` `PING_KEY` | Body `{text,nonce}`. `nonce` must match `^[0-9a-f]{32}$`, otherwise `400`. `text` must be a string of 1 to 16384 UTF-8 bytes, otherwise `400` or `413`. A body over 20 KiB is `413`. Success calls `env.EMAIL.send` and returns `200` `{"t_ping_rx","t_email_sent","colo"}`. `t_ping_rx` is the handler start. `t_email_sent` is when `EMAIL.send` resolves. The subject is `EyeGrok ping <nonce> <base64url>` of the text truncated to 500 UTF-8 bytes on a whole-character boundary. The email body is JSON `{"nonce","text"}` with the full text. `to` is omitted. Email Service sends to the binding's `destination_address` [F]. The sender is `PING_FROM` with the name `EyeGrok`. A missing or short `PING_KEY`, a key equal to `WRITE_KEY` or `READ_KEY`, a missing `EMAIL` binding, or a missing `PING_FROM` is `503` `{"error":"ping-off"}`. The wrong bearer is `401`. A send failure is `502` `{}`. The failure log line is `ping-send err=` plus the error code or name. A success log line is `trace tid=` plus 8 hex chars, `ev=`, `t=`, and `colo=`. The log never includes the text, the full nonce, or a key. |
| `POST /pair` | `Bearer` pair key | JSON `{"id","box"}`. `id` matches `^[A-Za-z0-9_-]{22}$`. `box` is `{"v":1,"iv","ct","mac"}` with `iv` 16 bytes, `mac` 32 bytes, and `ct` at most 4096 bytes, all base64url. A bad shape is `400`. `ct` over 4096 bytes, or a body over 8 KiB, is `413`. An existing id is `409`. Success is `201` `{}` and `put("p:"+id, box, {expirationTtl})`. The write key or the read key is `401`. Pairing off is `503` `{"error":"pairing-off"}`. |
| `GET /pair/<id>` | none | The id is the capability. A bad id is `400`. A hit is `200` `{"box":...}` and then a best-effort `delete`. A miss is `404` `{}` and counts toward the miss limiter. More than 10 misses from one address in 60 seconds is `429` `{}` for the rest of that window. Pairing off is `503`. |
| `DELETE /pair/<id>` | pair key | `204`, including when the id is already gone. A bad id is `400`. The wrong key is `401`. |
| anything else | | `404` |

The write key on `GET /reply/<nonce>` is `401`. A missing `Authorization` header is `401`. Keys are compared with `crypto.subtle.timingSafeEqual` on equal-length byte arrays (Workers Web Crypto). A different length is `401`. `POST /ping` uses the same compare against `PING_KEY`.

`expirationTtl` must be at least 60 seconds (Cloudflare KV write docs). This Worker uses 600.

## Limits

`POST /ping` does not touch KV. It sends one email. The KV write happens at `/reply`: 1 write, at most 125 reads, and 1 delete. The app waits 2 seconds, then polls every 1 second, and stops at 125 requests or 120 seconds, whichever comes first. A 429 or a 5xx still backs off, doubling from the 1 second interval up to 16 seconds. A pairing is 1 put, 1 delete on read, and at most 1 `DELETE`. Free KV is 100,000 reads/day, 1,000 writes/day (different keys), 1 write/second on the same key, 1 GB storage, 512-byte keys, and 25 MiB values [F]. About 1,000 pings/day is the write cap [F]. A day of pings that each used all 125 reads would hit the read cap first, around 800. Deletes count against the same 1,000/day write budget [F].

The relay stores the pairing box as it arrived. It does not have the encryption key. A KV dump is ciphertext.

The miss limiter is in memory inside one isolate. It counts `404` misses only. It does not write to KV. The Workers Rate Limiting binding is not used. The docs show the binding syntax, and they do not say the free plan includes it, so this Worker leaves it out.

Single use is best effort. `GET` returns the box and deletes with `waitUntil`. Two reads in different locations can both succeed, because KV has no transactions. The entry also expires. `PAIR_TTL_SECONDS` is clamped to 60 through 300, and the default is 300. That covers the cross-location lag measured in P0-e(c).

## Consistency

KV is eventually consistent. Changes may take up to 60 seconds or more to be visible in other global network locations [F]. A read of a missing key can stay missing for a while after the write (negative caching). The exact cache behavior is UNVERIFIED until P0-e(b) and P0-e(c) record numbers. That result decides KV versus a Durable Object (decision D-1). The app's poll budget is 120 seconds. A later manual "Check again" is one extra read. Reply storage stays KV. This slice does not add a Durable Object.

## Durable Object option

Not built. Same HTTP API and the same two keys, so the app and the routine would not change. A `ReplyBox` SQLite-backed Durable Object, addressed by `idFromName(nonce)`, would store `{text, exp}`, delete on read inside the object, and alarm at 600 seconds to drop leftovers. The migration tag `new_sqlite_classes` is UNVERIFIED. Cost is about 3 Durable Object requests per poll cycle. If D-1 selects it, it lands as `relay/src/do.js` with `STORE=do|kv` in `wrangler.toml`, and the same `smoke.mjs` must pass.

## Rotate a key

```
npx wrangler secret put WRITE_KEY
```

or `READ_KEY`. Then push a new setup file to the headset so the app gets the new read key. The previous key stops working on the next Worker request. Do not put the new key in a file in this repo or in chat.
