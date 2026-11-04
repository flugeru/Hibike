<div align="center">

<img src="site/Assets/hibike.png" alt="Logo do Hibike" width="128" />

# Hibike 響

**Sua música. Seu ritmo.**

Biblioteca local e novas descobertas, no mesmo player.

[![Release](https://img.shields.io/github/v/release/flugeru/Hibike?label=release&color=b784f7&style=flat-square)](https://github.com/flugeru/Hibike/releases/latest)
[![Windows](https://img.shields.io/badge/Windows-x64-202020?style=flat-square)](https://github.com/flugeru/Hibike/releases/latest)
[![Android](https://img.shields.io/badge/Android-7.0%2B%20%C2%B7%20ARM64-202020?style=flat-square&logo=android&logoColor=b784f7)](https://github.com/flugeru/Hibike/releases/latest)
[![Licença](https://img.shields.io/badge/licen%C3%A7a-MIT-202020?style=flat-square)](LICENSE)

[**Baixar Hibike**](https://github.com/flugeru/Hibike/releases/latest) · [Site oficial](https://flugeru.github.io/Hibike/) · [Novidades](https://github.com/flugeru/Hibike/releases) · [Reportar problema](https://github.com/flugeru/Hibike/issues)

</div>

---

Hibike é um player de música para **Windows e Android**, com visual AMOLED, interface responsiva e reprodução de arquivos locais e streaming. Use sem conta ou conecte sua conta Google, opcionalmente.

## Uma biblioteca do seu jeito

| Biblioteca | Descoberta | Reprodução |
| :--- | :--- | :--- |
| Importe músicas com tags e capas. Organize favoritos, playlists e sua fila. | Explore buscas, rádios, recomendações, artistas e coleções. | Alterne entre miniplayer e player expandido, com informações técnicas do áudio. |

- **No Android:** reprodução em segundo plano e controles de mídia do sistema.
- **No Windows:** libmpv como padrão, VLC como alternativa e integração com Discord.
- **No dia a dia:** fila e volume persistentes, com sessão Google armazenada de forma protegida.

## Downloads

| Plataforma | Download | Como usar |
| :--- | :--- | :--- |
| Windows x64 | [Instalador `.exe`](https://github.com/flugeru/Hibike/releases/latest/download/Hibike-Setup-x64.exe) | Execute o instalador. |
| Windows x64 | [Portátil `.zip`](https://github.com/flugeru/Hibike/releases/latest/download/Hibike-win-x64.zip) | Extraia a pasta inteira e abra `Hibike.Desktop.exe`. |
| Android 7.0+ · ARM64 | [Aplicativo `.apk`](https://github.com/flugeru/Hibike/releases/latest/download/Hibike-Android-arm64.apk) | Instale o APK no aparelho compatível. |

Releases incluem [checksums SHA-256](https://github.com/flugeru/Hibike/releases/latest/download/SHA256SUMS.txt), [certificado Android](https://github.com/flugeru/Hibike/releases/latest/download/signing-certificate.txt) e fontes correspondentes do libmpv.

> **Projeto em evolução.** Consulte as [notas da versão](https://github.com/flugeru/Hibike/releases/latest) para limitações conhecidas e orientações de atualização.

> **Preserve seus dados.** APKs locais e das releases podem ter assinaturas diferentes. Não desinstale nem limpe dados para contornar uma falha de atualização sem preservar sua biblioteca e seus arquivos. Músicas importadas são copiadas para o armazenamento privado do aplicativo e podem ser apagadas ao desinstalar ou limpar dados.

## Privacidade e transparência

Biblioteca, histórico e preferências ficam no aparelho. Login Google é opcional; a sessão é protegida pelo Android Keystore ou Windows DPAPI e fica fora dos backups do aplicativo. Não há sincronização completa entre dispositivos.

Busca, streaming e imagens dependem de serviços externos. A integração com Google/YouTube Music é **não oficial**, pode ficar indisponível e não implica vínculo com essas empresas. Utilize conteúdo conforme os direitos e termos dos respectivos provedores.

[Política de privacidade](PRIVACY_POLICY.md) · [Termos de uso](TERMS_OF_SERVICE.md) · [Componentes de terceiros](THIRD-PARTY-NOTICES.md)

## Contribua

Encontrou um problema ou tem uma ideia? [Abra uma issue](https://github.com/flugeru/Hibike/issues) com plataforma, versão e passos para reproduzir. **Não compartilhe cookies, tokens ou credenciais.**

<details>
<summary><strong>Desenvolvimento e builds</strong></summary>

Construído com **C#, .NET 10 e Avalonia UI 12**. Android requer workload Android e SDK; empacotamento Windows requer ambiente Windows.

```powershell
# Verificações
dotnet test tests/Hibike.Core.Tests -c Release
dotnet run --project tests/Hibike.Ui.Checks -c Release -- --headless
python scripts/check_source.py

# Windows
./scripts/publish_windows.ps1

# Android ARM64
python scripts/prepare_android_runtime.py --abi arm64-v8a
dotnet publish src/Hibike.Android/Hibike.Android.csproj -f net10.0-android -c Release -r android-arm64 -p:HibikeSmokeTests=false -o artifacts/android
```

Os scripts verificam os componentes baixados antes do empacotamento. Licenças e referências estão em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

</details>

---

<div align="center">

Desenvolvido por [Flugel](https://github.com/flugeru) · Código sob [licença MIT](LICENSE)

</div>
