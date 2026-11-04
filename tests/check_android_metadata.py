from pathlib import Path

root = Path(__file__).resolve().parents[1]
activity = (root / 'src/Hibike.Android/MainActivity.cs').read_text(encoding='utf-8')
reader = (root / 'src/Hibike.Android/AndroidMetadata.cs').read_text(encoding='utf-8')
controller = (root / 'src/Hibike.App/MusicController.cs').read_text(encoding='utf-8')
view = (root / 'src/Hibike.App/Views/MainView.cs').read_text(encoding='utf-8')
assert activity.index('AppServices.ReadLocalMetadata =') < activity.index('base.OnCreate(savedInstanceState)')
assert 'AppServices.ReadLocalTrack(original,' in controller
assert 'AppServices.ReadLocalTrack(track,' in controller
assert 'AppServices.ReadLocalTrack(new Track(' in view
for field in ('Title', 'Artist', 'Album', 'CdTrackNumber', 'Date', 'Duration'):
    assert f'MetadataKey.{field}' in reader
assert 'GetEmbeddedPicture()' in reader and 'MediaExtractor' in reader
assert 'if (!track.IsLocal' in reader and 'if (!Path.IsPathFullyQualified(path)' in reader
assert 'LocalTrackMetadata.ReadAudioSource(source)' in reader
player = (root / 'src/Hibike.Android/AndroidAudioPlayer.cs').read_text(encoding='utf-8')
assert 'public AudioSource? PlaybackSource' in player and 'AndroidMetadata.ReadAudioSource(source)' in player
print('Android metadata wiring checks passed; native device format coverage still requires physical testing.')
