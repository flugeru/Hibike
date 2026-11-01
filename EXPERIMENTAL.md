# Hibike — laboratório

Branch `feat/experimental-player-lab`; não integrada à main.
Acesso: Configurações → Laboratório experimental.

- Links HTTP/HTTPS de extratores yt-dlp, playlists limitadas a 100 entradas.
- Busca YouTube e SoundCloud, até 30 resultados.
- Adicionar resultados, ordenar e embaralhar fila; limpar preservando faixa atual.
- Repetir fila, pular -10/+10/+30 segundos, sleep timer 15/30/60 min.
- Histórico em memória da sessão, até 100 faixas.
- Download de áudio original para biblioteca privada, progresso no status e cancelamento.

Downloads limitados a stream HTTPS direto de até 1 GB, um por vez, com prazo de 30 minutos. Sem FFmpeg, conversão, manifestos HLS/DASH ou retomada. Cancelamento remove arquivo parcial. Não é garantia de suporte a todo serviço; DRM e restrições de acesso não são contornados. Baixe apenas conteúdo autorizado.

Histórico e sleep timer não persistem. Recursos ainda sem validação ponta a ponta em serviços reais e Android físico. Workflows desta branch geram artefatos de teste, não releases.

## Biblioteca da conta YouTube (Desktop)

Configurações → Biblioteca da conta YouTube. API oficial com `youtube.readonly`: playlists da conta e vídeos curtidos, até 1000 entradas por lista, incluindo metadados autorizados de playlists privadas. Não fornece feed personalizado, histórico, uploads musicais ou biblioteca completa do YT Music. Reprodução continua anônima: itens privados/restritos podem não tocar. Cookies antigos não são mais utilizados pelo yt-dlp; botão nas configurações permite apagá-los.

1. Crie projeto no Google Cloud e ative YouTube Data API v3.
2. Configure tela de consentimento e adicione sua conta como usuário de teste.
3. Crie cliente OAuth do tipo **Desktop app**, baixe JSON e importe pelo botão **Importar configuração OAuth Desktop**. Não publique esse arquivo no Git.
4. Clique **Conectar com Google**, autorize leitura no navegador e volte ao Hibike.

PKCE S256 e validação de state protegem retorno local. Token fica apenas em memória, sem refresh ou armazenamento; reconecte após expirar/fechar. Desconectar remove referência local, não revoga autorização no Google; revogação disponível em https://myaccount.google.com/permissions. Configuração OAuth fica no diretório de dados e pode ser apagada pela interface. Nenhum token é entregue ao yt-dlp. Android ainda não tem login: precisa integração nativa de autorização Google, pacote/assinatura registrados e configuração própria; fluxo Desktop não é usado no celular.
