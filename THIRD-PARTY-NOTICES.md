# Third-party components

Hibiki source is distributed under the repository's MIT license. Separately executed upstream tools retain their own licenses.

- **Avalonia UI / Inter:** MIT / SIL Open Font License; [Avalonia](https://github.com/AvaloniaUI/Avalonia), [Inter](https://github.com/rsms/inter).
- **YT-DLP 2026.10.26:** Unlicense, including bundled third-party components; [source and license](https://github.com/yt-dlp/yt-dlp/tree/2026.10.26), [third-party licenses](https://github.com/yt-dlp/yt-dlp/blob/2026.10.26/THIRD_PARTY_LICENSES.txt). The release zipapp includes yt-dlp-ejs and its JavaScript component licenses.
- **Android Python/QuickJS distribution:** binaries from [yausername/youtubedl-android](https://github.com/yausername/youtubedl-android/tree/d725d5c9a18c3a99a13ee0308bf78275dc310760), commit pinned in `scripts/runtime-lock.json`. The upstream project is GPL-3.0; its license is reproduced in `scripts/LICENSE.youtubedl-android.txt`. Hibiki does not link its Java/Kotlin wrapper; it launches the supplied standalone Python and QuickJS executables. Python and native dependency source/build references are in the upstream [BUILD_PYTHON.md](https://github.com/yausername/youtubedl-android/blob/d725d5c9a18c3a99a13ee0308bf78275dc310760/BUILD_PYTHON.md) and [README](https://github.com/yausername/youtubedl-android/blob/d725d5c9a18c3a99a13ee0308bf78275dc310760/README.md). Python is PSF-licensed; QuickJS is MIT; dependencies retain their upstream notices.
- **LibVLCSharp / LibVLC:** LGPL-2.1; [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp), [VLC](https://www.videolan.org/vlc/download-sources.html). Used by the desktop host only; not included in the initial Android APK.
- **.NET / Android platform:** respective Microsoft and Android Open Source Project licenses.

The runtime preparation script downloads pinned upstream bytes and checks Git blob identities / SHA-256 before packaging. It does not fetch executable code at app startup. Update the pinned versions and checksums deliberately when updating the extractor or native runtime. This notice and the upstream wrapper GPL license are bundled as Android assets in the APK; bundled Python and YT-DLP distributions also retain their embedded notices.

Hibiki is independent of YouTube/Google. Users are responsible for accessing content they are authorized to use.
