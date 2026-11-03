#!/usr/bin/env python3
"""Verify the Android release APK runtime files and R8 output."""
import sys
import zipfile
with zipfile.ZipFile(sys.argv[1]) as apk:
    names = set(apk.namelist())
    for expected in ("lib/arm64-v8a/libpython.so", "lib/arm64-v8a/libqjs.so",
                     "assets/runtime/arm64-v8a/python.zip", "assets/runtime/yt-dlp"):
        if expected not in names:
            raise SystemExit("Missing APK runtime file: " + expected)
    dex_files = [item for item in apk.infolist()
                 if "/" not in item.filename and item.filename.startswith("classes") and item.filename.endswith(".dex")]
    if not any(b"~~R8{" in apk.read(item) for item in dex_files):
        raise SystemExit("APK DEX does not contain an R8 compiler marker.")
    dex_size = sum(item.file_size for item in dex_files)
print("APK contains Python, QuickJS and yt-dlp.")
print(f"R8 verified in DEX. Uncompressed DEX size: {dex_size:,} bytes.")
