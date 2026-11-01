#!/usr/bin/env python3
"""Download pinned upstream runtime files; verify every byte before packaging."""
import hashlib
import json
import pathlib
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
lock = json.loads((ROOT / "scripts/runtime-lock.json").read_text())
android = ROOT / "src/Hibiki.Android"

def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Hibiki-build/0.1"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()

for item in lock["files"]:
    path = item["path"]
    abi, name = path.split("/")[-2:]
    data = fetch(f"https://raw.githubusercontent.com/yausername/youtubedl-android/{lock['runtime_commit']}/{path}")
    blob = hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()
    if blob != item["git_blob"]:
        raise SystemExit(f"Upstream integrity check failed: {path}")
    if name == "libpython.zip.so":
        target = android / f"Assets/runtime/{abi}/python.zip"
    else:
        target = android / f"Native/{abi}/{name}"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    print(f"Verified {abi}/{name}")

item = lock["yt_dlp"]
data = fetch(item["url"])
if hashlib.sha256(data).hexdigest() != item["sha256"]:
    raise SystemExit("yt-dlp SHA-256 mismatch")
target = android / "Assets/runtime/yt-dlp"
target.parent.mkdir(parents=True, exist_ok=True)
target.write_bytes(data)
print(f"Verified yt-dlp {item['version']}")
