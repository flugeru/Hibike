# Hibiki 響

**Sua música. Seu ritmo. · Your music. Your rhythm. — by Flugel**

## Português

Player de música em **C# / .NET 10 e Avalonia UI 12**, com busca e streaming via **yt-dlp**, áudio local e interface adaptável para Android e desktop.

**Em desenvolvimento. Nenhuma release publicada ainda.**

### Recursos

- Tema AMOLED, tipografia Inter, miniplayer e player adaptável com capa e controles de reprodução.
- Busca por música, artista ou link do YouTube; streaming sem baixar a faixa inteira antes de tocar.
- Fila, favoritos locais, reprodução aleatória, repetição e ajuste de volume.
- Importação de áudio local; leitura de título, artista, álbum, duração e capa embutida em FLAC.
- Painel de informações do áudio e diagnóstico de reprodução.
- Android: reprodução em segundo plano, notificação e controles na tela bloqueada com Media3/ExoPlayer. Python, QuickJS e yt-dlp incluídos no APK; sem Termux.
- Desktop: áudio com LibVLC e Discord Rich Presence com **Listening**, faixa, artista, capa e tempo. Requer Discord desktop aberto; não usa token pessoal. Para FLAC, busca capa online por artista e álbum.

### Testar

**Android 7.0+ / API 24, ARM64:** abra [Actions → Hibiki • Android APK](https://github.com/flugeru/Hibiki/actions/workflows/android.yml), selecione uma execução concluída e baixe `Hibiki-Android-arm64` em **Artifacts**. Extraia e instale APK. São builds de desenvolvimento, não releases.

A assinatura Android depende da chave de desenvolvimento mantida no cache do Actions. Se ela mudar, será necessário desinstalar build anterior, perdendo dados locais.

**Windows:** `scripts/publish_windows.ps1` gera pacote portátil com .NET, LibVLC e yt-dlp. Extraia pasta inteira e abra `Hibiki.Desktop.exe`.

**Linux:** host disponível no código; requer LibVLC, plugins de áudio e yt-dlp. Execução Linux ainda não validada. `HIBIKI_YTDLP` permite indicar executável do yt-dlp no desktop.

### Limites e privacidade

Sem downloads offline, sincronização ou playlists persistentes. Acesso ao YouTube depende das restrições do serviço; configurações permitem importar e remover cookies Netscape existentes.

Áudios importados são copiados para armazenamento privado. Remover favorito não apaga arquivo; limpar dados ou desinstalar apaga cópias. Hibiki não possui conta nem telemetria. Busca, streaming e capas contatam provedores externos; capa de FLAC no Discord consulta iTunes com artista e álbum, sem enviar arquivo. `HIBIKI_DISCORD_RPC=0` desativa Rich Presence e essa consulta no desktop.

## English

Music player built with **C# / .NET 10 and Avalonia UI 12**, featuring **yt-dlp** search and streaming, local audio and an adaptive Android/desktop interface.

**In development. No releases published yet.**

### Features

- AMOLED theme, Inter typography, miniplayer and adaptive player with artwork and playback controls.
- Search by song, artist or YouTube URL; streaming without downloading the entire track first.
- Queue, local favorites, shuffle, repeat and volume control.
- Local audio import; embedded FLAC title, artist, album, duration and artwork support.
- Audio information panel and playback diagnostics.
- Android: Media3/ExoPlayer background playback, media notification and lockscreen controls. Python, QuickJS and yt-dlp bundled in APK; no Termux required.
- Desktop: LibVLC audio and Discord Rich Presence with **Listening**, song, artist, artwork and playback time. Requires Discord desktop; no personal token. Local FLAC artwork is looked up online by artist and album.

### Try it

**Android 7.0+ / API 24, ARM64:** open [Actions → Hibiki • Android APK](https://github.com/flugeru/Hibiki/actions/workflows/android.yml), select a completed run and download `Hibiki-Android-arm64` under **Artifacts**. Extract and install APK. These are development builds, not releases.

Android signing uses a development key cached by Actions. If that key changes, uninstalling the previous build is required, which removes local app data.

**Windows:** `scripts/publish_windows.ps1` builds a portable package with .NET, LibVLC and yt-dlp. Extract the entire folder and run `Hibiki.Desktop.exe`.

**Linux:** desktop host available in source; requires LibVLC, audio plugins and yt-dlp. Linux execution is not yet validated. Set `HIBIKI_YTDLP` to override the desktop yt-dlp executable path.

### Limits and privacy

No offline downloads, synchronization or persistent playlists. YouTube access depends on service restrictions; settings support importing and removing existing Netscape cookies.

Imported audio is copied into private app storage. Removing a favorite does not delete its file; clearing app data or uninstalling removes copies. Hibiki has no account or telemetry. Search, streaming and artwork contact external providers; Discord artwork lookup sends FLAC artist and album to iTunes, not the file. `HIBIKI_DISCORD_RPC=0` disables desktop Rich Presence and its artwork lookup.

## Source

| Project | Purpose |
|---|---|
| `src/Hibiki.Core` | Catalog, metadata, models and storage |
| `src/Hibiki.App` | Shared Avalonia interface and playback state |
| `src/Hibiki.Android` | Android host, bundled runtime and Media3 |
| `src/Hibiki.Desktop` | Desktop host, LibVLC and Discord Rich Presence |
| `tests` | Core tests and native UI/audio checks |

```sh
dotnet test tests/Hibiki.Core.Tests
dotnet build src/Hibiki.Android
dotnet build src/Hibiki.Desktop
```

Android builds require .NET Android workload and Android SDK. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for upstream components.
