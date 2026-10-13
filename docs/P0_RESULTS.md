# P0 results

No account names, hostnames, or paths. Dates are UTC dates of the run that filled the row.

| Step | Result | Notes | Date |
|---|---|---|---|
| P0-a | FAIL (email) | Email and IMAP cannot send unattended. Relay chosen. See facts. | 2026-10-13 |
| P0-b | PASS | Empty scene, IL2CPP, SIGNING=debug. native-code arm64-v8a only. sdkVersion 30. targetSdk 36. APK 35357303 bytes. | 2026-10-13 |
| P0-c | APK built, headset NOT RUN | Probe APK 70726490 bytes. OpenXR loader is in the APK. Khronos Simple Controller is the only enabled feature. Meta and Oculus features are off. SIGNING=debug. | 2026-10-13 |
| P0-d | APK built, headset NOT RUN | Flat probe APK 68296869 bytes. OpenXR loader is not in this APK. Permissions are INTERNET and RECORD_AUDIO. SIGNING=debug. | 2026-10-13 |
| P0-e smoke | PASS | Deployed worker. health 200, no-auth 401, write-key-on-read 401, bad nonce 400, oversize 413, write 204, read 200 match, re-read 404. | 2026-10-13 |
| P0-e(a/b) | PASS | Same location, visibility after 5 negative lookups. 0.48 to 0.58 s. 5 of 5. Operator report. The plan asked for 10 trials. These 5 are what was recorded. | 2026-10-13 |
| P0-e(c) | MEASURED | Cross location. Writer and reader in different Cloudflare locations. 1 s polling after negative lookups. Trials 28.2, 12.9, 48.4, 33.1, 17.8 s. Median 28.2 s. Max 48.4 s. Near the escalation line (median over 30 s). Two trials are over 30 s. Durable Object switch was not made. Decision pending the owner. | 2026-10-13 |
| P0-f | NOT RUN | Live routine. Outbound POST approval is UNVERIFIED until this row is filled. | |

## Decisions

Each decision below is provisional, pending headset results.

| Gate | Decision | Why |
|---|---|---|
| D-1 | KV | The owner set KV for this build. No Durable Object code. The storage seam stays in `relay/` as a comment only. The app poll waits up to 120 s and keeps Check again, because P0-e(c) max was 48.4 s and the median is close to 30 s. |
| D-2 | Mode B only | The app is the flat scene. The P0-c XR probe APK is still built. Mode A waits on the headset result. |
| D-3 | Debug signing | No release keystore in this build. `SIGNING=debug`. |
| D-4 | TTS deferred | Reply text only. Lepton has no Android TTS engine (P0-d logs the engine count to confirm). Piper, piper.unity, and espeak-ng are GPL-3.0 and stay out of the Apache-2.0 APK. sherpa-onnx license, size, and Unity ARM64 integration are UNVERIFIED. |

Slice 3 may start with these rows recorded. Headset runs can still change D-1 and D-2.

## Build record

Host paths are not listed. Sizes are bytes.

| APK | Bytes | Notes |
|---|---|---|
| P0-b empty | 35357303 | Slice 1. |
| P0-c XR probe | 70726490 | `libopenxr_loader.so` and `libUnityOpenXR.so` present. Permissions: INTERNET, RECORD_AUDIO, `org.khronos.openxr.permission.OPENXR`, `org.khronos.openxr.permission.OPENXR_SYSTEM`. allowBackup false. usesCleartextTraffic false. Activity entry. arm64-v8a. sdk 30. target 36. |
| P0-d flat probe | 68296869 | No `libopenxr_loader.so`. Permissions are exactly INTERNET and RECORD_AUDIO. allowBackup false. usesCleartextTraffic false. Activity entry. arm64-v8a. sdk 30. target 36. |

Whisper model `ggml-tiny.en-q5_1.bin` SHA-256 `c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b`, 32166155 bytes. The hash is the Hugging Face LFS oid. whisper.unity commit `e951e4a4c6e44c781b1d36bb8dc5bf1b7bae9687` (tag 1.4.0).

## Frame test checklist

Do this on the Frame. The coder does not run it.

### Connect

1. On the headset, turn on Developer Mode.
2. On the computer, start **Lepton Development** from the Steam library. Leave it running.
3. Plug in USB.
4. `adb forward tcp:5555 tcp:5555`
5. `adb connect localhost:5555`
6. `adb devices` lists `localhost:5555`.

