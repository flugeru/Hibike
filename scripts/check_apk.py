#!/usr/bin/env python3
"""Reject APKs missing the native executables or embedded extraction runtime."""
import sys
import zipfile
with zipfile.ZipFile(sys.argv[1]) as apk:
    names = set(apk.namelist())
    for expected in ("lib/arm64-v8a/libpython.so", "lib/arm64-v8a/libqjs.so",
                     "assets/runtime/arm64-v8a/python.zip", "assets/runtime/yt-dlp"):
        if expected not in names:
            raise SystemExit("Missing APK runtime file: " + expected)
print("APK contains Python, QuickJS and yt-dlp.")
