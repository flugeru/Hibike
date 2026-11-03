#!/usr/bin/env python3
"""Verify the ARM64 APK contains only the intended native and runtime files."""
from pathlib import Path
import sys
import zipfile
with zipfile.ZipFile(sys.argv[1]) as apk:
    names = set(apk.namelist())
    for expected in ("lib/arm64-v8a/libpython.so", "lib/arm64-v8a/libqjs.so",
                     "assets/runtime/arm64-v8a/python.zip", "assets/runtime/yt-dlp"):
        if expected not in names:
            raise SystemExit("Missing APK runtime file: " + expected)
    architectures = {name.split("/")[1] for name in names if name.startswith("lib/") and name.endswith(".so")}
    if architectures != {"arm64-v8a"}:
        raise SystemExit("Unexpected native architectures in ARM64 APK: " + ", ".join(sorted(architectures)))
    if any(name.startswith("assets/runtime/x86_64/") for name in names):
        raise SystemExit("ARM64 APK contains the x86_64 Python runtime.")
print(f"ARM64 APK contains Python, QuickJS and yt-dlp. APK size: {Path(sys.argv[1]).stat().st_size:,} bytes.")
