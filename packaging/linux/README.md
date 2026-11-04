# Hibike Linux x86_64 — experimental / experimental

## Português

AppImage autocontido quanto ao .NET; **VLC e bibliotecas do sistema não estão incluídos**. Base de compilação: Ubuntu 22.04, glibc 2.35 ou posterior. Compatibilidade com outras distribuições ainda requer validação.

Ubuntu/Debian:

```sh
sudo apt install libvlc5 vlc-plugin-base vlc-plugin-video-output python3 libfontconfig1 libice6 libsm6
chmod +x Hibike-x86_64.AppImage
./Hibike-x86_64.AppImage
```

AppImage requer FUSE 2 (`libfuse2` ou `libfuse2t64`, conforme a distribuição). Como alternativa sem FUSE:

```sh
./Hibike-x86_64.AppImage --appimage-extract
./squashfs-root/AppRun
```

Motor Linux: VLC, para áudio local e streaming. yt-dlp acompanha o pacote; usa Python 3 do sistema. Login Google usa fluxo OAuth no navegador, exige configuração própria e não tem a persistência protegida disponível no Windows/Android. Controles de mídia nativos Windows não estão disponíveis no Linux. Não há integração MPRIS nesta versão.

## English

The AppImage includes .NET, but **does not bundle VLC or system libraries**. Built on Ubuntu 22.04; requires glibc 2.35 or later. Other distributions still need validation. Install the packages above, make the AppImage executable and launch it. FUSE 2 is required (`libfuse2` or `libfuse2t64`, depending on your distribution); the extraction commands above work without FUSE.

Linux uses VLC for local audio and streaming. Bundled yt-dlp requires system Python 3. Google sign-in uses browser OAuth, requires your own configuration and does not offer Windows/Android protected session persistence. Windows native media controls are unavailable; MPRIS is not implemented.
