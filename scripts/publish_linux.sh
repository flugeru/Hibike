#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
appdir="$root/artifacts/Hibike-linux-x64.AppDir"
if [[ -e "$appdir" ]]; then
    echo "AppDir already exists; use a fresh build directory: $appdir" >&2
    exit 1
fi
mkdir -p "$appdir/usr/bin" "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/256x256/apps"
dotnet publish src/Hibike.Desktop/Hibike.Desktop.csproj -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=false -o "$appdir/usr/bin"

# Use the pinned Python zipapp instead of a glibc-dependent standalone extractor.
python3 - "$appdir/usr/bin" <<'PY'
import hashlib, json, pathlib, sys, urllib.request
output = pathlib.Path(sys.argv[1])
version = json.loads(pathlib.Path('scripts/runtime-lock.json').read_text())['yt_dlp']['version']
base = f'https://github.com/yt-dlp/yt-dlp/releases/download/{version}'
checksums = urllib.request.urlopen(base + '/SHA2-256SUMS', timeout=60).read().decode()
expected = next(line.split()[0] for line in checksums.splitlines() if line.split()[-1].lstrip('*') == 'yt-dlp')
data = urllib.request.urlopen(base + '/yt-dlp', timeout=120).read()
assert hashlib.sha256(data).hexdigest() == expected, 'yt-dlp checksum mismatch'
(output / 'yt-dlp').write_bytes(data)
(output / 'yt-dlp').chmod(0o755)
PY
cp LICENSE THIRD-PARTY-NOTICES.md "$appdir/usr/bin/"
cp packaging/linux/hibike.desktop "$appdir/"
cp packaging/linux/hibike.desktop "$appdir/usr/share/applications/"
cp site/Assets/hibike.png "$appdir/hibike.png"
cp site/Assets/hibike.png "$appdir/usr/share/icons/hicolor/256x256/apps/hibike.png"
cat > "$appdir/AppRun" <<'SH'
#!/bin/sh
set -eu
HERE="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
export PATH="$HERE/usr/bin:$PATH"
exec "$HERE/usr/bin/Hibike.Desktop" "$@"
SH
chmod +x "$appdir/AppRun" "$appdir/usr/bin/Hibike.Desktop"
"$appdir/usr/bin/yt-dlp" --version
desktop-file-validate "$appdir/hibike.desktop"

tool="$root/artifacts/appimagetool-x86_64.AppImage"
curl --fail --location --retry 3 \
    https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage -o "$tool"
echo "62aebc77c2149e96b593b94da9bbd1881e0984a56399ff6d7a03b7ddd7c9ce68  $tool" | sha256sum --check
chmod +x "$tool"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" "$appdir" "$root/artifacts/Hibike-x86_64.AppImage"
test -s "$root/artifacts/Hibike-x86_64.AppImage"
