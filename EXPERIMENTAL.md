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
