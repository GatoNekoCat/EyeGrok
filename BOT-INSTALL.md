# BOT-INSTALL

You are a Grok Bot. Read this, then tell the human the steps in plain words. Read the first-use script aloud, as written. Do not add a flag, a host, a key, or a path. Do not repeat real keys in chat or write them into a file.

## What you need

The human needs a Steam Frame with Lepton Development in the Steam Library, and a Windows PC on the same Wi-Fi.

## How it works today (v0.1)

This version is 0.1.0, unreleased. A self-hosted relay is required. The human uses their own free Cloudflare account, signs in once, and does the setup in docs/RELAY.md. The sign-in is `npx wrangler login`. EyeGrok does not run a shared relay.

The headset does not read the reply on the webhook response. A webhook result of 200 means the Bot accepted the call and started a run. It does not mean the run finished. The routine posts the reply to the relay. The headset polls the relay for up to 120 seconds.

Before the command, the human turns on Developer Mode on the Frame and starts Lepton Development from the Steam Library. Leave Lepton running. When the relay is up, run this and pass the values as one JSON line on stdin. Never as arguments. Never as environment variables.

```
installer\EyeGrok-Setup.ps1 -Headless -Mode full -PairMode usb
```

The script prints `STEP tools`, `STEP frame`, and `STEP install` first. It prints `STEP values` and then waits. Send this one line then. No second line.

```json
{"webhookUrl":"https://REPLACE_ME","webhookKey":"REPLACE_ME","relayUrl":"https://REPLACE_ME","relayReadKey":"REPLACE_ME"}
```

Those four names are the fields `Test-SetupValues` checks. Both URLs must start with `https://`. This command does not pass `-AllowLoopbackHttp`, so an `http://` address is rejected. `webhookKey` and `relayReadKey` must be 16 to 512 characters with no whitespace. The relay rejects a read key shorter than 32 characters, and the read key must differ from the write key. The write key is not in this line. It stays with the Bot.

`pairKey` is not in this line. `-PairMode usb` does not post a pairing box and does not require `pairKey`.

Before you send the line, the human opens EyeGrok on the Frame and leaves it open. The script then prints `STEP import` and checks up to 20 times, with a quarter-second pause between checks, that the headset took the file. The last lines on success are `Imported` and `OK`.

The hands-free trigger is a Grok Bot routine with an email trigger on this bot's own Grok Bot inbox. The ping text rides in the email subject.

The headset does not send that mail. It posts the spoken words to `webhookUrl` as JSON, fields `text` and `nonce`, with `webhookKey` as the Bearer token. It then reads the reply from `relayUrl` with `relayReadKey`. You post the reply to the relay with the write key. The reply draft is docs/ROUTINE_TEMPLATE.md. Tell the human to hold to talk, then read the text that comes back from the relay.

## Coming next

Not available yet. Do not set these up, and do not tell the human they work today.

- No Cloudflare account.
- The PC acts as the relay.
- A shared doorbell wakes the bot.
- A 5-minute fallback with no cloud.

## What the human does

Tell the human to do these, in this order.

1. Start Lepton Development from the Steam Library on the Frame, and leave it running.
2. Put the headset on.
3. Hold to talk. Say a short question in English. Let go.

The reply is text on the headset. It can take up to 120 seconds. `Check again` does one more read. `Cancel` returns to Idle. After setup, Mic and Relay should read green. Routine stays `not tested` until the human taps `Test ping`.

## First-use script

Read this aloud after the installer prints `OK`.

> Start Lepton Development from the Steam Library on your Frame, and leave it running. Put the headset on. Open EyeGrok. Press and hold the talk button, ask a short question in English, and let go. The answer shows as text. It can take up to two minutes. If nothing shows, use Check again once.

## Troubleshooting

On a failed step the installer prints a key, then a sentence from `Get-EyeGrokErrorText`. Constrained Language Mode and `BAD values` print one line only. Tell the human the sentence. Where it says click Retry, run the same command again after the fix. This headless run has no Retry button.

| What the installer prints | What to do |
|---|---|
| PowerShell is in Constrained Language Mode. EyeGrok Setup cannot run. | The shell is locked. This installer cannot run there. |
| Accept the Android SDK Platform-Tools terms before download. | The script also prints a `LICENSE` line. That line is the terms page. Ask the human to accept. Then run the command again with `-AcceptPlatformToolsLicense`. If the installer already finds adb, it skips the download. |
| Could not download Android platform-tools. Check your internet connection, then click Retry. | Check the network, then run the command again. The script prints this same sentence if it throws. |
| The downloaded platform-tools did not pass the signature check. Nothing was run. | Nothing from that download was started. Run the command again. |
| No Frame found. Check the USB cable, turn on Developer Mode, and start Lepton Development from the Steam Library on the Frame. Then click Retry. | Cable, Developer Mode, and Lepton Development. Then run the command again. |
| This PC cannot find a headset named frame. On the Frame, open Quick Settings, click your Wi-Fi network, and enter the IP address shown there. Steam Settings, then Internet, shows the same address, for example 192.168.4.80. Make sure no two devices share the hostname frame. | The human reads the address on the Frame. Run the command again with `-FrameHost` set to that address. Only one device may use the name frame. The sample address in the sentence is the installer's example. |
| The Frame refused the Wi-Fi connection. Start Lepton Development from the Steam Library on the Frame, or any Android game from Steam, then click Retry. | Start Lepton Development from the Steam Library on the Frame. Then run the command again. |
| The headset did not answer over Wi-Fi. Put this PC and the Frame on the same network, and turn off client isolation on the router. Or use USB instead. Make sure no two devices share the hostname frame. | Same Wi-Fi, client isolation off, and only one device named frame. Or run again with `-FrameUsb`. |
| That is not an IPv4 address. Enter four numbers from 0 to 255, separated by dots. | Printed only if you passed `-FramePrompt` and the next stdin line is not an IPv4 address. This command does not pass `-FramePrompt`. |
| The Frame is connected but has not allowed this PC. Put on the headset, allow USB debugging, then click Retry. | The human puts on the headset, allows USB debugging, and you run the command again. Whether the Frame shows that prompt is UNVERIFIED. |
| The Frame is connected, but Lepton Development is not running. Start Lepton Development from the Steam Library on the Frame, then click Retry. | Start Lepton Development from the Steam Library on the Frame. Then run the command again. |
| EyeGrok did not install (INSUFFICIENT_STORAGE). Free some space on the Frame. | Free some space on the Frame, then run the command again. |
| EyeGrok did not install (UPDATE_INCOMPATIBLE). Remove the old EyeGrok first. | Remove the old EyeGrok, then run the command again. Any other code inside the parentheses is printed with "Try again." |
| No EyeGrok APK next to the installer. Click Browse to pick one. | This run has no Browse button. Put an EyeGrok APK next to the installer, or pass `-ApkPath`. The file name starts with `EyeGrok-` and ends with `.apk`. |
| BAD values | The JSON line was missing or failed the four checks above. Fix the line and run the command again. The script does not name the bad field. |
| The headset did not take the setup. Open EyeGrok on the Frame and try again, or use another pairing tab. | Open EyeGrok on the Frame before you send the JSON line. Then run the command again. |
| Pairing is off on your relay. Add PAIR_KEY (see docs/RELAY.md), or use the USB push tab. | Not printed for `-PairMode usb`. For `qr`, `code`, or `all`, this is any pairing result other than 201, 401, or 403. |
| The relay rejected the pairing key. | Not printed for `-PairMode usb`. For `qr`, `code`, or `all`, the pair key failed the installer check, or the relay returned 401 or 403. It must differ from the write key and the read key. |
