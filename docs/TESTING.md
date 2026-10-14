# Testing

Run this on a Frame. The coder does not run it. Probe procedures stay in `docs/P0_RESULTS.md`.

Before you start: a self-hosted relay is required. The headset posts to the relay `POST /ping`, which emails the bot's own Grok Bot inbox. The bot routine has an email trigger and POSTs its reply to `/reply`. See docs/RELAY.md. QR or code pairing also needs PAIR_KEY. USB push does not.

1. Turn on Developer Mode on the Frame. PASS when the installer gets past the Frame step.
2. Start Lepton Development from the Steam Library on the Frame. PASS when the installer does not stop on "The Frame refused the Wi-Fi connection. Start Lepton Development from the Steam Library on the Frame, or any Android game from Steam, then click Retry." and does not stop on "The Frame is connected, but Lepton Development is not running. Start Lepton Development from the Steam Library on the Frame, then click Retry."
3. Put the Frame on the same Wi-Fi as this PC. PASS when the Frame step connects. If the name does not resolve, enter the IP from Quick Settings (click your Wi-Fi network) or from Steam Settings, then Internet. That error starts "This PC cannot find a headset named frame."
4. Double-click `EyeGrok-Setup.cmd` and follow it. PASS when Tools, Frame, Install, Values, and Pair finish with no error from the table in `docs/SETUP.md`, and EyeGrok is on the headset.
5. In EyeGrok, scan the QR or enter the code, then hold to talk. PASS when the checklist shows Mic green, Relay green, and Routine grey `not tested` until `Test ping`. Hold to talk three times. At least 2 of 3 replies arrive at or under 120 seconds, and Routine turns green when a reply arrives.

USB fallback: click Use USB instead. The installer runs `adb forward tcp:5555 tcp:5555`, then `adb connect localhost:5555`. A PC USB port may not power the headset. Use a powered cable or adapter. If the headset has not allowed this PC, the installer stops on "The Frame is connected but has not allowed this PC. Put on the headset, allow USB debugging, then click Retry." Whether that prompt appears is UNVERIFIED.

The checklist opens from the main screen, and once after the first successful pair. It checks on open and on tap. Mic is green when the permission is granted and a microphone is present, otherwise red. Relay is green when health returns `ok`, otherwise red. Routine stays grey until `Test ping`, then green on Done and red on Error.

Record the ping count in `docs/P0_RESULTS.md`. Do not write account names, hostnames, paths, keys, or the words you said.
