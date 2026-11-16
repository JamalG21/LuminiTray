# Changelog - Lumini Tray

Todas as alterações notáveis deste projeto serão documentadas neste arquivo.
O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [0.30.5] - 2026-11-15

### 🛠️ Correções
- **Endpoint Oficial de Diagnóstico XAML (`VisualDiagConnection1` / `VisualDiagConnection2`):**
  - Corrigida falha introduzida na `0.30.3` onde o endpoint customizado `LuminiDiagV2` retornava `0x80070490` (`ERROR_NOT_FOUND`) em `Windows.UI.Xaml.dll`, impedindo o carregamento do hook no `explorer.exe`. Agora utiliza exclusivamente os endpoints válidos `VisualDiagConnection1` e `VisualDiagConnection2`.
  - Aprimorado o hook `LuminiTray.TaskbarHook.v3.dll` para rastrear múltiplos ícones em `MainStack` (Microfone, GPS, Studio Effects e Recall) e desregistrar callback do Sino quando definido como `Nunca ocultar`.

## [0.30.4] - 2026-11-15

### 🛠️ Correções
- **Erro "Attempted to perform an unauthorized operation" e Reversão dos Ícones:**
  - No Windows 11 24H2+, a chave `TaskbarDa` (Widgets) é protegida pelo sistema e lançava exceção, abortando a aplicação **antes** de sinalizar o hook — por isso desmarcar opções ou **Restaurar Padrão** não revertiam Volume, Rede e demais ícones.
  - Cada valor do Registro agora é gravado de forma isolada; falhas em chaves protegidas não interrompem os demais ajustes e o hook é sempre sinalizado. O usuário recebe um aviso indicando qual ajuste o Windows bloqueou, sem caixa de erro.

## [0.30.3] - 2026-11-15

### 🛠️ Correções
- **Atualização Sem Travamento da DLL do Hook (`LuminiTray.TaskbarHook.v2.dll`):**
  - Alterados o nome do arquivo extraído, o endpoint de diagnóstico (`LuminiDiagV2`) e o evento de atualização (`LuminiTrayTaskbarTweaksRefresh_v2`) para evitar conflito com instâncias antigas da DLL mantidas em memória pelo `explorer.exe`.

## [0.30.2] - 2026-11-15

### 🛠️ Correções
- **Reversão e Restauração Padrão dos Ícones da Barra de Tarefas:**
  - Corrigida falha no `LuminiTray.TaskbarHook.dll` em que a thread de atualização (`RefreshWatcherProc`) acessava `XamlRoot.Content()` fora da thread de UI do `explorer.exe`, impedindo que desmarcar opções ou clicar em **"Restaurar Padrão"** revertesse os ícones ocultos. Agora o `CoreDispatcher` é capturado em `winrt::agile_ref` na thread de UI.

## [0.30.1] - 2026-11-15

### 🛠️ Correções
- **Correção Real dos Ícones da Bandeja & Barra de Tarefas no Windows 11:**
  - Substituído o mecanismo anterior baseado apenas em políticas legadas (ignoradas pelo Windows 11 22H2+) pelo hook nativo embutido `LuminiTray.TaskbarHook.dll` via API oficial `InitializeXamlDiagnosticsEx` (`Windows.UI.Xaml.dll`), manipulando em tempo real a árvore WinUI XAML do `explorer.exe` sem downloads adicionais e sem reiniciar o Explorer.
  - Adicionados os controles faltantes do módulo: **Bateria em escala de cinza**, **Microfone (Windows)**, **Localização / GPS**, **Studio Effects**, **Ícones suplementares de idioma** e os 4 modos do **Ícone do Sino** (`Nunca`, `Sem novas notificações`, `Sem notificações e Não Perturbe off`, `Sempre`), com reaplicação automática ao iniciar.

## [0.30.0] - 2026-11-15

### 🚀 Adicionado & Aprimorado
- **Ocultar Ícones da Bandeja & Barra de Tarefas (Tweaks Exclusivos):**
  - Novo card **BARRA DE TAREFAS & ÍCONES DO SISTEMA** na aba **Tweaks Exclusivos** com manipulação direta em tempo real da árvore WinUI XAML do `explorer.exe` via `LuminiTray.TaskbarHook.dll` embutida (`InitializeXamlDiagnosticsEx`), sem precisar baixar Windhawk, símbolos PDB ou dependências externas.
  - Suporta: **Volume**, **Rede / Wi-Fi**, **Bateria**, **Bateria em escala de cinza**, **Microfone (Windows)**, **Localização / GPS**, **Studio Effects**, **Windows Recall**, **Barra de Idiomas**, **Ícones suplementares de idioma**, **Ícone do Sino** (`Nunca`, `Sem novas notificações`, `Sem notificações e Não Perturbe off`, `Sempre`), **Mostrar área de trabalho**, **Visão de Tarefas** e **Widgets**.
  - Botão único **"Aplicar Alterações"** (sem reiniciar o `explorer.exe`) e botão **"Restaurar Padrão"**.

## [0.29.0] - 2026-11-15

