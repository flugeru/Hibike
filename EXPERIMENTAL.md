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

## Biblioteca da conta YouTube (Desktop e Android)

Configurações → Biblioteca da conta YouTube. API oficial com `youtube.readonly`: playlists da conta e vídeos curtidos, até 1000 entradas por lista, incluindo metadados autorizados de playlists privadas. Não fornece feed personalizado, histórico, uploads musicais ou biblioteca completa do YT Music. Reprodução continua anônima: itens privados/restritos podem não tocar. Cookies antigos não são mais utilizados pelo yt-dlp; botão nas configurações permite apagá-los.

1. Crie projeto no Google Cloud e ative YouTube Data API v3.
2. Configure tela de consentimento e adicione sua conta como usuário de teste.
3. Crie cliente OAuth do tipo **Desktop app**, baixe JSON e importe pelo botão **Importar configuração OAuth Desktop**. Não publique esse arquivo no Git.
4. Clique **Conectar com Google**, autorize leitura no navegador e volte ao Hibike.

Desktop: PKCE S256 e validação de state protegem retorno local. Token fica apenas em memória, sem refresh ou armazenamento; reconecte após expirar/fechar. Desconectar remove referência local, não revoga autorização no Google; revogação disponível em https://myaccount.google.com/permissions. Configuração OAuth fica no diretório de dados e pode ser apagada pela interface. Nenhum token é entregue ao yt-dlp.

Android: Configurações → Biblioteca da conta YouTube → Entrar com Google. Usa AuthorizationClient do Google Play Services e solicita somente `youtube.readonly`, com seleção/consentimento nativo quando necessário. Não precisa importar JSON nem embutir segredo: Google identifica aplicativo pelo pacote `com.flugel.hibike` e certificado de assinatura. Registre cliente OAuth Android com esses dados no mesmo projeto com API ativada e usuário de teste. Dispositivo precisa Google Play Services. Token fica somente em memória; após expiração, desconecte e entre novamente.

Artefato Android no GitHub inclui `signing-certificate.txt` com SHA-1 do APK gerado. SHA-1 do APK local não serve para APK do GitHub. Assinatura de desenvolvimento do CI ainda depende de cache: se chave for recriada, cadastro OAuth precisa ser atualizado. Não confundir esse certificado com assinatura estável de produção. Login real Android ainda precisa validação em aparelho com conta autorizada; check automatizado confirma registro da entrada de autorização, não consentimento real.
