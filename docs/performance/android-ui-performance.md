# Investigação dos engasgos da interface Android

As correções e a comparação posterior estão em [android-ui-optimization.md](android-ui-optimization.md).

Estudo de 02/11/2026 sobre a branch `queue-fix`, base `818b8200cc465fe938a3087f9c2dce3b9843ecf7`, Avalonia 12.1.4. Este commit acrescenta diagnóstico e documentação; não aplica otimizações à interface nem altera imagens, resolução, recorte ou qualidade das capas.

A evidência mais forte está na montagem da tela inicial: o código constrói cartões que acaba descartando e mantém muitas capas fora da área visível. A ausência de cache de imagens e a persistência síncrona aumentam o trabalho. São problemas verificados no código e em ensaio local; a participação de cada um nos engasgos percebidos no Poco X6 Pro ainda depende de um trace no aparelho.

## Método e resultados

Ensaio Release em Linux, .NET 10.0.14, Avalonia Headless com renderizador Skia, janela de 390×844 DIP. Catálogo e player simulados; capas do teste de reutilização vêm de um arquivo local. A preparação fornece onboarding concluído e cache de descoberta recente para impedir consultas externas. Nenhum dispositivo Android ou emulador é usado.

A carga contém 60 favoritos, histórico de 40 faixas, recomendações e três listas simuladas. O ensaio de busca usa 1.000 entradas. Cada operação temporizada tem aquecimento excluído e 8–50 amostras; os valores abaixo são medianas. A alocação é a gerenciada da thread chamadora e não inclui texturas, memória nativa do Skia, threads auxiliares nem o custo posterior de layout/renderização.

| Observação | Resultado local | Interpretação |
| --- | --- | --- |
| Início com assinatura dos dados já conhecida | 0,072 ms; 10.424 bytes/chamada | O retorno antecipado é barato. |
| Início forçando reavaliação, com as mesmas 9 seções reaproveitadas | 213,716 ms; 12.892.432 bytes/chamada | Construção descartável de cartões, apesar do reaproveitamento das seções. |
| Capas montadas na árvore do início | 150 | Não há virtualização das prateleiras. |
| Capas cujo retângulo cruza a janela | no máximo 9 | Limite superior: desconsidera recortes adicionais dos ScrollViewers e áreas cobertas pelo cabeçalho/player. |
| Busca com 1.000 faixas | 5 linhas registradas como realizadas | A lista de busca está virtualizando neste cenário; não é correto afirmar que todas as listas falham. |
| Atualização completa de busca estável | 2,460 ms; 245.592 bytes/chamada | Assinaturas, formatação e verificações ainda alocam sem trocar a lista. |
| Tick de reprodução com player fechado | 0,005 ms; 408 bytes/chamada | Não foi um gargalo relevante isoladamente neste ensaio. |
| Atualização do dock desktop oculto | 0,012 ms; 1.080 bytes/chamada | Trabalho evitável no celular, mas secundário frente à montagem do início. |
| Salvar sessão de 100 faixas e preferências | 0,923 ms; 96.640 bytes/chamada | Escrita e serialização bloqueiam a thread da interface. |
| Mesmo salvamento com 1.000 estatísticas | 2,895 ms; 316.240 bytes/chamada | O custo cresce com os dados; estes tempos não representam o armazenamento do telefone. |
| Oito controles da mesma capa local | 8 bitmaps distintos | Não compartilham o resultado da decodificação. |
| Retirar e recolocar os mesmos oito controles | 8 bitmaps substituídos | Desanexar descarta a imagem e anexar decodifica novamente. |

O cenário de reavaliação limpa `_homeFingerprint` deliberadamente. Ele isola o custo que ocorre quando algum estado muda e o código precisa reavaliar as seções. Na execução normal, histórico, faixa atual, fila, estado de carregamento e datas dos rankings fazem parte dessa assinatura; não se trata de um custo pago a cada tick nem de uma medição da frequência real desses eventos no Android.

Os tempos não são FPS do aparelho, não predizem quantos frames ele perde e não provam que GPU, rede, armazenamento ou GC sejam isoladamente a causa. Os valores brutos estão em [android-ui-baseline.json](android-ui-baseline.json).