Filter Unity logs with `adb logcat -s Unity`. Probe lines start with `P0C` or `P0D`. App lines use `len=` and never print the transcript or the reply.

### P0-c

`adb install -r` the XR probe APK, then launch the app. The package id is the same for every variant, so the install replaces the previous one.

Expected screen: a world-space panel about 1.5 m in front of you. The label reads `EyeGrok P0-c`. A controller ray can press it.

PASS when all of these are true:

- The panel is visible and the ray press logs `P0C_BUTTON_PRESSED`.
- Log has `P0C runtime=` with a name and `ver=`.
- Log has `P0C gfx=` (Vulkan or OpenGLES3).
- Log has `P0C ext=` with the runtime extension list.

FAIL if the panel never shows, or a press does not log `P0C_BUTTON_PRESSED`. Record FAIL in the table. Do not treat a missing Frame controller profile as a pass. The Valve Frame profile is not in this APK. Whether Lepton maps the Frame controller onto the Khronos Simple Controller profile is UNVERIFIED.

### P0-d

`adb install -r` the flat probe APK, then launch. Expected screen: a flat overlay titled `EyeGrok P0-d` with Hold, Record 3 s, Probe services, and Probe keystore. No keyboard is required.

PASS when all of these are true:

- Press and release Hold. Log shows `P0D pointer down` and `P0D pointer up`.
- Mic permission logs `P0D permission before=` and then granted (`P0D permission after=granted` or `before=True`). Lepton is expected to auto-grant. Confirm it.
- Log shows `P0D devices=` of at least 1, `P0D caps`, `P0D requested=16000`, and `P0D actual=` with the real rate. Lepton capture is expected at 48 kHz. Write the actual number down. Resample-to-16 kHz is not in the app until this number says it is needed.
- Say a short English phrase and press Record 3 s. Log shows `P0D model path=streaming` or `P0D model path=persistent`, `loaded=True`, `P0D transcribe ms=` and `P0D transcript` with recognizable words. Target is at or under 8 s. That target is UNVERIFIED until this run. The probe is allowed to log the transcript. The app is not.
- Probe services logs `P0D services rec=false services=0 engines=0`. `tts=` is the Android init code. Expected from the Frame control APKs is no recognizer and no TTS engine. Confirm the numbers.
- Probe keystore logs `P0D keystore=ok`.
- Log shows `P0D external=` and `P0D setup-file=` true or false. The line must not contain file contents.

### Ping

Install the app APK with `adb install -r`, launch it once so the external files directory exists, then run `tools/push-setup.ps1` or `tools/push-setup.sh` with a setup file that lives outside the repo. The script prints `IMPORTED` or `NOT_IMPORTED`. It does not print the file. Delete the local file after import.

Expected screen states, pointer or controller only:

1. Before setup: status `Unconfigured`, setup line `Not set up — see docs/SETUP.md`.
2. After import: setup line `Configured`, status `Idle — Hold to talk`. While the model loads, the status can read `Loading voice model…`.
3. Hold the large Hold to talk button. Status `Recording — Release to send`. Releasing outside the button counts as release. Under half a second returns to Idle with `Too short`.
4. After release: `Transcribing`, then `Sending`, then `AwaitingReply — Waiting for reply`. The transcript line shows the words. Logs show `stt len=` and `nonce=` (6 hex chars), never the words.
5. When the reply arrives: status `Done — Done`, reply text on screen. Log shows `reply len=` only.
6. If nothing arrives: after 120 s, status Error with `no reply in 120 s. The reply may still arrive. Use Check again.` Check again does one more read. Cancel during the wait returns to Idle and stops further requests.
7. Forget setup is a 3 second hold. It returns to Unconfigured.

A debug typing panel exists only in Editor and Development builds. The release APK has Development Build off, so that panel is absent. The hold button, Cancel, Check again, and Forget setup must be usable with a pointer and no keyboard.

PASS for one ping: hold, speak a short English question, release, see the transcript, see a reply at or under 120 s after send. Do this 3 times. At least 2 of 3 pass while D-1 stays KV. P0-f is still required before calling the Bot path proven, because outbound POST approval is UNVERIFIED.
