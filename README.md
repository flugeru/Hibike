# Hibiki 響

**Sua música. Seu ritmo. • by Flugel**

Player de música em **C# / .NET 10 + Avalonia UI 12**, com busca e extração de áudio via **YT-DLP**. O **Windows tem uma interface desktop própria** inspirada na organização visual do OrbisExtract (navegação lateral, área central, painel de reprodução e player inferior permanente); o **Android mantém a interface móvel dedicada**. Ambos usam o mesmo núcleo de reprodução e catálogo.

## Português

### Primeira versão — 0.1.0

- Tema escuro com roxo, ícones vetoriais e tipografia Inter.
- Layout compacto: cabeçalho e busca, lista de faixas sem cartões grandes e navegação inferior no celular; barra lateral no desktop.
- Miniplayer aparece ao selecionar uma faixa. Toque nele para abrir a tela dedicada de reprodução, com transições suaves, capa, progresso e controles completos. Opções de favoritos/fila ficam no menu de cada faixa.
- Busca por música/artista ou link, resultados reais e capas carregadas sob demanda.
- Streaming de áudio com Media3/ExoPlayer no Android, buffer inicial configurado em 250 ms e alvo de buffer de 10 s. Não baixa a faixa inteira antes de tocar.
- Tentativa inicial com o cliente oficial do YT-DLP que dispensa JavaScript, com fallback para os clientes padrão. Cache em memória de links por 4 minutos e resolução antecipada dos primeiros resultados/próxima faixa; não é cache de arquivos de áudio.
- Play/pause, anterior/próxima, progresso e busca na faixa.
- Serviço de reprodução em segundo plano, MediaSession, notificação nativa com capa/metadados e controles de mídia na tela bloqueada.
- Fila, reprodução aleatória e repetição da faixa atual.
- Favoritos persistidos localmente; URLs de streaming são resolvidas novamente ao reproduzir, sem salvar links temporários.
- Importação de áudio local. O arquivo é copiado para o armazenamento privado do app para continuar acessível no Android.
- Buscas canceláveis, limites separados para extração/conexão e diagnóstico da última etapa, com cópia manual.
- Cache privado do extrator e importação opcional de cookies Netscape nas configurações. A sessão permanece apenas no armazenamento privado do app e pode ser removida pelo mesmo painel.
- Android: Python 3.12, QuickJS, YT-DLP e EJS incluídos no APK; não requer Termux ou servidor externo.

### Baixar o APK

