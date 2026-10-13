# Testing

Run this on a Frame. The coder does not run it. Installing the XR probe, the flat probe, or the app replaces the others. The package id is `io.github.eyegrok.ping`.

## Connect

1. On the Frame, turn on Developer Mode. Valve's setup page is https://partner.steamgames.com/doc/steamhardware/steamframe/setup. Exact menu names on that page are UNVERIFIED.
2. On the Frame, start **Lepton Development** from the Steam library. Leave it running.
3. Plug in USB.
4. `adb forward tcp:5555 tcp:5555`
5. `adb connect localhost:5555`
6. `adb devices` lists `localhost:5555`.

If `adb devices` is empty, start Lepton Development again from the Steam library on the Frame, then repeat the forward and the connect.

Logs: `adb logcat -s Unity`. Probe lines start with `P0C` or `P0D`. App lines use `len=` and never print the transcript or the reply.

## P0-c

`adb install -r Builds/probes/EyeGrok-p0c-xr.apk`

Launch with `adb shell monkey -p io.github.eyegrok.ping 1`. Expected screen: a world-space panel about 1.5 m in front of you. The label reads `EyeGrok P0-c`. A controller ray can press it.

PASS when all of these are true:

- The panel is visible and the ray press logs `P0C_BUTTON_PRESSED`.
- Log has `P0C runtime=` with a name and `ver=`.
- Log has `P0C gfx=` (`Vulkan` or `OpenGLES3`).
- Log has `P0C ext=` with the runtime extension list.

FAIL if the panel never shows, or a press does not log `P0C_BUTTON_PRESSED`. Do not treat a missing Frame controller profile as a pass. The Valve Frame profile is not in this APK. Whether Lepton maps the Frame controller onto the Khronos Simple Controller profile is UNVERIFIED.

## P0-d

`adb install -r Builds/probes/EyeGrok-p0d-flat.apk`

Launch the same way. Expected screen: a flat overlay titled `EyeGrok P0-d` with Hold, Record 3 s, Probe services, and Probe keystore. No keyboard is required.

PASS when all of these are true:

- Press and release Hold. Log shows `P0D pointer down` and `P0D pointer up`.
- Mic permission logs `P0D permission before=True`, or `P0D permission before=False` and then `P0D permission after=granted`. `P0D permission after=denied` is a fail. Lepton is expected to auto-grant. Confirm it.
- Log shows `P0D devices=` of at least 1, one line `P0D caps min=` with `max=`, `P0D requested=16000`, and `P0D actual=` with the real rate. Lepton capture is expected at 48 kHz. Write `P0D actual=` down. Resample-to-16 kHz is not in the app until this number says it is needed.
- Say a short English phrase and press Record 3 s. Log shows `P0D model path=streaming` or `P0D model path=persistent`, then `P0D model load ms=` with `loaded=True`, then `P0D transcribe ms=` and `P0D transcript` plus recognizable words. `P0D transcript empty`, `P0D model path=fail`, or `P0D mic=fail` is a fail. Target is at or under 8 s. That target is UNVERIFIED until this run. The probe is allowed to log the transcript. The app is not.
- Probe services logs `P0D services rec=false services=0 engines=0 tts=` plus the Android init code. Expected from the Frame is no recognizer and no TTS engine. Confirm the numbers.
- Probe keystore logs `P0D keystore=ok`.
- Log shows `P0D external=` and `P0D setup-file=True` or `P0D setup-file=False`. The setup-file line must not contain file contents.

## App

`adb install -r Builds/EyeGrok-v001.apk`

Launch once. The setup line reads `Not set up — see docs/SETUP.md`. Status reads `Unconfigured — Not set up — see docs/SETUP.md`. That launch creates the folder the setup file is pushed into.

Push a setup file that lives outside this repo. The fields are in `docs/SETUP.md`. The script does not print the file.

`powershell -File tools/push-setup.ps1` plus that file, or `sh tools/push-setup.sh` plus that file.

`IMPORTED` means the app read the file and deleted it on the device. `NOT_IMPORTED` means the file is still there. Delete your local copy either way.

After `IMPORTED`, the setup line reads `Configured` and status reads `Idle — Hold to talk`. While the voice model loads, status can read `Idle — Loading voice model…`.

## Voice ping

Use the pointer. No keyboard. The release build has no typing box.

1. Press and hold **Hold to talk**. Status reads `Recording — Release to send`. Releasing outside the button also sends. Under half a second returns to `Idle — Too short`.
2. Status then reads `Transcribing — Transcribing`, then `Sending — Sending`, then `AwaitingReply — Waiting for reply`. The transcript shows on screen. An empty result reads `Error — Didn't catch that`. Logs show `stt len=` and `nonce=` plus 6 hex characters, never the words.
3. The reply shows on screen and status reads `Done — Done`. Log shows `reply len=` only. This can take up to 120 seconds.
4. If the wait ends first, status reads `Error — no reply in 120 s. The reply may still arrive. Use Check again.` **Check again** does one more read. A miss then reads `Error — No reply yet`. A hit reads `Done — Done`.
5. **Cancel** during the wait returns to `Idle — Hold to talk` and stops further requests.
6. **Forget setup** is a 3 second hold. Status returns to `Unconfigured — Not set up — see docs/SETUP.md`. The setup line reads the same text.

PASS for one ping: hold, speak a short English question, release, see the transcript, see a reply at or under 120 s after send. Do this 3 times. At least 2 of 3 pass. P0-f is still required before calling the Bot path proven, because outbound POST approval is UNVERIFIED.

## What to record

Fill the P0-c, P0-d, and ping rows in the table in `docs/P0_RESULTS.md`. The date is the UTC date of the run.

Write `P0C runtime=` and `ver=`, `P0C gfx=`, `P0D actual=`, `P0D transcribe ms=`, the `P0D services` counts, `tts=`, and `P0D keystore=ok` or the fail line. For the ping, write how many of 3 returned a reply at or under 120 s.

Do not write account names, hostnames, paths, keys, or deployment URLs. Do not copy `P0D external=`. It is a device path. Do not copy `P0D transcript` text or the reply.