### 🚀 Adicionado & Aprimorado
- **Sistema de Atualização do Programa:**
  - Novo ícone de notificação no canto superior direito da janela (ao lado do botão de tema), com indicador visual quando há nova versão no GitHub e painel com versão, novidades e botão **"Atualizar Agora"** (download com barra de progresso e reinício automático).
  - Nova opção em **Configurações → Geral** para pesquisar atualizações automaticamente. A pesquisa ocorre somente ao iniciar com a janela visível e a cada abertura do launcher; em segundo plano na bandeja o programa nunca pesquisa.
  - Atualização funciona tanto na versão **instalada** quanto na **portátil**, preservando as configurações do usuário.
- **Instalador Web Leve (`LuminiTray-Installer.exe`):** baixa a versão mais recente direto do GitHub, instala em `%LocalAppData%\Programs\LuminiTray`, cria atalhos (Área de Trabalho e Menu Iniciar) e registra o desinstalador no Windows.
- **Assinatura Digital:** executáveis publicados passam a ser assinados digitalmente com o nome **Lumini Tray**.
- **Downloads Identificados no GitHub:** a release agora publica `LuminiTray-Installer.exe` (instalador), `LuminiTray-Portable.exe` (portátil único) e `LuminiTray-Portable.zip` (portátil completo), com tabela de identificação nas notas.
- **Unidades de Medida na Overlay:** nova opção na aba **Overlay** para alternar a exibição de temperatura entre **°C (Celsius)**, **°F (Fahrenheit)** e **K (Kelvin)**, e de clock entre **Padrão**, **MHz** e **GHz**.

### 🛠️ Correções
- **Clock da Memória da GPU na Linha VRAM:** o item **Clock da memória** (`gpu_memclock`) foi movido do grupo/linha **GPU** para o grupo/linha **VRAM** da overlay.

## [0.28.1] - 2026-11-13

### 🛠️ Correções
- **Campo App Alvo & Escopo no Remapeador de Teclado:**
  - Corrigida sobreposição de `TxtRemapCaptureHint` sobre `PanelRemapTargetApp` na mesma célula do `Grid` em `MainWindow.xaml`, que impedia clicar na caixa `TxtRemapTargetApp`.
  - Habilitado **App alvo (opcional)** também para **Tecla Única** (além de **Atalho**), com botão **"🔍 Escolher..."** integrado ao seletor de aplicativos (`AppPickerWindow`) e botão rápido de limpeza.
  - Adicionada **Lista de Exclusão de Aplicativos** como complemento de controle do escopo (com seletor `AppPickerWindow`, entrada manual e chips removíveis), ignorando o remapeamento quando o aplicativo excluído estiver em foco.

## [0.28.0] - 2026-11-12

### 🚀 Adicionado & Aprimorado
- **Remapeador de Teclas e Atalhos:**
  - Novo painel na sub-aba **Teclado** para remapear teclas individuais (`Tecla → Tecla / Atalho / Desativada`) e atalhos (`Atalho → Atalho / Tecla / Desativado`), com suporte opcional a escopo por aplicativo (ex: `notepad.exe`).
  - Captura interativa via botão **"Digitar Tecla"** ou seleção manual por catálogo de teclas (`Virtual-Key`).
  - Motor global baseado em `WH_KEYBOARD_LL` + `SendInput` com proteção anti-loop e custo zero de CPU quando desativado ou sem regras ativas.
  - Regras persistidas em `AppSettings` (desativado por padrão).

## [0.27.1] - 2026-11-12

### 🛠️ Correções
- **Navegação da Sub-aba Dispositivos:**
  - Corrigido problema em que clicar na sub-aba **Dispositivos** redirecionava para a sub-aba **Teclado**.
- **Detecção de Armazenamento vs Celulares (MTP/WPD):**
  - Corrigida classificação de volumes internos (`SWD\WPDBUSENUM` de HDs e SSDs) para que não sejam listados incorretamente como celulares conectados via MTP.

## [0.27.0] - 2026-11-12

### 🚀 Adicionado & Aprimorado
- **Nova Sub-aba Dispositivos (em Periféricos):**
  - Listagem em tempo real de dispositivos conectados (pen drives, HDs/SSDs externos, cartões SD, celulares MTP/WPD e periféricos USB), com ações para abrir no Explorer e **Ejetar com Segurança**.
  - **Módulo Bluetooth:** listagem de dispositivos pareados, conectar/desconectar/desparear, sistema de **Favoritos** com auto-conexão quando o Bluetooth estiver ativo, e **Modo Manual** de conexão.
  - Integração automática de dispositivos com armazenamento detectado às ferramentas de análise de Saúde (SMART), Benchmark, Bad Blocks e Teste de Integridade.

## [0.26.0] - 2026-11-12

### 🚀 Adicionado & Aprimorado
- **Nova Aba Tweaks:**
  - Centralização em aba dedicada de atalhos rápidos de Áudio/Microfone, Supressor Neural, Overlay de Jogos, Mini Gerenciador de Tarefas e Wallpapers Animados.
  - Nova opção **Desativar Office Hotkeys** (`Ctrl+Shift+Alt+Win`) na seção de Tweaks de Sistema.

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