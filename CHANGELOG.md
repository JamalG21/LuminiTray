# Changelog - Lumini Tray

Todas as alterações notáveis deste projeto serão documentadas neste arquivo.
O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [0.25.0] - 2026-11-11

### 🚀 Adicionado & Aprimorado
- **Contorno Dinâmico nas Letras das Overlays:**
  - Adicionado efeito de contorno nítido (text outline / stroke via DropShadow shader com profundidade zero) nas fontes da Overlay de Jogos e do Mini Gerenciador de Tarefas quando o fundo estiver transparente.
  - Novos controles na aba de Overlays: ativar/desativar contorno, alternância automática inteligente (ativação somente com fundo transparente), controle deslizante de **grossura do contorno** (1.0 a 8.0 px) e **transparência/opacidade** (10% a 100%).
- **Exibição do Maior Consumidor de Memória RAM:**
  - O Mini Gerenciador de Tarefas agora identifica e exibe com clareza o processo que mais está consumindo memória física RAM, eliminando estado congelado em compressão de memória.
- **Botão " ZERAR HISTÓRICO\ na aba Inicialização:**
 - Permite ao usuário apagar o histórico de inicializações do Windows exibido na timeline a qualquer momento (marcador de corte persistente + limpeza do log de desempenho de boot quando executado como administrador).
- **Auto-reparo do início automático:**
 - A cada abertura, o Lumini Tray compara o caminho gravado no Agendador de Tarefas e na chave Run com a pasta atual do programa e regrava ambos automaticamente se a pasta tiver sido movida pelo usuário.

### 🛠️ Correções
- **Início automático com o Windows:**
 - O caminho do executável agora é sempre detectado dinamicamente a partir do processo em execução (sem caminhos fixos), garantindo funcionamento independente da pasta de instalação.
 - Falhas de privilégio no Agendador de Tarefas agora são verificadas e informadas ao usuário, mantendo registro seguro de contingência no Registro do Windows.
- **Correção da Timeline de Boot:**
 - Valores fictícios deixam de ser exibidos após o histórico de inicializações ser zerado.
- **Despertar e Ciclo de Vida do Mini Gerenciador:**
 - Corrigido travamento visual em \Coletando...\ e garantida a coleta contínua mesmo quando a janela principal é minimizada para a bandeja.