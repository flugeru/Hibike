<div align="center">

<img src="site/Assets/hibike.png" alt="Símbolo do Hibike" width="112" />

# Hibike 響

### Sua música. Seu ritmo.

Um espaço para sua biblioteca, suas próximas descobertas e tudo o que você quer ouvir de novo.

[![Release](https://img.shields.io/github/v/release/flugeru/Hibike?label=vers%C3%A3o&color=b784f7)](https://github.com/flugeru/Hibike/releases/latest)
[![Windows](https://img.shields.io/badge/Windows-x64-202020)](https://github.com/flugeru/Hibike/releases/latest)
[![Android](https://img.shields.io/badge/Android-7.0%2B%20%C2%B7%20ARM64-202020)](https://github.com/flugeru/Hibike/releases/latest)
[![MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-202020)](LICENSE)

[**Baixar Hibike**](https://github.com/flugeru/Hibike/releases/latest) · [Site oficial](https://flugeru.github.io/Hibike/) · [Reportar um problema](https://github.com/flugeru/Hibike/issues)

</div>

---

## Feito para ouvir

Hibike é um player de música para **Windows e Android**, com interface AMOLED, biblioteca local e descoberta musical. Combina seus arquivos com a busca nos provedores integrados, sem exigir uma conta para começar.

**A versão 0.1.0 é experimental.** Os recursos já estão disponíveis para uso, mas podem mudar durante o desenvolvimento.

| Sua coleção | Seu próximo som | Sua experiência |
| :--- | :--- | :--- |
| Importe músicas, preserve tags e capas e organize favoritos e playlists locais. | Escolha artistas e gêneros, explore recomendações e crie rádios a partir de uma faixa. | Alterne entre miniplayer e player expandido, com controles adaptados a cada tela. |

## Música com a sua identidade

### Um início mais pessoal

Na primeira abertura, escolha entre **3 e 20 artistas** e, se quiser, seus gêneros favoritos. Prefere explorar antes? Pule essa etapa e personalize depois.

A tela Início reúne conteúdo conforme sua disponibilidade:

- **Continuar ouvindo** — acesso às faixas recentes e ao histórico.
- **Feito para você** — sugestões combinando preferências, favoritos e hábitos de reprodução.
- **Seus artistas favoritos** — atalhos para explorar músicas dos artistas escolhidos.
- **Porque você gosta de…** — descobertas relacionadas por meio do rádio do YouTube Music.
- **Descubra algo novo** — espaço para artistas ainda não explorados.
- **Álbuns e favoritos** — acesso à sua coleção e aos álbuns identificados nos metadados.
- **Rankings e tendências** — listas públicas do YouTube Music, com região, fonte e horário de consulta.

Recomendações usam pontuação local, limites por artista e feedback **“Não tenho interesse”**. Seções sem conteúdo não aparecem; resultados de busca não são apresentados como rankings.

### Sua biblioteca, sem perder os detalhes

- Importação de áudio local, incluindo leitura de tags e capas embutidas em FLAC.
- Favoritos, playlists locais e fila persistente.
- Restauração da faixa, posição e fila ao reabrir, sem iniciar a reprodução automaticamente.
- Reprodução aleatória, repetição, volume e informações do áudio.
- Busca por música, artista ou link, com streaming pelos provedores integrados.

### Controles onde você precisa

**Windows:** reprodução com LibVLC, controles de mídia do sistema e Discord Rich Presence no cliente desktop, com faixa, artista, capa e tempo de reprodução.

**Android:** reprodução em segundo plano com Media3/ExoPlayer, notificação de mídia e controles na tela bloqueada. O motor de extração acompanha o APK; não exige Termux.

## Baixar e instalar

Todos os arquivos estão na [página de releases](https://github.com/flugeru/Hibike/releases/latest).

| Plataforma | Arquivo | Instalação |
| :--- | :--- | :--- |
| Windows x64 | `Hibike-Setup-x64.exe` | Execute o instalador. |
| Windows x64 portátil | `Hibike-win-x64.zip` | Extraia a pasta inteira e abra `Hibike.Desktop.exe`. |
| Android 7.0+ · ARM64 | `Hibike-Android-arm64.apk` | Abra o APK e autorize a instalação pela origem utilizada, se solicitado. |

As releases incluem `SHA256SUMS.txt` para conferir a integridade dos downloads e `signing-certificate.txt` com o certificado Android.

> **Atualizações Android:** os APKs usam uma chave de desenvolvimento do CI. Builds locais ou assinados com outra chave podem não aceitar atualização sobre a instalação existente. Não desinstale o aplicativo sem preservar seus arquivos: a desinstalação remove os dados locais. O pacote atual é `com.flugel.hibike`; não há migração automática do antigo `com.flugel.hibiki`.

## Privacidade e armazenamento

Seu perfil musical, preferências, histórico, favoritos e playlists são armazenados **no aparelho**, sem conta Hibike ou telemetria própria. Conteúdo já salvo pode ser consultado sem conexão; busca e streaming exigem acesso à rede.

- Busca, reprodução, rankings e imagens fazem requisições aos respectivos serviços externos.
- O yt-dlp não recebe cookies nem credenciais Google.
- Arquivos importados são copiados para o armazenamento privado do aplicativo. Remover um favorito não apaga o arquivo; limpar os dados do app apaga as cópias.
- O desinstalador Windows preserva os dados do usuário.
- No desktop, a busca de capa para FLAC no Discord pode consultar o iTunes com artista e álbum, sem enviar o áudio. `HIBIKE_DISCORD_RPC=0` desativa o Rich Presence e essa consulta.

## Estado do projeto

Hibike está em desenvolvimento ativo. Nesta versão:

- **Login Google e seletor experimental de fontes estão ocultos.** Não há sincronização entre dispositivos.
- Descoberta e rankings dependem de uma integração **não oficial do YouTube Music**, sujeita a mudanças. Falhas não impedem o acesso à biblioteca local.
- Recomendações usam rádio e metadados; não há análise acústica ou IA externa. Lançamentos sem dados verificáveis permanecem ocultos.
- A disponibilidade de streaming depende dos provedores, da região e das restrições de cada serviço.
- Linux possui um host no código, mas sua execução ainda não foi validada. Os downloads publicados são para Windows e Android.
- Diagnóstico e laboratório ficam desativados nos builds públicos.

Use o aplicativo respeitando os termos dos provedores e os direitos sobre o conteúdo acessado.

## Desenvolvimento

Construído com **C#**, **.NET 10** e **Avalonia UI 12**. A interface e o estado de reprodução são compartilhados entre as plataformas, com integração nativa de áudio e mídia.

| Projeto | Responsabilidade |
| :--- | :--- |
| `src/Hibike.Core` | Catálogo, metadados, descoberta, modelos e armazenamento. |
| `src/Hibike.App` | Interface Avalonia, perfil local e controle de reprodução. |
| `src/Hibike.Desktop` | Host desktop, LibVLC e integrações Windows/Discord. |
| `src/Hibike.Android` | Host Android, runtime embarcado e Media3. |
| `tests` | Testes Core e verificações de interface e áudio. |

### Verificações

```powershell
dotnet test tests/Hibike.Core.Tests -c Release
dotnet run --project tests/Hibike.Ui.Checks
python scripts/check_source.py
```

As verificações nativas de interface exigem Windows. Para compilar Android, instale o workload Android do .NET e o Android SDK.

### Builds locais

```powershell
# Windows portátil
./scripts/publish_windows.ps1

# Android ARM64, sem emulador
python scripts/prepare_android_runtime.py
dotnet publish src/Hibike.Android/Hibike.Android.csproj -f net10.0-android -c Release -r android-arm64 -p:HibikeSmokeTests=false -o artifacts/android
```

No desktop, `HIBIKE_YTDLP` permite indicar outro executável do yt-dlp. Ferramentas de desenvolvimento podem ser habilitadas explicitamente com `-p:HibikeDeveloperTools=true`.

## Contribuir

Encontrou um problema? [Abra uma issue](https://github.com/flugeru/Hibike/issues) com plataforma, versão do app e passos para reproduzir. Não inclua tokens, cookies, credenciais ou dados pessoais em logs e capturas.

## Licença

Distribuído sob a [licença MIT](LICENSE). Componentes e avisos de terceiros estão em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

<div align="center">

**Hibike 響** · Desenvolvido por [Flugel](https://github.com/flugeru)

</div>