## Caminhos encontrados no Hibike

**1. Cartões são construídos antes da comparação de reaproveitamento.** Em [MainView.cs](../../src/Hibike.App/Views/MainView.cs), `RefreshHome` passa sequências de `CoverCard`/`TrackCard` a `Shelf`. O `ToArray` de `Shelf` materializa os controles antes de calcular a chave e comparar com a seção anterior. Mesmo quando a seção existente vence a comparação, botões, grades, textos, ícones e controles de capa novos já foram criados. O ensaio confirma o reaproveitamento das 9 seções e, simultaneamente, a grande alocação desse caminho. Pressão adicional sobre GC é uma consequência plausível; suas pausas precisam ser medidas no telefone.

**2. O início não virtualiza o conteúdo das prateleiras.** `_home` e cada linha horizontal são `StackPanel` dentro de `ScrollViewer`, em vez de um painel virtualizador com viewport delimitado. Estar fora da região recortada não significa ser desanexado da árvore visual. Como [Artwork.cs](../../src/Hibike.App/Views/Artwork.cs) inicia a carga em `AttachedToVisualTree`, uma capa fora da tela também pode começar a carregar. O teste contou controles anexados, não requisições HTTP reais.

**3. Capas não têm cache compartilhado no controle atual.** `Artwork` usa um `HttpClient` estático, mas isso não implementa por si só cache explícito dos bytes/bitmaps nem unificação de solicitações simultâneas. A decodificação já usa `Task.Run` e dois slots — isso é positivo. Cada controle, porém, possui seu bitmap; ao desanexar, cancela e o descarta. No teste local, a mesma imagem foi decodificada oito vezes e novamente ao reanexar. A repetição de download remoto é um risco deduzido desse caminho, não uma contagem medida no ensaio.

**4. Serialização e gravação acontecem na thread da interface.** Em [MusicController.cs](../../src/Hibike.App/MusicController.cs), `Changed` chama `SaveSession`, e o timer também salva a cada cinco segundos. [PlaybackSessionStore.cs](../../src/Hibike.Core/Services/PlaybackSessionStore.cs) e `SavePersonalization` usam `JsonSerializer.Serialize`, `File.WriteAllText` e `File.Move` síncronos. `PersistPersonalization` ainda salva as preferências e emite `Changed`, que pode salvá-las novamente. O teste verificou execução na UI thread. A duração no armazenamento real do Android permanece por medir.

**5. Há trabalho secundário que não deve virar a primeira aposta.** O dock desktop continua recebendo atualizações quando invisível no layout móvel. `UpdateRows` recria pincéis e reaplica propriedades. O equalizador altera `Height` das barras, invalidando layout; máscaras de opacidade podem adicionar custo gráfico. Nada disso foi isolado como causa dominante neste estudo. O marquee do título está no caminho do layout desktop e não deve ser apontado como explicação geral do problema no Android.

## Documentação e projetos comparados

