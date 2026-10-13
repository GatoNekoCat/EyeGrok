# P0 results

No account names, hostnames, or paths. Dates are UTC dates of the run that filled the row.

| Step | Result | Notes | Date |
|---|---|---|---|
| P0-a | FAIL (email) | Email and IMAP cannot send unattended. Relay chosen. See facts. | 2026-10-13 |
| P0-b | PASS | Empty scene, IL2CPP, SIGNING=debug. native-code arm64-v8a only. sdkVersion 30. targetSdk 36. APK 35357303 bytes. | 2026-10-13 |
| P0-c | NOT RUN | Headset. OpenXR quad and button. | |
| P0-d | NOT RUN | Headset. Mic, STT, services, keystore, setup path. | |
| P0-e(a) | NOT RUN | Deployed same-location write then poll, 10 trials. Another operator deploys. | |
| P0-e(b) | NOT RUN | Deployed poll-before-write, 10 trials. Decides KV negative caching. | |
| P0-e(c) | NOT RUN | Cross-location, with P0-f. 5 trials. | |
| P0-f | NOT RUN | Live routine. Outbound POST approval is UNVERIFIED until this row is filled. | |

Decision gates D-1 through D-4 are not decided. Slice 3 does not start until they are recorded here.

## Local relay smoke

`scripts/smoke.mjs` against `wrangler dev` on this host is recorded in the Slice 1 report. It is not P0-e(a) or P0-e(b). Local KV does not measure cross-location delay.
