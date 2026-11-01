#!/usr/bin/env python3
"""Fast source checks. Does not restore dependencies or compile the app."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
for glob in ("*.csproj", "*.props", "*.axaml", "*.xml", "*.slnx"):
    for path in root.rglob(glob):
        if not {"obj", "bin"}.intersection(path.parts):
            ET.parse(path)
json.loads((root / "global.json").read_text())
lock = json.loads((root / "scripts/runtime-lock.json").read_text())
assert len(lock["runtime_commit"]) == 40
assert len(lock["yt_dlp"]["sha256"]) == 64
print("Project XML, XAML, manifests and runtime lock verified.")