- [Avalonia: performance](https://docs.avaloniaui.net/docs/app-development/performance): virtualização precisa de medidas limitadas; criar muitos controles e alterar propriedades de layout aumenta o trabalho. Aplicação aqui: realizar apenas o conteúdo necessário e comparar dados antes de construir a árvore.
- [Avalonia: threading](https://docs.avaloniaui.net/docs/app-development/threading): callbacks de `DispatcherTimer` executam na UI thread. Aplicação aqui: não fazer serialização/escrita de arquivos no callback. Isso não significa que todo desenho do backend Android necessariamente ocorra nessa mesma thread.
- [ControlCatalog/ListBoxPage](https://github.com/AvaloniaUI/Avalonia/blob/HEAD/samples/ControlCatalog/Pages/ListBoxPage.xaml): exemplo oficial com lista e configuração do painel virtualizador. A versão instalada deve ser conferida antes de copiar nomes de propriedades de documentação mais recente.
- [KugouMusic.NET — cache de imagens](https://github.com/Linsxyx/KugouMusic.NET/blob/HEAD/src/Apps/KugouAvaloniaPlayer/Services/BoundedDiskCachedWebImageLoader.cs): o player utiliza retenção limitada, compartilhamento de imagens, contagem de consumidores e decodificação fora da UI. Sua [lista de músicas](https://github.com/Linsxyx/KugouMusic.NET/blob/HEAD/src/Apps/KugouAvaloniaPlayer/Controls/SongCollectionDetailView.axaml) usa `ListBox`/`VirtualizingStackPanel`. É uma referência de implementação desktop, não uma prova de desempenho no Android.
- [AsyncImageLoader.Avalonia — MemoryImageCache](https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia/blob/HEAD/AsyncImageLoader.Avalonia/Core/Caching/MemoryImageCache.cs): reúne carregamentos e usa leases para compartilhar a imagem sem descartá-la enquanto outro controle a utiliza. Referência de ciclo de vida para um futuro cache do Hibike, sem necessidade de importar uma biblioteca nesta investigação.
- [Android: slow rendering](https://developer.android.com/topic/performance/issues/render) e [system tracing](https://developer.android.com/topic/performance/tracing): a verificação de jank deve incluir aparelho real e correlação de frames com atividade das threads. Perfetto ajuda nessa etapa; o ensaio headless não a substitui.

Também foram verificados os flags de [Hibike.Android.csproj](../../src/Hibike.Android/Hibike.Android.csproj): AOT e profiled AOT estão explicitamente desligados. A [documentação do .NET for Android](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties#androidenableprofiledaot) explica essas opções. JIT em telas acessadas pela primeira vez é um candidato adicional, ainda não medido; mudar AOT/trimming exige validação separada de compatibilidade e não resolve controles descartáveis.

## Ordem de correção proposta, preservando a experiência

1. Fazer `Shelf` receber dados e uma fábrica de cartões; comparar a chave antes de chamar a fábrica. Reavaliar só as seções cujos dados mudaram. Critério: no cenário das mesmas seções, nenhuma nova instância de cartão.
2. Virtualizar/pré-carregar o início e suas linhas com viewport e buffer adequados, preservando ordem, dimensões, rolagem e efeitos. Critério: controles realizados acompanham viewport + buffer, não o catálogo inteiro; rolar mantém posições e acessibilidade.
3. Compartilhar bytes originais e bitmaps por identidade da imagem e parâmetros de decodificação, com orçamento de memória, cancelamento por consumidor e descarte seguro. Critério: solicitações iguais compartilham trabalho; o player continua obtendo a resolução exigida, sem usar uma miniatura inadequada do cache.
4. Capturar um snapshot consistente dos dados na UI e serializar/gravar em um escritor de fundo único, agrupando eventos próximos. Manter escrita atômica, ordenação, tratamento de erros e flush ao suspender/encerrar. Não executar leituras concorrentes de `ObservableCollection`/dicionários mutáveis via um `Task.Run` ingênuo.
5. Medir novamente no aparelho e só então priorizar atualizações de controles invisíveis, animações de layout, máscaras e AOT. Manter a cadência e os efeitos visuais durante a comparação.

Nenhuma dessas etapas exige baixar a resolução das capas, diminuir a qualidade de interpolação, trocar por placeholders permanentes ou remover conteúdo. O objetivo é deixar de repetir o mesmo trabalho, reutilizando os mesmos dados de imagem.

## Reprodução e confirmação no aparelho

```sh
dotnet run --project tests/Hibike.Ui.Checks -c Release -- --performance-study --headless
```

O comando escreve `android-ui-study.json` ao lado do executável de testes. Os resultados são diagnósticos, não limites fixos de aprovação que variam com a máquina.

Para fechar a atribuição no Android: usar APK Release no Poco, comparar abertura fria/quente, rolagem vertical do início, rolagem de uma prateleira, troca de faixa e ida/volta do player; capturar um trace de sistema/Perfetto e correlacionar jank com layout, criação de controles, GC e escritas. Comparar os mesmos dados e capas antes/depois, sem emulador. Não há trace do aparelho neste estudo.
