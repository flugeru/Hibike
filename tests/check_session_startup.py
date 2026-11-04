from pathlib import Path

root = Path(__file__).resolve().parents[1]
android = (root / 'src/Hibike.Android/MainActivity.cs').read_text(encoding='utf-8')
desktop = (root / 'src/Hibike.Desktop/Program.cs').read_text(encoding='utf-8')
restore = (root / 'src/Hibike.App/Views/MainView.AccountRefresh.cs').read_text(encoding='utf-8')
login = (root / 'src/Hibike.App/Views/MainView.GoogleWelcome.cs').read_text(encoding='utf-8')
assert android.index('WebSessionStore.Configure(this)') < android.index('base.OnCreate(savedInstanceState)')
assert desktop.index('WebSessionStore.Configure()') < desktop.index('AppBuilder.Configure')
assert restore.index('AppServices.RestoreYouTubeAccount is not { } restore') < restore.index('_accountRestoreAttempted = true')
assert 'AppServices.SaveYouTubeAccount?.Invoke' not in login
print('Session startup checks passed: storage ready before UI, missing hook retryable, login requires persistence.')
