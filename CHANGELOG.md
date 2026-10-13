# Changelog

All notable changes to EyeGrok are documented here.

The format is Keep a Changelog. This project uses semantic versions. `0.1.0` is unreleased.

## [0.1.0] - unreleased

### Added

- Android probe builds for the OpenXR scene and the flat scene, plus the on-device Whisper model fetch and hash pin.
- whisper.unity pinned to commit `e951e4a4c6e44c781b1d36bb8dc5bf1b7bae9687`.
- Headset checklist for the Frame probes in `docs/P0_RESULTS.md`.

### Changed

- Provisional decisions, pending headset results. D-1 is KV. D-2 is Mode B for the app. D-3 is debug signing. D-4 leaves TTS out of this version (plan section 6: no Lepton TTS engine, and Piper / espeak-ng are GPL).
- Deployed relay smoke and P0-e timing are recorded in `docs/P0_RESULTS.md`. The Durable Object switch was not made. That call is still pending the owner. P0-e(c) median 28.2 s, max 48.4 s.
