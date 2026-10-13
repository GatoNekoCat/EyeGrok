#!/bin/sh
# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Pushes a setup file to the headset and restarts the app. Never prints file contents.

set -eu
file="${1:-}"
pkg="io.github.eyegrok.ping"
remote="/sdcard/Android/data/${pkg}/files/eyegrok-setup.json"

if [ -z "$file" ] || [ ! -f "$file" ]; then
  echo "setup file not found" >&2
  exit 1
fi

adb push "$file" "$remote"
adb shell am force-stop "$pkg"
adb shell monkey -p "$pkg" 1

i=0
while [ "$i" -lt 20 ]; do
  if ! adb shell ls "$remote" >/dev/null 2>&1; then
    echo "IMPORTED"
    echo "Delete the local setup file."
    exit 0
  fi
  i=$((i + 1))
  sleep 1
done

echo "NOT_IMPORTED"
echo "Delete the local setup file."
exit 1
