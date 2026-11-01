#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/smoke
apk="$(find artifacts/emulator -maxdepth 1 -name '*-Signed.apk' -print -quit)"
test -n "$apk"
adb install -r "$apk"
adb shell pm grant com.flugel.hibiki android.permission.POST_NOTIFICATIONS
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

# Exercise native audio, then the real embedded extractor and remote playback.
adb shell am force-stop com.flugel.hibiki
adb logcat -c
adb shell am start -W -n "$activity" --ez hibiki.smoke.playback true
for attempt in $(seq 1 70); do
  if adb logcat -d -s HibikiProbe:I '*:S' | grep -q HIBIKI_PROBE_DONE; then break; fi
  if adb logcat -d -s HibikiProbe:I '*:S' | grep -Eq 'HIBIKI_PROBE_(LOCAL|RUNTIME)_FAILED'; then break; fi
  sleep 1
done
adb logcat -d > artifacts/smoke/playback-logcat.txt
adb logcat -d -s HibikiProbe:I '*:S' > artifacts/smoke/playback-probe.txt
cat artifacts/smoke/playback-probe.txt
grep -q HIBIKI_PROBE_LOCAL_OK artifacts/smoke/playback-probe.txt
grep -q HIBIKI_PROBE_MEDIA_CONTROLS_OK artifacts/smoke/playback-probe.txt
grep -q HIBIKI_PROBE_BACKGROUND_OK artifacts/smoke/playback-probe.txt
grep -q HIBIKI_PROBE_NOTIFICATION_OK artifacts/smoke/playback-probe.txt
grep -q HIBIKI_PROBE_RUNTIME_OK artifacts/smoke/playback-probe.txt
grep -q HIBIKI_PROBE_DONE artifacts/smoke/playback-probe.txt
grep -q HIBIKI_PROBE_LAYOUT_OK artifacts/smoke/playback-probe.txt
adb shell am start -W -n "$activity"
sleep 1
adb exec-out screencap -p > artifacts/smoke/player.png
adb shell input keyevent 4
sleep 1
adb exec-out screencap -p > artifacts/smoke/results-layout-fixture.png
