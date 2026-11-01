#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/smoke
apk="$(find artifacts/emulator -maxdepth 1 -name '*-Signed.apk' -print -quit)"
test -n "$apk"
adb install -r "$apk"
adb logcat -c
# Resolve the generated .NET activity name instead of hard-coding its CRC prefix.
activity="$(adb shell cmd package resolve-activity --brief com.flugel.hibiki | tr -d '\r' | tail -n 1)"
adb shell am start -W -n "$activity" | tee artifacts/smoke/launch.txt
for attempt in $(seq 1 20); do
  sleep 1
  if ! adb shell pidof com.flugel.hibiki > /dev/null; then
    adb logcat -d > artifacts/smoke/logcat.txt
    tail -n 160 artifacts/smoke/logcat.txt
    echo 'Hibiki exited during the startup smoke test.' >&2
    exit 1
  fi
done
adb shell dumpsys activity activities > artifacts/smoke/activities.txt
adb logcat -d > artifacts/smoke/logcat.txt
if ! grep -E '(mResumedActivity|topResumedActivity).*com.flugel.hibiki' artifacts/smoke/activities.txt; then
  echo 'Hibiki did not reach the foreground activity.' >&2
  exit 1
fi
adb exec-out screencap -p > artifacts/smoke/startup.png
echo 'Hibiki stayed open in the foreground for 20 seconds.'
