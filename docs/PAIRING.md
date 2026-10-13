<!--
Copyright 2026 EyeGrok contributors
SPDX-License-Identifier: Apache-2.0
-->

# Pairing

The installer encrypts the setup JSON before the relay stores it. The relay checks the envelope shape and size. It does not have the key. A KV dump without the QR or the code is ciphertext.

## Assets

The setup JSON holds four values: webhook URL, webhook key, relay URL, and relay read key. PAIR_KEY is a separate relay secret. The installer asks for it once per session, sends it as a bearer token, and does not write it to disk. The write key stays in the Bot. It is not an installer field.

## Actors

A network attacker can send requests to the relay, or watch the TLS endpoints only as far as the certificate allows. A KV reader, including someone with Cloudflare-side access to the namespace, sees stored boxes and key names. A person or a camera can see the QR or the 11-digit code during its lifetime. Malware on the PC can read the installer process. Another app on the headset can read the app's external files directory if the platform lets it.

## Shared controls

The box is version 1, a 16-byte iv, ciphertext, and a 32-byte mac, each base64url. The MAC input is the ASCII bytes of EG1, a zero byte, the id, the iv, and the ciphertext. Both the installer and the app check the MAC in constant time, and decrypt only after it matches. A bad MAC stores nothing.

The construction is AES-256-CBC with HMAC-SHA256, encrypt-then-MAC. `AesGcm` is absent on .NET Framework 4.8, which is what Windows PowerShell 5.1 uses, and it is absent on the Unity profile this app builds. Both runtimes have `Aes`, `HMACSHA256`, and `RandomNumberGenerator`. CBC plus HMAC is confidentiality and integrity in managed code, so EditMode can test the same bytes the installer produces. Crypto stays on those managed types.

The iv is 16 new random bytes for every entry. On the QR path the id is 16 random bytes and the secret is 32 random bytes. The generator is `RandomNumberGenerator`.

The default lifetime is 300 seconds. `PAIR_TTL_SECONDS` is clamped to the range 60 through 300. A pairing costs one put, one delete when the headset reads, and at most one extra DELETE from the installer. The read returns the box and deletes with `waitUntil`. KV has no transactions, so two locations can both succeed if they read before either delete lands. The second reader still needs the QR secret or the code. That race is accepted.

A 404 on the headset retries up to 6 times, 5 seconds apart, then stops. Those calls are reads. They do not write.

More than 10 misses from one address in 60 seconds returns 429 for the rest of that window. The counter is in memory in one isolate. It counts misses only. The Workers Rate Limiting binding is not used. The limit is best effort when many isolates are running, and it is strict under `wrangler dev`.

Logs record an error name, a step, or an exit code. They do not record the id, the code, the URL, or the setup JSON.

## QR path

The payload text is `EG1`, the relay URL, the id, and the secret, separated by bars. The URL has to pass the same https rule as setup, and it cannot contain a bar, a question mark, or a hash. HKDF-SHA256 of the secret, with distinct labels, produces a 32-byte encryption key and a 32-byte MAC key. There is no password stretching. The id is 128 bits and the secret is 256 bits, so guessing either value is not a practical attack.

What is exposed: the QR image on the PC screen for up to 300 seconds. What mitigates it: the short life, best-effort single use, and the fact that the secret is not stored on the headset as a QR. Residual risk: a photo of that image during the window decrypts that one payload. After the app imports it, the four values are in the Android Keystore.

## Code path

The installer draws 10 random digits and appends one Damm check digit. The screen shows the 11 digits in groups of 4, 4, and 3. A wrong check digit shows "Check the code" on the headset and does not call the relay.

Those 10 digits are the PBKDF2 password. The salt is the ASCII string `eyegrok-pair-v1`. The hash is HMAC-SHA256. The iteration count is 100,000. The output is 32 bytes. HKDF of that output with label `id` supplies the first 16 bytes of the KV id. Labels `enc` and `mac` supply the 32-byte keys. Stretching the id means the KV key name is not a cheap copy of the digits.

