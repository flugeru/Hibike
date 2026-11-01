# Hibiki 響

**Sua música. Seu ritmo. • by Flugel**

Player de música em **C# / .NET 10 + Avalonia UI 12**, com busca e extração de áudio via **YT-DLP**. Interface compartilhada entre **Android, Windows e Linux**, seguindo a organização do OrbisExtract, com identidade visual própria.

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

No desktop, a base usa **yt-dlp no PATH** (ou `HIBIKI_YTDLP` com o caminho do executável), além de um runtime JavaScript compatível com YT-DLP. O pacote nativo LibVLC do Windows está referenciado; no Linux é necessário instalar LibVLC e os plugins de áudio da distribuição. **Os hosts desktop ainda não têm workflow de compilação nem validação de execução nesta primeira etapa.**

**Compilações e testes do núcleo .NET são executados pelo GitHub Actions.** O workflow instala o workload Android e o SDK, verifica os runtimes contra versões/hashes fixos, executa os testes do núcleo, publica o APK ARM64 assinado e valida sua assinatura e conteúdo. **O emulador foi removido do Actions para acelerar as builds; a interface e a reprodução são testadas manualmente no Android.**

Sem compilar localmente, a estrutura pode ser verificada com `python3 scripts/check_source.py`.

## English

Hibiki is a music player by **Flugel**, built with **C# / .NET 10 and Avalonia UI 12**, using **YT-DLP** for search and audio extraction. One adaptive interface serves Android, Windows and Linux.

The compact interface includes search/URL input, real results and artwork, a conditional miniplayer and an expanded player sheet. Playback includes seeking, a queue, shuffle/repeat, persistent favorites and local audio import. Per-track menus keep secondary actions out of the main list. Private extractor caching, optional Netscape session cookie import and redacted playback diagnostics are available in settings. The Android APK bundles Python, QuickJS, YT-DLP and EJS. No Termux or external backend is required.

**Only Android ARM64 is built by GitHub Actions for now.** Download the installable APK from the `Hibiki-Android-arm64` workflow artifact. Android 7.0/API 24 or later is required. An Actions-generated development key is cached between builds without committing private keys or requiring personal signing secrets. If that cache expires or is removed, the signature changes and the old APK must be uninstalled before installing the new one, which removes local app data. This is not a production or Play Store signing identity.

Android playback runs in a Media3/ExoPlayer foreground service with a MediaSession, system media notification and lockscreen controls. The initial buffer target is 250 ms, with a 10-second buffer target; URL extraction/network latency can still delay cold starts. Resolved addresses are cached in memory for four minutes and prefetched without downloading media files. The player uses a dedicated animated page. Offline downloading, browser-based account login, persistent playlists and sync are not implemented. Importing an existing session does not guarantee access when YouTube restricts the content or network. Desktop hosts are scaffolded with LibVLC but have not yet been built or runtime-tested by a desktop workflow. Desktop also requires yt-dlp and a supported JavaScript runtime; Linux requires system LibVLC/audio plugins.

.NET builds and core tests run in **GitHub Actions**, not locally. The workflow publishes and verifies a signed ARM64 APK; emulator testing has been removed to reduce build time, so playback and UI must be checked on a real Android device. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for upstream components and source/build references.
