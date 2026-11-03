# Otimização da interface Android

As alterações implementam as prioridades do [diagnóstico](android-ui-performance.md): reaproveitar seções antes de criar os cartões, virtualizar as prateleiras horizontais e manter uma margem de pré-carregamento, compartilhar bytes e bitmaps de capas, e tirar a serialização e a gravação da thread de interface. O dock oculto deixa de receber atualizações quando o player também está fechado.

O cache mantém a URL original/alternativa e o tamanho exato de decodificação, com limites de memória e descarte de imagens sem consumidores. Trabalho pendente é cancelado quando não há mais consumidores. A política de resolução, recorte, escala da tela e capa de 1.200 px do player foi preservada. O teste verifica que o player não recebe o bitmap menor da miniatura.

O salvamento agrupa mudanças próximas e escreve a última cópia consistente em um único worker. Os arquivos continuam com os mesmos formatos e substituição atômica; o app aguarda o salvamento pendente na suspensão/saída. Durante a inicialização, o timer não substitui uma sessão que ainda não foi lida.

| Métrica | Antes | Depois |
| --- | ---: | ---: |
| Reavaliar início com as mesmas seções | 213,72 ms | 1,45 ms |
| Alocação gerenciada nessa reavaliação | 12,89 MB | 0,69 MB |
| Controles de capa montados | 150 | 14 |
| Bitmaps para oito controles da mesma capa | 8 | 1 |
| Bitmaps substituídos após reanexar | 8 | 0 |
| Trabalho síncrono da UI ao salvar 100 faixas e 1.000 estatísticas | 2,895 ms | 0,132 ms |

Comparação das medianas no ensaio Release, Skia headless, 390×844 DIP, com dados simulados. O tempo de salvar após a otimização mede a cópia e o enfileiramento na UI; a escrita ocorre em seguida no worker. Os dados brutos estão em [antes](android-ui-baseline.json) e [depois](android-ui-optimized.json). Esses valores não medem FPS nem representam tempos do telefone.

A compilação, os 54 testes do núcleo e as verificações de fila/rádio, rolagem a 360/390/430 DIP, cache de capas, resolução e persistência passaram. A confirmação de fluidez em hardware real é feita com o APK Android ARM64 do Actions.

```sh
dotnet run --project tests/Hibike.Ui.Checks -c Release -- --performance-checks --headless
```
