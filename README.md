<div align="center">

<img src="site/Assets/hibike.png" alt="Símbolo do Hibike" width="112" />

# Hibike 響

Sua música. Seu ritmo.

[![Release](https://img.shields.io/github/v/release/flugeru/Hibike?label=vers%C3%A3o&color=b784f7)](https://github.com/flugeru/Hibike/releases/latest)
[![MIT](https://img.shields.io/badge/licen%C3%A7a-MIT-202020)](LICENSE)

[**Downloads**](https://github.com/flugeru/Hibike/releases/latest) · [Site](https://flugeru.github.io/Hibike/) · [Problemas](https://github.com/flugeru/Hibike/issues)

</div>

Player de música para **Windows x64 e Android 7.0+ ARM64**, com interface AMOLED, biblioteca local e descoberta musical. **Versão 0.2.0 experimental.**

## Recursos

- Importação de músicas com tags e capas, favoritos, playlists e fila persistente.
- Busca, rádio, recomendações e páginas de artistas e coleções.
- Miniplayer, player expandido e informações técnicas do áudio.
- Login Google opcional, sessão protegida e restauração ao reabrir. Conta não é obrigatória.
- Controles de mídia do sistema, reprodução Android em segundo plano e integração Discord no Windows.
- Windows com libmpv padrão e VLC selecionável após reiniciar, para arquivos locais e streaming.

## Instalação

| Download | Uso |
| :--- | :--- |
| `Hibike-Setup-x64.exe` | Instalador Windows. |
| `Hibike-win-x64.zip` | Extraia a pasta inteira e abra `Hibike.Desktop.exe`. |
| `Hibike-Android-arm64.apk` | Instale no Android ARM64. |

Releases incluem `SHA256SUMS.txt` e `signing-certificate.txt`.

> APKs locais e do CI podem ter assinaturas diferentes e não aceitar atualização entre si. Não desinstale nem limpe dados para contornar isso sem preservar sua biblioteca e seus arquivos. Pacote atual: `com.flugel.hibike`.

## Privacidade e limites

Biblioteca, histórico e preferências ficam no aparelho. Sessão Google usa Android Keystore ou Windows DPAPI, fica fora dos backups do aplicativo e não é enviada ao yt-dlp. Não há sincronização completa entre dispositivos.

Busca, streaming e imagens consultam serviços externos. Integração YouTube Music é não oficial e pode falhar ou ser bloqueada. Arquivos importados são copiados para armazenamento privado; limpar dados ou desinstalar pode apagar essas cópias. Use conteúdo conforme direitos e termos dos provedores.

MediaInfo no Windows é opcional e não acompanha os downloads. Android usa APIs nativas e parser FLAC, não MediaInfo. Cobertura de formatos e persistência Google real ainda exigem validação em aparelhos. Fontes experimentais e rankings permanecem ocultos.

[Privacidade](PRIVACY_POLICY.md) · [Termos](TERMS_OF_SERVICE.md) · [Componentes de terceiros](THIRD-PARTY-NOTICES.md)

## Desenvolvimento

C#, .NET 10 e Avalonia UI 12. Android exige workload Android e SDK; builds públicos não usam emulador.

```powershell
dotnet test tests/Hibike.Core.Tests -c Release
dotnet run --project tests/Hibike.Ui.Checks -c Release -- --headless
python scripts/check_source.py

# Windows
./scripts/publish_windows.ps1

# Android ARM64
python scripts/prepare_android_runtime.py --abi arm64-v8a
dotnet publish src/Hibike.Android/Hibike.Android.csproj -f net10.0-android -c Release -r android-arm64 -p:HibikeSmokeTests=false -o artifacts/android
```

Relate problemas com plataforma, versão e passos para reproduzir. Não inclua cookies, tokens ou credenciais.

[Licença MIT](LICENSE) · Desenvolvido por [Flugel](https://github.com/flugeru)
