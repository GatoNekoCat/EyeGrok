#!/bin/sh
# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Downloads the pinned Whisper model and checks SHA-256. A mismatch deletes the file and exits 1.

set -eu
cd "$(dirname "$0")/.."
dest="unity/Assets/StreamingAssets/Whisper/ggml-tiny.en-q5_1.bin"
url="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en-q5_1.bin"
pin="tools/models.sha256"

expected="$(awk 'NF && $1 !~ /^#/ && $1 ~ /^[0-9a-fA-F]{64}$/ { print tolower($1); exit }' "$pin")"
if [ -z "$expected" ]; then
  echo "models.sha256 has no sha256" >&2
  exit 1
fi

mkdir -p "$(dirname "$dest")"
if [ ! -f "$dest" ]; then
  echo "MODEL_FETCH=start"
  curl -L --fail --retry 3 --output "$dest" "$url" || {
    rm -f "$dest"
    echo "download failed" >&2
    exit 1
  }
fi

if command -v sha256sum >/dev/null 2>&1; then
  actual="$(sha256sum "$dest" | awk '{ print tolower($1) }')"
else
  actual="$(shasum -a 256 "$dest" | awk '{ print tolower($1) }')"
fi

if [ "$actual" != "$expected" ]; then
  rm -f "$dest"
  echo "model hash mismatch" >&2
  exit 1
fi

echo "MODEL_CHECK=ok"
exit 0
