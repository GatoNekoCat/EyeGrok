# EyeGrok relay

EyeGrok v0.1 requires this Worker. The headset posts to `POST /ping`, which emails the bot's own Grok Bot inbox. The bot routine has an email trigger and POSTs its reply to `/reply`.

Cloudflare Worker. One KV key per nonce, read once, gone after 600 seconds. No runtime dependencies. `wrangler` (see `package.json`) is a dev tool and is not part of the APK. Its license is UNVERIFIED and it is not redistributed.

Full API, limits, and the Durable Object fallback: `docs/RELAY.md`.

## Storage seam

V-001 stores each reply in Workers KV. D-1 is KV, provisional, pending headset results. No Durable Object code is in this repo. `relay/src/index.js` is the KV mailbox. A later switch would replace those KV calls and keep `POST /reply` and `GET /reply/<nonce>`.

Keys never go in files or chat.

## Free account setup

1. `npm ci`
2. `npx wrangler login`
3. `npx wrangler kv namespace create REPLIES`
4. Copy `wrangler.example.toml` to `wrangler.toml` and paste the namespace id over `REPLACE_ME`.
5. Generate two 32-byte base64url keys in the shell. Pipe each into `npx wrangler secret put WRITE_KEY` and `npx wrangler secret put READ_KEY`. Do not echo them.
6. `npx wrangler deploy`
7. `RELAY_URL=... RELAY_WRITE_KEY=... RELAY_READ_KEY=... node scripts/smoke.mjs`

`wrangler dev` runs the same Worker locally and does not need a login. Local KV does not show cross-location delay. Deployed timing is P0-e(a) and P0-e(b) in `docs/P0_RESULTS.md`.

## Limits

Workers Free: 100,000 requests/day, 10 ms CPU/request, reset 00:00 UTC, error 1027 when over [F]. KV Free: 100,000 reads/day, 1,000 writes/day, 1 write/second on the same key [F]. A ping is 1 write, at most 30 reads, and 1 delete, so about 1,000 pings/day [F].

KV may take 60 seconds or more to show a write in another location [F]. `expirationTtl` is 600, above the 60 second minimum in the KV write docs.

## Rotate

`npx wrangler secret put` the key you are replacing, then push a new setup file to the device for a new read key. Do not store the key in this repo.
