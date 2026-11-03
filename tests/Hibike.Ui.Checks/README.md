# UI check (Windows)

Run `dotnet run --project tests/Hibike.Ui.Checks/Hibike.Ui.Checks.csproj` from repository root.

Run the focused queue/radio regressions with `dotnet run --project tests/Hibike.Ui.Checks -- --queue-only`.
Add `--headless` on machines without a display; Skia still renders the layout screenshots.
The focused checks cover retry after provider/fallback failure, manual refresh with upcoming
tracks, preservation on empty/failed refreshes, cancelled/stale requests, station switching,
manual queue priority, reordering during audio resolution, session restore, and radio controls
at 360/390/430/1260 widths. Providers and audio are faked; no live radio service is required.

Uses native Avalonia desktop rendering and fake catalog/player. Checks 360/390/430 widths,
minimum text size, visible button height, search clear state, virtualization with 1,000 tracks,
and opening/closing player. Writes `ui-check-result.txt` beside executable. No network or real audio.

Android build: run `python scripts/prepare_android_runtime.py`, then
`dotnet build src/Hibike.Android/Hibike.Android.csproj`.

Still requires device review: Android gesture/three-button insets, IME Search action,
artwork cropping, text contrast and final visual comparison against OrbisExtract.
List uses separate layout rows for header, mini player and navigation, with an opacity-only
edge fade. No surface gradients. Album display falls back to artist because Track has no album.
Lyrics, timer and volume cards were not invented without existing controls/data.
Project retains existing .NET 10 targets and Android workload.

## Layout references

- [OrbisExtract MainView and UiTheme](https://github.com/flugeru/OrbisExtract): palette, cards, page column and dialog composition.
- [Avalonia ControlCatalog ListBox](https://github.com/AvaloniaUI/Avalonia/blob/master/samples/ControlCatalog/Pages/ListBoxPage.xaml): constrained list viewport and virtualized items.
- [CherylUI mobile tab composition](https://github.com/kikipoulet/CherylUI/blob/master/CherylUI/Styles/TabControlStyles.axaml): fixed bottom actions separated from page content. No CherylUI dependency or copied theme.

Native check writes search, mini player, player and action-dialog PNGs at 360/390/430/1260 widths
beside executable for visual inspection. Fake artwork is deliberately network-free.

## Android UI performance investigation

Run `dotnet run --project tests/Hibike.Ui.Checks -c Release -- --performance-study --headless`.
This offline diagnostic uses synthetic data at 390×844 DIP and writes `android-ui-study.json`
beside the executable. It measures managed allocations and synchronous call duration, counts
realized controls, and checks bitmap reuse with a local asset. It does not measure Android FPS,
GPU work, native bitmap allocation size, network behavior, or device storage latency.
The numbers are observations, not machine-dependent pass/fail thresholds.

See [the investigation](../../docs/performance/android-ui-performance.md) and its recorded baseline.