Online, one guess is one GET. One address is also capped by the miss limiter. The free plan allows 100,000 requests a day. Ten digits is 10 billion candidates. Spending the entire daily quota inside one 300 second window is 100,000 divided by 10 billion, about 1 in 100,000, for a single live code. The check digit does not shrink that search. It only stops typos before a request.

Offline, a KV dump taken inside the 300 second life still requires about 10 billion times 100,000 PBKDF2-SHA256 iterations per code. That is years of GPU time.

What is exposed: the 11 digits on the PC screen, plus the relay URL the headset already has or just received. Residual risk: a photo of the code during the window is enough to fetch and decrypt, once the attacker knows the relay URL. The offline cost above is the case where the attacker has the ciphertext and not the digits.

## Relay URL on a first code pair

The code carries no relay URL. If the Keystore already has one, the app uses it. On a first run the user ticks that the headset has never been paired. The installer then streams only the relay URL on adb stdin into `eyegrok-pair-origin.txt`. The app reads the file, deletes it, and keeps the URL in memory as the pending origin.

USB is already required for the first install, so this bootstrap does not need another service. A public rendezvous would be someone else's host. Typing the URL needs a keyboard the release build does not have. Putting the URL in an intent extra would place it on the adb command line.

What is exposed: the relay hostname, briefly, in the app files directory. Residual risk: the hostname is not a key. The read key is what protects the mailbox. Another app that can read that directory during the gap learns the hostname and nothing else from this file.

## USB push

The USB push tab writes the setup JSON in the clear into the app files directory, as `eyegrok-setup.json`. The first attempt is adb `exec-in` with the bytes on stdin. The fallback is adb `shell` with the same stdin. The JSON is not a command-line argument. The app reads the file, stores the four values, and deletes it. A `.part` file is ignored and deleted.

What is exposed: plaintext setup on headset storage until import finishes. Residual risk: a reader of that directory in the gap gets all four values. PAIR_KEY is not in the file.

If both stdin paths fail, the installer writes a temp file under the EyeGrok `tmp` directory, limits the ACL to the current user, pushes that file, then overwrites it with zeros and deletes it in a `finally` block. The log line is `b3 code=0 key=tempfile`. Residual risk: the file exists for the length of the push. A crash before `finally` can leave it on the PC disk. The log does not contain the JSON.

## Clipboard and process memory

`Paste blob` reads the clipboard into the masked boxes. `Clear clipboard` clears the current clipboard data. Windows clipboard history, opened with Win+V, can keep the pasted blob after that clear. Residual risk: a setup blob pasted on a shared PC can remain in that history.

While the window is open, the four values and PAIR_KEY live in the text boxes and in the PowerShell process. On close, Cancel, a new code, or Done, the installer deletes the live relay entry, clears every box, and drops the key variables. Residual risk: a memory dump during the session sees the values. `installer.log` has step names, exit codes, and error keys only.

## Quota

Random `GET /pair` requests spend the Workers daily request quota, 100,000, and do not spend the 1,000 writes. `POST /pair` still requires PAIR_KEY. A flood of reads can make the user's own pings fail until the quota resets at 00:00 UTC. That denial of service is accepted. Pairing has no separate request budget.

## Non-goals

A PC that is already running hostile code in the installer session is out of scope. Clipboard history is out of scope. A strict single-use guarantee across two Cloudflare locations at the same moment is out of scope. Hiding the relay hostname from headset storage on the first code pair is out of scope, and the file above is the accepted residue. Request-quota exhaustion is out of scope.

The camera stays off until the BL-2 row in `docs/P0_RESULTS.md` is PASS. The app build does not request CAMERA and does not include a QR decoder until then. The number pad and the USB push tab do not use the camera.

Android platform-tools are not part of EyeGrok and are not in the release zip. The installer downloads them only after Accept.
