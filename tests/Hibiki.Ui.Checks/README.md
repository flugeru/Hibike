# UI check (Windows)

Run `dotnet run --project tests/Hibiki.Ui.Checks/Hibiki.Ui.Checks.csproj` from repository root.

Uses native Avalonia desktop rendering and fake catalog/player. Checks 360/390/430 widths,
minimum text size, visible button height, search clear state, virtualization with 1,000 tracks,
and opening/closing player. Writes `ui-check-result.txt` beside executable. No network or real audio.

Android build: run `python scripts/prepare_android_runtime.py`, then
`dotnet build src/Hibiki.Android/Hibiki.Android.csproj`.

Still requires device review: Android gesture/three-button insets, IME Search action,
artwork cropping, text contrast and final visual comparison against OrbisExtract.
List uses separate layout rows for header, mini player and navigation, not an overlapping
fade; no gradients were introduced. Album display falls back to artist because Track has no album.
Lyrics, timer and volume cards were not invented without existing controls/data.
Project retains existing .NET 10 targets and Android workload.
