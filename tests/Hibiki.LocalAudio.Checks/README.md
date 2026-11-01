# Checks locais e Discord

```powershell
dotnet run --project tests/Hibiki.LocalAudio.Checks -- --presence-only
dotnet run --project tests/Hibiki.LocalAudio.Checks -- "C:\musicas\faixa.flac"
```

Primeiro comando verifica Listening, timestamps, limite UTF-8, filtro de capas locais e correspondência de álbum sem acessar Discord ou rede.
Segundo exige FLAC com tags e capa embutida; verifica extração e metadados usando LibVLC, com volume zero.

Rich Presence desktop usa Application ID `1566415890596241459`, sem token da conta.
Discord deve estar aberto com compartilhamento de atividade ativado. Pausa, carregamento e encerramento removem presença.
Capas online usam URL HTTPS. FLAC consulta iTunes por artista/álbum, exige correspondência dos dois e não envia arquivo.
Sem resultado, presença continua sem capa. Resultados ficam em memória durante sessão.
`HIBIKI_DISCORD_RPC=0` desativa integração e consultas de capas.
Validação do payload não comprova exibição no perfil: conferir Listening, capa, seek, pausa, troca de faixa e reconexão no Discord real.
