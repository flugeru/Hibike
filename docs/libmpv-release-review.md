# libmpv distribution review — 0.2.0

Status: replacement passed local playback and live yt-dlp streaming checks with TLS verification enabled and the pinned Mozilla CA bundle. libmpv remains default; VLC is the only alternative. Previous Windows binaries below must not be used for the new release.

## Replacement for public packaging

Use https://github.com/freeKC/libmpv-builds/releases/tag/20261021-1. Binary archive SHA-256 `b3d1a4b3a8e202c28369a46f5fb776baa0742e38de8c9d0757b0255bcb8223d1` and source archive SHA-256 `77e612c8110b1731c9b180cda840bb965939440c743d480f34cab957861e9d11` verified locally. Source archive contains `recipe/`, `recipe-files/`, `libmpv-builds/windows/sources.lock` and `src_packages/` including mpv and FFmpeg. Binary archive includes dependency licenses and `BUILDINFO.txt` identifying pinned dependency revisions, compiler, recipe and container.

Packaging retains these notices and headers, permits replacing the separate DLL, and uploads the corresponding source archive alongside the application downloads. This replaces the former source-link-only approach; it is not a blanket legal certification. The experimental source-build workflow is manual only and is not needed for releases.

## Identified build

- Archive: `mpv-dev-lgpl-x86_64-20261103-git-46787be250.7z`
- SHA-256: `ce2563d352aac81c2e6e9b2b63e846ec847f4bdabdec74fb14c5b29049c161d7`
- mpv revision: `46787be2502556586b6a5fea9ebd595f5cd9b713`
- Publisher workflow revision: `37b83557ec1cb08a241defc77f60cb151f7b314c`
- Build: https://github.com/zhongfly/mpv-winbuild/actions/runs/41669743475
- LGPL x64 job: https://github.com/zhongfly/mpv-winbuild/actions/runs/41669743475/job/126034743845
- Publisher: https://github.com/zhongfly/mpv-winbuild

## Verified evidence

The publisher describes this archive as LGPL-2.1+ libmpv with statically linked LGPLv3 FFmpeg, while disclaiming a guarantee that every incompatible dependency is disabled. The job's `64-lgpl_logs` artifact contains `ffmpeg-configure-out.log`, which reports static linking and `License: LGPL version 3 or later`.

The pinned workflow checks out `shinchiro/mpv-winbuild-cmake`, restores source/build caches, applies patches from the publisher repository and `compile-lgpl-libmpv.patch`, and invokes `build.sh`. Therefore the mpv revision alone does not identify the complete corresponding source tree. Unpinned repository links and current dependency versions cannot substitute for the sources used in this build.

## Remaining work

Obtain the complete corresponding source for this specific binary: dependency revisions and source archives, applied patches, build recipes and relevant configuration, including the material needed to rebuild the statically linked components. Preserve these alongside the release with checksums and applicable notices. Review replacement/relinking requirements and bundled dependency licenses against that material before publication.

The publisher retains release builds for 30 days. A temporary upstream link alone is not a durable source distribution plan. No statement in this document certifies legal compliance.
