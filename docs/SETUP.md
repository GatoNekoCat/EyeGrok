# Setup

EyeGrok on a Steam Frame is a flat app. You hold a button, speak, and read the reply. You do not need a keyboard on the headset.

You bring your own Grok Bot webhook and your own relay. The app stores those four values in the Android Keystore. It does not ship them.

## What you need

- A Frame with Developer Mode on.
- **Lepton Development** started from the Steam library on the computer. Leave it running.
- A USB cable.
- `adb` on the computer.
- The app APK, `Builds/EyeGrok-v001.apk`, or the same file from a release.
- A setup file that is not inside this repo.

## Connect

1. On the Frame, turn on Developer Mode. Valve's setup page is https://partner.steamgames.com/doc/steamhardware/steamframe/setup. Exact menu names on that page are UNVERIFIED.
2. Plug in USB.
3. `adb forward tcp:5555 tcp:5555`
4. `adb connect localhost:5555`
5. `adb devices` lists `localhost:5555`.

If `adb devices` is empty, start Lepton Development again, then repeat the forward and the connect.

## Install

`adb install -r Builds/EyeGrok-v001.apk`

Launch EyeGrok once from the headset, or with:

`adb shell monkey -p io.github.eyegrok.ping 1`

The first screen says `Not set up — see docs/SETUP.md`. That launch creates the folder the setup file is pushed into.

## Setup file

Copy `tools/setup.example.json` to a path outside this repo. Fill in the four fields. Use `https` URLs. Each key is 16 to 512 characters with no spaces.

- `webhookUrl` and `webhookKey` are the Bot webhook.
- `relayUrl` and `relayReadKey` are the relay read side. The read key is not the write key. The write key stays in the Bot's secrets. See `docs/RELAY.md` and `docs/ROUTINE_TEMPLATE.md`.

Push it. The script does not print the file.

Windows:

`powershell -File tools/push-setup.ps1 C:\path\outside\eyegrok-setup.json`

Other shells:

`sh tools/push-setup.sh /path/outside/eyegrok-setup.json`

The script copies the file to the app's external files directory, restarts the app, and waits up to 20 seconds. `IMPORTED` means the app read the file and deleted it on the device. `NOT_IMPORTED` means the file is still there. Delete your local copy either way.

Logs: `adb logcat -s Unity`. A good import does not log the URLs or the keys. The setup line on screen reads `Configured`. Status reads `Idle — Hold to talk`. While the voice model loads, status can read `Loading voice model…`.

## First ping

Use the pointer. No keyboard.

The first hold can ask for the microphone. Lepton auto-grants that permission, so the prompt may not appear. If it does, allow the microphone and hold to talk again. A hold while the prompt is up can come back empty.

1. Press and hold **Hold to talk**. Status reads `Recording — Release to send`.
2. Speak a short English question. Hold at least half a second. Releasing outside the button also sends.
3. Status moves through `Transcribing`, `Sending`, and `AwaitingReply — Waiting for reply`. The transcript shows on screen.
4. The reply shows on screen and status reads `Done — Done`. This can take up to 120 seconds. The cross-location relay wait is the slow part.
5. If the wait ends first, the screen says `no reply in 120 s. The reply may still arrive. Use Check again.` **Check again** does one more read.
6. **Cancel** during the wait returns to Idle and stops further requests.
7. **Forget setup** is a 3 second hold. It deletes the stored values and returns to the not-set-up screen.

The release build has no typing box. That box exists only in the Editor and in a Development build.

## Probes

The XR probe and the flat probe are separate APKs with the same package id. Installing one replaces the other. Pass and fail lines for P0-c, P0-d, and the ping are in `docs/P0_RESULTS.md`.

## When something fails

- Setup line says `Setup file invalid:` plus a field name. Fix that field. The bad file is already deleted on the device. Push a new one.
- Status says `webhook 401`. The webhook key was rejected. Rotate it and push a new setup file.
- Status says `relay 401` or `relay 400`. The read key or the nonce was rejected. Push a new setup file if the read key changed.
- No reply after 120 seconds. Read the latency section in `docs/RELAY.md`. Use Check again once. P0-f (whether the Bot posts without an approval prompt) is still open until that row in `docs/P0_RESULTS.md` is filled.
- `Too short` means the hold was under half a second.
- `Didn't catch that` means Whisper returned nothing useful. Try again, closer to the mic.

`adb logcat -s Unity` is the filter. App lines look like `stt len=`, `reply len=`, and `nonce=` plus 6 hex characters. They do not contain the words you said or the reply.
