# Hibiki 響

**Sua música. Seu ritmo. • by Flugel**

Player de música em **C# / .NET 10 + Avalonia UI 12**, com busca e extração de áudio via **YT-DLP**. Interface compartilhada entre **Android, Windows e Linux**, seguindo a organização do OrbisExtract, com identidade visual própria.

## Português

### Primeira versão — 0.1.0

- Tema escuro com roxo, ícones vetoriais e tipografia Inter.
- Layout adaptável: navegação inferior no celular, barra lateral no desktop e painel de fila em janelas maiores.
- Busca por música/artista ou link, resultados reais e capas carregadas sob demanda.
- Streaming de áudio, play/pause, anterior/próxima, progresso e busca na faixa.
- Fila, reprodução aleatória e repetição da faixa atual.
- Favoritos persistidos localmente; URLs de streaming são resolvidas novamente ao reproduzir, sem salvar links temporários.
- Importação de áudio local. O arquivo é copiado para o armazenamento privado do app para continuar acessível no Android.
- Buscas canceláveis, tempo limite e mensagens de erro visíveis.
- Android: Python 3.12, QuickJS, YT-DLP e EJS incluídos no APK; não requer Termux ou servidor externo.

### Baixar o APK

Abra **[Actions → Hibiki • Android APK](https://github.com/flugeru/Hibiki/actions/workflows/android.yml)**, selecione uma execução concluída e baixe **Hibiki-Android-arm64** em **Artifacts**. Descompacte e instale `Hibiki-Android-arm64.apk`.

- Android 7.0 / API 24 ou superior; alvo API 36.
- O workflow publica **apenas Android ARM64** nesta etapa, incluindo o Poco X6 Pro.
- O projeto também tem preparação de runtime x86_64 para desenvolvimento futuro em emulador.
- A chave de desenvolvimento é gerada no runner e preservada no cache do Actions, sem enviar chaves ao repositório e sem precisar de segredos pessoais. Enquanto o cache existir, a assinatura será a mesma entre builds; se ele expirar ou for apagado, será necessário desinstalar o APK antigo (perdendo seus dados locais) antes de instalar um build com nova assinatura. Ela **não é uma identidade de produção/Play Store**.
- O versionCode cresce pelo número da execução do Actions para permitir atualizações.

### Escopo desta base

Reprodução **com o app aberto**. Ao ir para segundo plano, o áudio pausa e a preparação pendente é cancelada. Ainda não há serviço em segundo plano, controles na notificação/tela bloqueada, downloads offline, login/cookies, playlists persistentes ou sincronização. Os favoritos são locais. O acesso ao YouTube depende das restrições do serviço e pode exigir futuras atualizações do extrator; conteúdo privado/restrito não é suportado nesta etapa.

A biblioteca pode conter arquivos locais importados; removê-los dos favoritos não apaga a cópia de áudio. Ela será removida ao limpar os dados/desinstalar o app. Não há conta nem telemetria do Hibiki. Buscas, streams e capas contatam os provedores do conteúdo.

### Plataformas e arquitetura

| Projeto | Responsabilidade |
|---|---|
| `Hibiki.Core` | Catálogo YT-DLP, modelos, contratos e armazenamento |
| `Hibiki.App` | Interface Avalonia compartilhada, tema e estado de reprodução |
| `Hibiki.Android` | Host Android, runtime embarcado e áudio nativo MediaPlayer |
| `Hibiki.Desktop` | Host Windows/Linux e áudio LibVLC |
| `Hibiki.Core.Tests` | Parser, tratamento de parâmetros, headers, falhas e favoritos |

No desktop, a base usa **yt-dlp no PATH** (ou `HIBIKI_YTDLP` com o caminho do executável), além de um runtime JavaScript compatível com YT-DLP. O pacote nativo LibVLC do Windows está referenciado; no Linux é necessário instalar LibVLC e os plugins de áudio da distribuição. **Os hosts desktop ainda não têm workflow de compilação nem validação de execução nesta primeira etapa.**

**Compilações e testes .NET são executados pelo GitHub Actions.** O workflow instala o workload Android e o SDK, verifica os runtimes contra versões/hashes fixos, executa os testes do núcleo, publica e valida a assinatura e o conteúdo do APK.

Sem compilar localmente, a estrutura pode ser verificada com `python3 scripts/check_source.py`.

## English

Hibiki is a music player by **Flugel**, built with **C# / .NET 10 and Avalonia UI 12**, using **YT-DLP** for search and audio extraction. One adaptive interface serves Android, Windows and Linux.

The initial milestone includes search/URL input, real results and artwork, streaming playback, seeking, a queue, shuffle/repeat, persistent favorites and local audio import. The Android APK bundles Python, QuickJS, YT-DLP and EJS. No Termux or external backend is required.

**Only Android ARM64 is built by GitHub Actions for now.** Download the installable APK from the `Hibiki-Android-arm64` workflow artifact. Android 7.0/API 24 or later is required. An Actions-generated development key is cached between builds without committing private keys or requiring personal signing secrets. If that cache expires or is removed, the signature changes and the old APK must be uninstalled before installing the new one, which removes local app data. This is not a production or Play Store signing identity.

Playback is foreground-only in this milestone. Background service, notification/lockscreen controls, offline downloading, accounts, persistent playlists and sync are not implemented. Desktop hosts are scaffolded with LibVLC but have not yet been built or runtime-tested by a desktop workflow. Desktop also requires yt-dlp and a supported JavaScript runtime; Linux requires system LibVLC/audio plugins.

.NET compilation and behavioral tests run in **GitHub Actions**, not locally. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for upstream components and source/build references.
