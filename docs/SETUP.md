# Setup

EyeGrok on a Steam Frame is a flat app. You hold a button, speak, and read the reply. You bring your own Bot webhook and your own relay. The app stores the four setup values in the Android Keystore.

Before you start: your relay (with PAIR_KEY) and Bot webhook, see docs/RELAY.md.

1. Turn on Developer Mode on the Frame.
2. Start Lepton Development from the Steam Library on the Frame.
3. Plug the Frame into the PC with USB.
4. Double-click `EyeGrok-Setup.cmd` and follow it.
5. In EyeGrok, scan the QR or enter the code, then hold to talk.

The installer downloads Android platform-tools only after you click Accept and download. Nothing is fetched before that. adb is stored under the EyeGrok folder in Local AppData.

After a pair, the checklist opens once. Mic and Relay should read green. Routine stays grey and reads `not tested` until you tap `Test ping`.

Hold `Hold to talk`, speak a short English question, and release. A reply can take up to 120 seconds. `Check again` does one more read. `Cancel` returns to Idle. `Forget setup` is a 3 second hold.

## When something fails

| What you see | What to do |
|---|---|
| No Frame found. Check the USB cable, turn on Developer Mode, and start Lepton Development from the Steam Library on the Frame. Then click Retry. | Check the cable, Developer Mode, and Lepton, then click Retry. |
| The Frame is connected but has not allowed this PC. Put on the headset, allow USB debugging, then click Retry. | Allow USB debugging on the Frame, then click Retry. Whether the Frame shows that prompt is UNVERIFIED. |
| The Frame is connected, but Lepton Development is not running. Start Lepton Development from the Steam Library on the Frame, then click Retry. | Start Lepton Development from the Steam library on the Frame. |
| EyeGrok did not install (INSUFFICIENT_STORAGE). Free some space on the Frame. | Free some space, then install again. |
| EyeGrok did not install (UPDATE_INCOMPATIBLE). Remove the old EyeGrok first. | Use the button that uninstalls `io.github.eyegrok.ping`, then install again. |
| The headset did not take the setup. Open EyeGrok on the Frame and try again, or use another pairing tab. | Open EyeGrok on the Frame, or switch pairing tab. |
| Pairing is off on your relay. Add PAIR_KEY (see docs/RELAY.md), or use the USB push tab. | Add PAIR_KEY, or use the USB push tab. |
| The relay rejected the pairing key. | PAIR_KEY must differ from the write key and the read key. |
| No EyeGrok APK next to the installer. Click Browse to pick one. | Place the APK next to `EyeGrok-Setup.cmd`, or click Browse. |
| Could not download Android platform-tools. Check your internet connection, then click Retry. | Check the network, then click Retry. |
| The downloaded platform-tools did not pass the signature check. Nothing was run. | Nothing from that download was started. Click Retry, or put `adb` on PATH yourself. |
| Accept the Android SDK Platform-Tools terms before download. | Click Accept and download. |
| Windows blocks `EyeGrok-Setup.cmd` or `EyeGrok-Setup.ps1`. | Right-click the file, open Properties, and check Unblock. Then run it again. |

## Advanced

`tools/push-setup.ps1` and `tools/push-setup.sh` push a setup file that lives outside this repo. They are deprecated. Use them only when the installer's USB push tab cannot reach the headset. Neither script prints the file. Probe steps stay in `docs/P0_RESULTS.md`. The threat model is `docs/PAIRING.md`.