Abra **[Actions → Hibiki • Android APK](https://github.com/flugeru/Hibiki/actions/workflows/android.yml)**, selecione uma execução concluída e baixe **Hibiki-Android-arm64** em **Artifacts**. Descompacte e instale `Hibiki-Android-arm64.apk`.

- Android 7.0 / API 24 ou superior; alvo API 36.
- O workflow publica **apenas Android ARM64** nesta etapa, incluindo o Poco X6 Pro.
- O projeto também tem preparação de runtime x86_64 para desenvolvimento futuro em emulador.
- A chave de desenvolvimento é gerada no runner e preservada no cache do Actions, sem enviar chaves ao repositório e sem precisar de segredos pessoais. Enquanto o cache existir, a assinatura será a mesma entre builds; se ele expirar ou for apagado, será necessário desinstalar o APK antigo (perdendo seus dados locais) antes de instalar um build com nova assinatura. Ela **não é uma identidade de produção/Play Store**.
- O versionCode cresce pelo número da execução do Actions para permitir atualizações.

### Interface desktop redesenhada

A interface Windows foi reconstruída separadamente da tela móvel: barra lateral de navegação, busca com botão próprio, resultados com capas e ações para favoritos/fila, inspetor do que está tocando e player persistente na parte inferior. O tema usa preto profundo com superfícies elevadas discretas, contornos e o acento lilás. Em janelas mais estreitas, o inspetor lateral se recolhe para preservar o conteúdo. Configurações, importação de cookies e diagnóstico permanecem disponíveis.

A CI valida a renderização e interações básicas em 1000 e 1360 pixels e publica as imagens no artefato `Hibiki-Windows-ui-previews`. Reprodução real e acesso ao YouTube devem ser validados em um computador.

### Windows desktop (x64)

O [workflow Hibiki • Windows Desktop](https://github.com/flugeru/Hibiki/actions/workflows/windows.yml) publica o artefato **Hibiki-Windows-x64** em **Artifacts**. Baixe e extraia o ZIP **uma única vez** para uma pasta, mantendo todos os arquivos e a subpasta `tools`, e execute `Hibiki.Desktop.exe`. O pacote portátil inclui o runtime .NET 10, LibVLC e seus plugins, **yt-dlp.exe (2026.10.26), Deno (2.9.8) e o EJS distribuído pelo yt-dlp**. Não é necessário instalar manualmente o extrator, o Deno ou configurar o PATH para buscar músicas.

As versões de yt-dlp e Deno são fixadas e os downloads oficiais são verificados por SHA-256 antes de incluir os executáveis no pacote. A variável `HIBIKI_YTDLP` continua disponível para quem deseja usar uma versão personalizada do extrator.

O pacote não possui assinatura de código, instalador ou atualizador automático. O Actions verifica o executável, LibVLC e as ferramentas incluídas, mas a reprodução real de conteúdo online ainda depende de acesso à rede/YouTube e precisa ser verificada manualmente no Windows.

### Escopo desta base

O serviço Android mantém a reprodução ao minimizar o app. A notificação e os controles da tela bloqueada usam a integração nativa Media3/MediaSession. Ainda não há downloads offline, login integrado no navegador, playlists persistentes ou sincronização. Os favoritos são locais. O acesso ao YouTube depende das restrições do serviço e pode exigir futuras atualizações do extrator; conteúdo privado/restrito não é garantido. Se o YouTube pedir verificação de sessão, é possível importar um arquivo de cookies Netscape já obtido pelo usuário; isso não garante acesso quando o próprio serviço restringe o conteúdo ou a rede.

A biblioteca pode conter arquivos locais importados; removê-los dos favoritos não apaga a cópia de áudio. Ela será removida ao limpar os dados/desinstalar o app. Não há conta nem telemetria do Hibiki. Buscas, streams e capas contatam os provedores do conteúdo.

### Plataformas e arquitetura

| Projeto | Responsabilidade |
|---|---|
| `Hibiki.Core` | Catálogo YT-DLP, modelos, contratos e armazenamento |
| `Hibiki.App` | Interface Avalonia compartilhada, tema e estado de reprodução |
| `Hibiki.Android` | Host Android, runtime embarcado, Media3/ExoPlayer e serviço MediaSession |
| `Hibiki.Desktop` | Host Windows/Linux e áudio LibVLC |
| `Hibiki.Core.Tests` | Parser, tratamento de parâmetros, headers, falhas e favoritos |

No desktop, a base usa **yt-dlp no PATH** (ou `HIBIKI_YTDLP` com o caminho do executável), além de um runtime JavaScript compatível com YT-DLP. O pacote nativo LibVLC do Windows está referenciado; no Linux é necessário instalar LibVLC e os plugins de áudio da distribuição. **O Windows x64 agora possui workflow e pacote portátil próprios; a execução real no desktop ainda requer validação manual. O Linux ainda não possui workflow de publicação.**

**Compilações e testes do núcleo .NET são executados pelo GitHub Actions.** O workflow instala o workload Android e o SDK, verifica os runtimes contra versões/hashes fixos, executa os testes do núcleo, publica o APK ARM64 assinado e valida sua assinatura e conteúdo. **O emulador foi removido do Actions para acelerar as builds; a interface e a reprodução são testadas manualmente no Android.**

Sem compilar localmente, a estrutura pode ser verificada com `python3 scripts/check_source.py`.

## English

Hibiki is a music player by **Flugel**, built with **C# / .NET 10 and Avalonia UI 12**, using **YT-DLP** for search and audio extraction. One adaptive interface serves Android, Windows and Linux.

The compact interface includes search/URL input, real results and artwork, a conditional miniplayer and an expanded player sheet. Playback includes seeking, a queue, shuffle/repeat, persistent favorites and local audio import. Per-track menus keep secondary actions out of the main list. Private extractor caching, optional Netscape session cookie import and redacted playback diagnostics are available in settings. The Android APK bundles Python, QuickJS, YT-DLP and EJS. No Termux or external backend is required.

The Windows interface is now a **dedicated desktop shell**, inspired by OrbisExtract's spacing, cards, and restrained dark palette, with side navigation, a separate results workspace, a now-playing inspector, and a persistent bottom player. The Android screen remains independent and shares the underlying music controller. The CI runs headless UI checks at 1000px and 1360px and publishes desktop screenshots in `Hibiki-Windows-ui-previews`.

**GitHub Actions builds Android ARM64 and Windows x64.** Download the installable APK from the `Hibiki-Android-arm64` workflow artifact. Android 7.0/API 24 or later is required. An Actions-generated development key is cached between builds without committing private keys or requiring personal signing secrets. If that cache expires or is removed, the signature changes and the old APK must be uninstalled before installing the new one, which removes local app data. This is not a production or Play Store signing identity.

Android playback runs in a Media3/ExoPlayer foreground service with a MediaSession, system media notification and lockscreen controls. The initial buffer target is 250 ms, with a 10-second buffer target; URL extraction/network latency can still delay cold starts. Resolved addresses are cached in memory for four minutes and prefetched without downloading media files. The player uses a dedicated animated page. Offline downloading, browser-based account login, persistent playlists and sync are not implemented. Importing an existing session does not guarantee access when YouTube restricts the content or network. The [Windows Desktop workflow](https://github.com/flugeru/Hibiki/actions/workflows/windows.yml) publishes a **Hibiki-Windows-x64** artifact. Download and extract it **once**, preserving the `tools` subfolder, then run `Hibiki.Desktop.exe`. The portable package includes the .NET 10 runtime, native LibVLC, **yt-dlp.exe 2026.10.26**, **Deno 2.9.8**, and the EJS scripts bundled with yt-dlp. No separate yt-dlp/Deno installation or PATH configuration is needed. Official downloads are SHA-256 verified in the workflow. The `HIBIKI_YTDLP` override remains available. The ZIP is unsigned, with no installer or auto-updater. Real streaming still depends on internet/YouTube access and requires manual validation. Linux still needs system LibVLC/audio plugins and does not yet have a release workflow.

.NET builds and core tests run in **GitHub Actions**, not locally. The workflow publishes and verifies a signed ARM64 APK; emulator testing has been removed to reduce build time, so playback and UI must be checked on a real Android device. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for upstream components and source/build references.
