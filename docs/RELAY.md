# Relay

v0.1 requires this relay. There is no shared EyeGrok relay. The human runs it on their own free Cloudflare account.

The headset posts to this relay at `POST /ping`. That call emails the bot's own Grok Bot inbox. The bot routine has an email trigger and POSTs its reply to `/reply`. The app polls this Worker with a separate read key.

The Worker is one KV mailbox. It has no runtime dependencies. `wrangler` is a dev tool and is not shipped.

Keys never go in files or chat. `WRITE_KEY` and `READ_KEY` are generated in the shell and piped into `wrangler secret put`. The read key is what the app stores. The write key is what the Bot routine uses. They must differ, and each must be at least 32 characters.

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

Generate three keys (32 random bytes each, base64url) and pipe them. Do not echo them and do not write them to a file. The relay itself is required. `PAIR_KEY` is the optional secret, not the Worker. Pairing stays off until it is set, and `/health` and `/reply` keep working either way. It must differ from the write key and the read key.

```
# in a shell where the values are variables, not history if you can avoid it
npx wrangler secret put WRITE_KEY
npx wrangler secret put READ_KEY
npx wrangler secret put PAIR_KEY
```

`PAIR_TTL_SECONDS` is an optional var, not a secret. The Worker clamps it to 60 through 300. The default is 300. Local tests set 60 in `.dev.vars`.

Deploy, then run the smoke test with `RELAY_URL`, `RELAY_WRITE_KEY`, and `RELAY_READ_KEY` set in the environment:

```
npx wrangler deploy
node scripts/smoke.mjs
```

`RELAY_URL` looks like `https://REPLACE_ME.workers.dev` only as a placeholder. Use the hostname `deploy` prints. Do not commit it.

Local check, no login: `npx wrangler dev` serves the Worker on localhost. Point `RELAY_URL` at that origin and use the same env vars. Local KV does not reproduce Cloudflare's cross-location delay. The deployed measurements are P0-e(a) and P0-e(b).

## API

Every response sends `Cache-Control: no-store`. `/health` is `text/plain`. Everything else is `application/json`. There is no CORS.

| Route | Auth | Behavior |
|---|---|---|
| `GET /health` | none | `200` `ok`. No data. If a key is missing, shorter than 32 characters, or the two keys are equal, `500` `{"error":"misconfigured"}`. The body never contains a key. |
| `POST /reply` | `Authorization: Bearer` write key | JSON `{"nonce","text"}`. `nonce` must match `^[0-9a-f]{32}$`, otherwise `400`. `text` must be a string of 1 to 16384 UTF-8 bytes, otherwise `400` or `413`. A body over 20 KiB is `413`. On success, `REPLIES.put("r:"+nonce, text, {expirationTtl: 600})` and `204`. Overwrite is allowed. KV allows 1 write per second to the same key [F]. |
| `GET /reply/<nonce>` | read key | A bad nonce is `400`. A hit is `200` `{"text":...}` and then a best-effort `delete` of that key. A miss is `404` `{}`. |
| `POST /pair` | `Bearer` pair key | JSON `{"id","box"}`. `id` matches `^[A-Za-z0-9_-]{22}$`. `box` is `{"v":1,"iv","ct","mac"}` with `iv` 16 bytes, `mac` 32 bytes, and `ct` at most 4096 bytes, all base64url. A bad shape is `400`. `ct` over 4096 bytes, or a body over 8 KiB, is `413`. An existing id is `409`. Success is `201` `{}` and `put("p:"+id, box, {expirationTtl})`. The write key or the read key is `401`. Pairing off is `503` `{"error":"pairing-off"}`. |
| `GET /pair/<id>` | none | The id is the capability. A bad id is `400`. A hit is `200` `{"box":...}` and then a best-effort `delete`. A miss is `404` `{}` and counts toward the miss limiter. More than 10 misses from one address in 60 seconds is `429` `{}` for the rest of that window. Pairing off is `503`. |
| `DELETE /pair/<id>` | pair key | `204`, including when the id is already gone. A bad id is `400`. The wrong key is `401`. |
| anything else | | `404` |

The write key on `GET /reply/<nonce>` is `401`. A missing `Authorization` header is `401`. Keys are compared with `crypto.subtle.timingSafeEqual` on equal-length byte arrays (Workers Web Crypto). A different length is `401`.

`expirationTtl` must be at least 60 seconds (Cloudflare KV write docs). This Worker uses 600.

## Limits

A ping is 1 write, at most 30 reads, and 1 delete. A pairing is 1 put, 1 delete on read, and at most 1 `DELETE`. Free KV is 100,000 reads/day, 1,000 writes/day (different keys), 1 write/second on the same key, 1 GB storage, 512-byte keys, and 25 MiB values [F]. About 1,000 pings/day is the write cap [F]. Deletes count against the same 1,000/day write budget [F].

The relay stores the pairing box as it arrived. It does not have the encryption key. A KV dump is ciphertext.

The miss limiter is in memory inside one isolate. It counts `404` misses only. It does not write to KV. The Workers Rate Limiting binding is not used. The docs show the binding syntax, and they do not say the free plan includes it, so this Worker leaves it out.

Single use is best effort. `GET` returns the box and deletes with `waitUntil`. Two reads in different locations can both succeed, because KV has no transactions. The entry also expires. `PAIR_TTL_SECONDS` is clamped to 60 through 300, and the default is 300. That covers the cross-location lag measured in P0-e(c).

## Consistency

KV is eventually consistent. Changes may take up to 60 seconds or more to be visible in other global network locations [F]. A read of a missing key can stay missing for a while after the write (negative caching). The exact cache behavior is UNVERIFIED until P0-e(b) and P0-e(c) record numbers. That result decides KV versus a Durable Object (decision D-1). The app's poll budget is 120 seconds. A later manual "Check again" is one extra read.

## Durable Object option

Not built. Same HTTP API and the same two keys, so the app and the routine would not change. A `ReplyBox` SQLite-backed Durable Object, addressed by `idFromName(nonce)`, would store `{text, exp}`, delete on read inside the object, and alarm at 600 seconds to drop leftovers. The migration tag `new_sqlite_classes` is UNVERIFIED. Cost is about 3 Durable Object requests per poll cycle. If D-1 selects it, it lands as `relay/src/do.js` with `STORE=do|kv` in `wrangler.toml`, and the same `smoke.mjs` must pass.

## Rotate a key

```
npx wrangler secret put WRITE_KEY
```

or `READ_KEY`. Then push a new setup file to the headset so the app gets the new read key. The previous key stops working on the next Worker request. Do not put the new key in a file in this repo or in chat.
