#!/usr/bin/env bash
set -euo pipefail

# Fresh, dedicated directory: never reuse sources or dependency build caches.
root="$PWD/artifacts/libmpv-own"
mkdir -p "$(dirname "$root")"
mkdir "$root"
mkdir "$root/records" "$root/dist" "$root/source-release"
exec > >(tee "$root/records/build.log") 2>&1
pacman -Q > "$root/records/host-packages.txt"
git clone https://github.com/shinchiro/mpv-winbuild-cmake.git "$root/recipes"
git -C "$root/recipes" checkout --detach bda10a2816ce9908d419b3f4b564227330457e76
curl --fail --location https://raw.githubusercontent.com/zhongfly/mpv-winbuild/37b83557ec1cb08a241defc77f60cb151f7b314c/compile-lgpl-libmpv.patch -o "$root/records/lgpl.patch"
git -C "$root/recipes" apply --check "$root/records/lgpl.patch"
git -C "$root/recipes" apply "$root/records/lgpl.patch"

# Keep patched sources and generated build configuration: upstream cleanup resets both.
python - "$root/recipes" <<'PY'
from pathlib import Path
import sys
root = Path(sys.argv[1])
steps = root / 'cmake/custom_steps.cmake'
text = steps.read_text()
assert text.count('function(cleanup _name _last_step)') == 1
steps.write_text(text.replace('function(cleanup _name _last_step)',
                              'function(cleanup _name _last_step)\n    return()'))
mpv = root / 'packages/mpv.cmake'
text = mpv.read_text()
needle = 'GIT_REPOSITORY https://github.com/mpv-player/mpv.git'
assert text.count(needle) == 1
assert '-Dgpl=false' in text and '-Dcplayer=false' in text
mpv.write_text(text.replace(needle, needle + '\n    GIT_TAG 46787be2502556586b6a5fea9ebd595f5cd9b713'))
ffmpeg = (root / 'packages/ffmpeg.cmake').read_text()
assert '--enable-gpl' not in ffmpeg and '--enable-nonfree' not in ffmpeg
PY
cp "$0" "$root/records/build_libmpv.sh"
cp -R "$root/recipes" "$root/records/recipes-before-build"
git -C "$root/recipes" diff --binary > "$root/records/recipe-changes.patch"
cmake -S "$root/recipes" -B "$root/build" -G Ninja \
  -DTARGET_ARCH=x86_64-w64-mingw32 -DGCC_ARCH=x86-64 \
  -DCOMPILER_TOOLCHAIN=gcc -DSINGLE_SOURCE_LOCATION="$root/sources" \
  -DRUSTUP_LOCATION="$root/rust" -DMAKEJOBS=4
cmake --build "$root/build" --target gcc --parallel 2
cmake --build "$root/build" --target mpv --parallel 2

python - "$root" <<'PY'
from pathlib import Path
import shutil, subprocess, sys
root = Path(sys.argv[1])
dlls = list((root / 'build').glob('mpv-dev-lgpl-*/libmpv-2.dll'))
assert len(dlls) == 1, dlls
configs = list((root / 'build').rglob('ffmpeg-configure-out.log'))
assert len(configs) == 1
assert 'License: LGPL version 3 or later' in configs[0].read_text()
shutil.copytree(dlls[0].parent, root / 'dist', dirs_exist_ok=True)
mpv = root / 'sources/mpv'
for name in ('LICENSE.LGPL', 'Copyright'):
    shutil.copy(mpv / name, root / 'dist' / name)
with (root / 'records/source-revisions.txt').open('w') as output:
    for repo in sorted((root / 'sources').iterdir()):
        if (repo / '.git').exists():
            revision = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip()
            output.write(f'{repo.name} {revision}\n')
shutil.copytree(root / 'records', root / 'dist/build-records', dirs_exist_ok=True)
PY

# Preserve complete source/build trees, not just upstream links. Includes patched
# dependencies, toolchain sources, Cargo registry/git sources and generated config.
# Exclude credentials and compiler caches; never archive runner HOME wholesale.
export XZ_OPT=-1
tar --exclude=records/build.log --exclude=ccache --exclude=.git/credentials \
  -C "$root" -cJf - recipes sources build rust records \
  | split -b 1900M - "$root/source-release/Hibike-libmpv-source.tar.xz.part-"
cp "$root/records/source-revisions.txt" "$root/source-release/"
printf '%s\n' 'Reassemble: cat Hibike-libmpv-source.tar.xz.part-* > source.tar.xz' \
  'Extract: tar -xJf source.tar.xz' \
  'Build recipe and modifications: records/build_libmpv.sh and records/recipe-changes.patch.' \
  'Sources include preserved build directories; original absolute paths appear in generated configuration.' \
  'See individual source trees for dependency licenses and copyright notices.' \
  > "$root/source-release/README.txt"
(cd "$root/source-release" && sha256sum Hibike-libmpv-source.tar.xz.part-* > SHA256SUMS.txt)
(cd "$root/dist" && sha256sum libmpv-2.dll > SHA256SUMS.txt)
