# Motor de reprodução, persistência e plataformas

Auditoria do pipeline `MusicController` → catálogo/yt-dlp → player (libmpv, VLC, ExoPlayer) → armazenamento local, com correções na causa de cada falha. A identidade, os formatos de arquivo e o comportamento visível foram preservados.

## Defeitos corrigidos

| Área | Causa | Correção |
| --- | --- | --- |
| Saída no desktop | `FlushPendingSaves()` disparava o salvamento sem aguardar; o worker ainda esperava 50 ms antes de gravar, e o processo terminava antes. Fila, posição, preferências e volume da última sessão eram perdidos. | `LatestSnapshotWriter.Flush(timeout)` bloqueia até gravar (seguro na thread de UI). Um flush acorda o worker na hora e ele encerra sem a espera ociosa final. |
| Volume | Cada passo do slider gravava `volume.txt` de forma síncrona na thread de UI. | Gravação agrupada em worker, incluída nos flushes de suspensão e saída. |
| Desktop (libmpv) | O player não informava `IsBuffering`; a recuperação de travamento do motor nunca disparava quando a rede caía durante um stream. | `IsBuffering` reflete `paused-for-cache`, como no Android. |
| Android 7 (API 24–25) | Cancelar uma extração chamava `Process.destroyForcibly()` (API 26+), lançando `NoSuchMethodError` no lugar do cancelamento e sem aguardar o processo. | Chamada protegida por versão; demais verificações de versão passam a usar `OperatingSystem.IsAndroidVersionAtLeast`, que o analisador reconhece. |
| Downloads | Pausar manualmente uma transferência que ainda encerrava uma pausa por rede mantinha `NetworkPaused`; ao reconectar, ela voltava sozinha. | A pausa manual sempre limpa o marcador de rede. |
| Fim de faixa | O avanço automático rodava em `async void` no dispatcher; uma exceção encerraria o app. | Falhas são registradas e exibidas como erro de reprodução. |
| Windows Media | Uma capa local ilegível impedia publicar título e artista; uma `Track` substituída por metadados locais bloqueava novas tentativas. | A capa é opcional e a comparação usa a origem da faixa. |
| Login Google (desktop) | Uma conexão local sem requisição prendia o único listener de callback até o login expirar. | Leitura da requisição limitada a 5 s por conexão. |

## Desempenho medido

Medianas em Release, .NET 10, mesma máquina Linux x64; carga sintética e offline. Não representam tempos de telefone.

| Carga | Antes | Depois |
| --- | ---: | ---: |
| Deduplicação do rádio: 100 candidatos × 1.100 faixas de fila/histórico (thread de UI) | 435,0 ms · 435,2 MB alocados | 87,0 ms · 11,7 MB |
| `ReplaceQueue` com 1.000 faixas: alocação | 4,50 MB | 0,37 MB |
| Salvar favoritos com 5.000 faixas | 87,2 ms · 13,6 MB | 37,4 ms · 1,9 MB |
| Flush após enfileirar um estado | ≥ 50 ms + espera ociosa | imediato |

- **Rádio:** palavras normalizadas e artista de cada `Track` são calculados uma vez por instância (`ConditionalWeakTable`) com regex gerada em compilação. As regras de correspondência são as mesmas: 360.000 pares aleatórios deram resultados idênticos ao algoritmo anterior.
- **Fila:** `CurrentQueueIndex` copiava a fila inteira em cada chamada, e é avaliado a cada mudança da coleção. O tempo de `ReplaceQueue` é dominado pelas notificações da coleção e não mudou de forma mensurável (≈ 9–12 ms).
- **Favoritos:** a leitura protetora antes de salvar é pulada só quando o arquivo continua com o tamanho e a data que o próprio store leu ou gravou. Qualquer alteração externa ainda é validada, e um arquivo corrompido nunca é substituído (teste de regressão).
- **Android:** o serviço consultava o ExoPlayer a cada 100 ms indefinidamente, inclusive pausado e em segundo plano. Agora usa 100 ms só ao preparar, carregar ou tocar, e 1 s quando ocioso. Comandos e eventos do player reiniciam a amostragem na hora. A conectividade vem do callback de rede padrão do sistema, sem várias chamadas JNI a cada leitura. Esses ganhos não foram medidos em aparelho.

## Reproduzir

```sh
dotnet test tests/Hibike.Core.Tests -c Release
dotnet run --project tests/Hibike.Ui.Checks -c Release -- --headless
dotnet run --project tests/Hibike.Ui.Checks -c Release -- --headless --performance-study
```

`--performance-study` agora também registra `replaceQueue1000Tracks` e `radioMatch100CandidatesVs1100Queued`.
