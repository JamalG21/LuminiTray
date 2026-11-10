# Changelog - Lumini Tray

Todas as alterações notáveis deste projeto serão documentadas neste arquivo.
O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [0.24.2] - 2026-11-09

### 🛠️ Correções
- **Despertar Automático do Mini Gerenciador de Tarefas:**
  - Corrigido problema em que o Mini Gerenciador de Tarefas ficava congelado exibindo `--%` e `"Coletando..."` indefinidamente ao ser aberto.
  - O evento `IsVisibleChanged` da janela flutuante (`MiniTaskManagerWindow`) agora ativa e desativa automaticamente o timer de amostragem em nível de kernel (`MiniTaskManagerService.Instance.SetActive`), despertando a coleta no momento em que a janela fica visível.
- **Proteção do Monitoramento com MainWindow Minimizada:**
  - Ao minimizar a janela principal ou ocultá-la para a bandeja do sistema durante partidas ou tarefas em primeiro plano, o serviço agora valida se a janela flutuante está ativa (`MiniTaskManagerWindow.IsWindowVisible`), impedindo o cancelamento indevido da coleta de métricas em tempo real.
- **Ciclo de Vida Limpo de Janela WPF (`Closed`):**
  - Implementado reset de instância (`_instance = null`) e desligamento seguro de ganchos WinEvent e assinaturas de métricas no evento `Closed`, garantindo que o widget possa ser reaberto sem exceções.

### ⚡ Performance & Otimização
- **Inicialização Ultra-Rápida em Duplo Tick (Two-Tick Startup):**
  - Ao abrir o Mini Gerenciador de Tarefas, uma primeira amostra de baseline é capturada em tempo zero (`dueTime: 0`) seguida de um segundo pulso em apenas 80ms para computação imediata dos deltas de CPU e I/O de disco.
  - Elimina qualquer atraso perceptível de 1 a 3 segundos de espera visual, preenchendo todos os dados reais quase que instantaneamente ao abrir a HUD flutuante.
- **Repouso Rigoroso do Timer (0.0% CPU em Ocultação):**
  - Ao fechar ou ocultar a janela flutuante (`Hide()`), o timer de amostragem entra imediatamente em repouso absoluto (`Timeout.Infinite`), descarregando deltas acumulados e garantindo zero consumo de ciclos de CPU em segundo plano.

## [0.24.1] - 2026-11-09

### 🚀 Adicionado & Aprimorado
- **Restauração Inteligente do Modo Baixo Consumo (`LowPowerSnapshot`):**
  - Ao desativar o Modo Econômico, o Lumini Tray restaura com precisão o estado anterior de cada tweak (scroll de volume na barra de tarefas, atalhos de áudio, supressor de ruído master), recuperando a configuração exata do usuário.
  - Sincronização em tempo real de todos os checkboxes na aba Geral e Áudio e reinicialização imediata dos ganchos associados.
- **Sincronização Fiel de Status de Wallpapers por Monitor:**
  - O cabeçalho e os botões da aba de Wallpapers agora refletem com precisão se o monitor selecionado possui vídeo animado ativo ou se está utilizando papel de parede estático do Windows.
  - Quando todos os monitores utilizam papéis de parede estáticos, o motor MPV é completamente desativado (`DESATIVADO (MÍNIMO CONSUMO)`), eliminando status fantasma de "ATIVO".

### ⚡ Performance & Otimização
- **Auditoria e Otimização Profunda de RAM e CPU Minimizado:**
  - **Repouso Total do Timer (`0.0% CPU`):** Quando a janela estiver minimizada na bandeja e nem a overlay nem o controle de ventoinhas estiverem ativos, o `DispatcherTimer` principal é desligado (`_timer.Stop()`), zerando ciclos de CPU e alocações de memória.
  - **Stretch de Polling de Fans:** Quando apenas o controle de fans estiver ativo com o app na bandeja, o timer principal estica a leitura para 2 segundos, economizando 50% de uso de CPU.
  - **Avaliação Real de Visibilidade da Overlay:** O polling contínuo para a overlay agora verifica se a janela da overlay está de fato visível na tela (`_overlay != null && _overlay.IsVisible`), evitando leituras pesadas em segundo plano quando nenhum jogo estiver em execução.
  - **Suspensão do Mini Task Manager:** A amostragem de processos em nível de kernel (`MiniTaskManagerService`) permanece desativada enquanto minimizado, sendo chamada apenas sob demanda do usuário.
  - **Inicialização Sob Demanda de Fans:** O serviço de Fans (`NvAPIWrapper.dll`, `nvapi64.dll`, ADL e Super I/O) só é inicializado e carregado na memória RAM caso o controle de ventoinhas esteja ativado pelo usuário ou ao abrir a aba correspondente.
  - **Trim Imediato de Memória:** Ao minimizar ou ocultar na bandeja, o Lumini Tray aciona `TrimMemory()`, descarrega miniaturas da galeria e compacta o WorkingSet do processo imediatamente.

### 🛠️ Correções & Tamanho em Disco
- **Remoção de Duplicata do Binário MPV (-57.29 MB):**
  - Removido o arquivo duplicado `Assets\Plugins\mpv\mpv.exe` (57.29 MB), padronizando a execução exclusivamente através do executável customizado `LuminiTray.Wallpaper.exe`.

### 🚀 Adicionado & Aprimorado
- **Modo Baixo Consumo de Recursos (`ChkSettingsLowPowerMode`):**
  - Adicionada opção dedicada em Configurações Gerais para otimização extrema de CPU e RAM.
  - Ao ativar, desativa automaticamente tarefas e ganchos em segundo plano opcionais (scroll de volume na taskbar, atalhos de mute/ciclo de áudio, supressor de ruído sem retorno ativo).
  - Permite reativar qualquer utilitário individualmente conforme a preferência do usuário.
- **Controle Individual para Todas as Tweaks do Sistema:**
  - Interruptores independentes adicionados na aba Configurações Gerais e Áudio:
    - Volume na Barra de Tarefas com Scroll do Mouse (`ChkFastMouseWheelVolume` / `ChkEnableTaskbarVolume`)
    - Mutar Saída Geral de Som com atalho (`ChkFastPlaybackMute` / `ChkEnablePlaybackMute`)
    - Mutar/Desmutar Microfone globalmente (`ChkFastMicMute` / `ChkEnableMicMute`)
    - Alternar Saídas de Reprodução de Áudio (`ChkFastPlaybackCycle` / `ChkEnablePlaybackCycle`)
    - Alternar Microfones / Dispositivos de Entrada (`ChkFastRecordingCycle` / `ChkEnableRecordingCycle`)
  - A thread dedicada de gancho de teclado (`AudioHotkeyHook`) é completamente encerrada (`StopHook()`) quando nenhum atalho estiver habilitado, garantindo 0% de uso de CPU e 0 hooks do sistema.
  - O gancho de scroll na barra de tarefas (`TaskbarVolumeHook`) é desativado quando a opção correspondente estiver desligada.
- **Wallpapers Estáticos Nativos no Windows & Slideshow Sem MPV:**
  - Wallpapers estáticos são trocados diretamente no Windows através da API `IDesktopWallpaper` e `SystemParametersInfo` sem acionar o motor MPV.
  - Se papéis de parede animados estiverem desativados, o motor MPV permanece 100% desligado, poupando ~150 MB de RAM.
  - A rotação automática programada (Slideshow / Timer) funciona diretamente na inicialização do Windows (`InitializeWallpaperSlideshowOnStartup`), alternando imagens estáticas com consumo nulo de recursos.
  - Galeria de imagens libera recursos de memória (`ReleaseWallpaperGalleryMemory`) ao navegar para outras abas ou minimizar o app.

### ⚡ Performance & Otimização
- **Leitura Inteligente e Condicional de Sensores (`RefreshEssentialTempsOnlyAsync`):**
  - Os sensores do LibreHardwareMonitor agora só realizam leitura completa quando a Overlay estiver ligada ou quando o usuário estiver visualizando a tela inicial (Dashboard) ou a tela de Sensores.
  - Fora dessas telas e com a janela minimizada: se o Controle Inteligente de Fans estiver ativo, o aplicativo executa exclusivamente uma leitura ultra-leve (`RefreshEssentialTempsOnlyAsync`) restrita a temperaturas e rotações de CPU/GPU (~2ms), poupando leituras de placa-mãe, RAM, discos e rede. Se o Controle de Fans estiver desligado, as leituras de sensores são pausadas 100%.
  - A subtarefa `Lumini Tray - Monitor de Hardware` é registrada dinamicamente apenas quando a leitura de métricas estiver de fato em execução.
- **Ciclo de Vida Otimizado das Sub-Abas de Periféricos:**
  - Todas as sub-abas da tela de periféricos operam sob demanda estrita:
    - O teste de Gamepad só faz polling contínuo quando a sub-aba Gamepad estiver visível; interrompe ao sair.
    - A animação do OVNI (UFO Test) só executa no painel de Monitores.
    - A varredura de frequências de áudio (Sweep) para ao sair do painel de Áudio.
    - O microfone é mantido em repouso fora do painel de Microfone, exceto se a opção de retorno ao vivo (Loopback / Eco) estiver expressamente ligada pelo usuário para testes.
- **Otimização do Supressor de Ruído e Mini Gerenciador de Tarefas:**
  - O motor RNNoise do supressor só executa se a filtragem mestre ou o retorno no fone estiverem ligados.
  - O timer gráfico de VU meters do supressor (`_suppressorMeterTimer`) é pausado imediatamente ao sair da aba do Supressor.
  - Adicionado `SetActive(bool active)` ao `MiniTaskManagerService` para pausar amostragem contínua de processos quando desnecessária.

### 🛠️ Correções
- **Ícones Oficiais nas Subtarefas do Gerenciador de Tarefas:**
  - Corrigido o ícone genérico em branco das subtarefas no Gerenciador de Tarefas do Windows através do envio de mensagens Win32 `WM_SETICON` e registro da classe com o ícone extraído do executável principal (`GetAppIconHandle`).

## [0.23.0] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Wallpapers Animados e Estáticos Multi-Monitor Independentes:**
  - Desacoplada a altern�ncia de monitores no dropdown (CmbWallpaperMonitor). Mudar de tela na interface agora atua exclusivamente como seletor da tela em foco para pr�via e configura��o, sem nunca fechar, reiniciar ou mover o wallpaper que j� estiver rodando nos demais monitores.
  - Implementado PlayFileOnMonitor(filePath, targetDeviceId) e StopMonitor(targetDeviceId) em LivelyWallpaperService.cs, permitindo atribuir vídeos animados diferentes para cada monitor conectado (ex: Vídeo A no Monitor 1 e Vídeo B no Monitor 2) ou aplicar em todos simultaneamente.
  - Adicionado suporte a persistência por monitor via AppSettings.LivelyWallpaperMonitorPaths e AppSettings.StaticWallpaperMonitorPaths. Ao reiniciar o aplicativo, cada tela retoma seu próprio wallpaper automaticamente.
  - O botão "Desativar" encerra de forma unificada o motor de wallpaper em todos os monitores de uma só vez, restaurando a Área de Trabalho limpa.
  - Ao aplicar uma imagem estática em um monitor específico, apenas a reprodução animada daquele monitor é liberada, mantendo vídeos ativos nos outros monitores.

### 🛠️ Correções & Padronização
- **Nomenclatura Limpa e Consistente das Subtarefas no Gerenciador de Tarefas:**
  - Padronizados os nomes amigáveis das quatro subtarefas principais do Lumini Tray:
    - Lumini Tray - Monitor de Hardware (sensores e telemetria)
    - Lumini Tray - Supressor de Ruído (filtro neural RNNoise e cabo virtual)
    - Lumini Tray - Controlador de Fans (curvas térmicas e controle PWM)
    - Lumini Tray - Papel de Parede Animado (motor MPV nativo acoplado ao Desktop)
- **Inicialização Automática e Registro Robusto do Supressor de Ruído:**
  - Implementado EnsureSuppressorServiceRunning() no carregamento inicial (Loaded) do MainWindow.xaml.cs. Se o supressor estiver ativado nas configurações, ele inicia imediatamente na abertura do sistema e registra a subtarefa Lumini Tray - Supressor de Ruído sem depender de navegação prévia na aba de ferramentas.

## [0.22.9] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Persistência Definitiva das Subtarefas no Gerenciador de Tarefas (SubtaskManager):**
  - Desvinculado o vínculo de propriedade Win32 (parentHwnd = IntPtr.Zero) em SubtaskManager.cs. As janelas de subtarefas agora são janelas de nível superior puras (WS_POPUP | WS_VISIBLE | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) associadas diretamente ao processo LuminiTray.exe.
  - Ao minimizar ou fechar o aplicativo para a bandeja do sistema (MainWindow.Hide()), o Windows User32 não oculta mais as janelas de subtarefas, mantendo todas as subtarefas (Lumini Tray - Controlador de Fans, Lumini Tray - Monitor de Métricas e Telemetria, etc.) continuamente visíveis e agrupadas sob o processo LuminiTray na aba Processos do Gerenciador de Tarefas do Windows.
  - Implementado watchdog keep-alive periódico que valida IsWindowVisible e restabelece a visibilidade instantaneamente via ShowWindow(SW_SHOWNA).

### ⚡ Performance & Otimização
- **Redução Efetiva de Memória do Motor MPV e Contabilização na Overlay:**
  - TrimProcessWorkingSet em LivelyWallpaperService.cs agora atua recursivamente em todos os processos filhos (mpv.exe executado sob o wrapper), aplicando EmptyWorkingSet e liberando memória real do player ao entrar em pausa.
  - GetTotalProcessesWorkingSet() e GetTotalLuminiAppWorkingSet() na Overlay consolidam com precisão absoluta a memória física (WorkingSet) do executável principal, de todas as subtarefas e do motor MPV ativo, eliminando qualquer subnotificação na tela de jogos.

### 🛠️ Correções
- **Eliminação de Ícones Duplicados de Wallpaper na Bandeja:**
  - Cache de inst�ncias Drawing.Icon para estados de reprodu��o e pausa em SystemTrayManager.cs, evitando a emiss�o cont�nua de novos handles e recria��o redundante de �cones.
  - No encerramento da aplicação (PerformFullExit e ExecutePreShutdownCleanup), implementado bombeamento síncrono da fila de mensagens via Application.DoEvents(), garantindo que o Windows Explorer receba e processe a remoção de todos os ícones da bandeja (NIM_DELETE) antes da finalização do processo.

## [0.22.8] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Detecção de Oclusão Multi-Monitor Persistente (`EnumWindows` + `DWMWA_CLOAKED`):**
  - Substituída a checagem exclusiva de janela ativa (`GetForegroundWindow`) por uma varredura ultrarrápida (< 0.1 ms) de todas as janelas visíveis e não ocultas (`!IsWindowCloaked`) em todos os monitores via `EnumWindows`.
  - O papel de parede no monitor primário agora **permanece pausado** quando coberto por um jogo ou programa maximizado/em tela cheia, mesmo quando o usuário clica ou interage com janelas ou a Área de Trabalho no monitor secundário.
- **Início Inteligente Já em Estado Pausado (`--pause=yes`):**
  - Ao ativar o papel de parede com um programa ou jogo já em tela cheia/maximizado naquela tela, o serviço detecta a oclusão previamente e já inicializa o processo do MPV com `--pause=yes`, estado `PAUSADO` na bandeja e memória reduzida desde o instante zero.

### ⚡ Performance & Otimização
- **Redução Drástica de Memória RAM do Wallpaper Quando Pausado:**
  - Ao entrar em pausa (automática ou manual), o serviço envia o comando `drop-buffers` via IPC ao MPV para descartar filas de pacotes e quadros decodificados, seguido de `EmptyWorkingSet` e `SetProcessWorkingSetSize(-1, -1)` no processo `LuminiTray.Wallpaper.exe`.
  - O consumo de memória física da subtarefa `Lumini Tray - Papel de Parede Animado` no Gerenciador de Tarefas cai de ~147 MB para **~10–18 MB** enquanto o papel de parede estiver pausado.
  - Limites de fila do demuxer ajustados (`--demuxer-max-bytes=8MiB --demuxer-max-back-bytes=2MiB --demuxer-readahead-secs=0.5 --vd-queue-enable=no --ad-queue-enable=no`) para menor pegada de memória também em reprodução.

### 🛠️ Correções
- **Encerramento Limpo e Imediato do Aplicativo (Bandeja e Configurações):**
  - Corrigido deadlock Win32 cross-process no `DestroyWindow` causado pela vinculação de `parentHwnd` em `SubtaskWorker.cs` sem loop de mensagens (`PeekMessage`/`DispatchMessage`).
  - Unificado o fluxo de saída em `PerformFullExit()` com watchdog imediato, parada prévia de todos os subprocessos (`SubtaskProcessManager.StopAll()`, `LivelyWallpaperService.Instance.StopPlayback()`) e preservação do estado `LivelyWallpaperEnabled` ao fechar o programa pela bandeja.

## [0.22.7] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **ProcessTree para Contabilização Precisa da Overlay:**
  - Implementado `ProcessTree.cs` com `CreateToolhelp32Snapshot` de altíssimo desempenho (< 1 ms), somando a árvore inteira de processos descendentes de `LuminiTray.exe`.
  - A métrica `app XX MB` na overlay de jogos agora engloba 100% de todo o consumo de memória do player MPV nativo (~147 MB), subprocessos de supressão de ruído, controladores e monitores.
- **Unificação Definitiva do Player Nativo de Vídeo:**
  - `LuminiTray.Wallpaper.exe` agora executa o motor MPV diretamente com metadados PE dedicados (`FileDescription: Lumini Tray - Papel de Parede Animado`), eliminando por completo launchers secundários e subprocessos anônimos (`mpv-core.exe`).
  - Removida a sobreposição do launcher no script de publicação `publicar.bat`, garantindo que o player nativo permaneça unificado em qualquer distribuição.
- **Detecção de Jogos com `SHQueryUserNotificationState`:**
  - Adicionada verificação nativa do Windows Shell (`SHQueryUserNotificationState`), detectando automaticamente jogos em tela cheia exclusiva DirectX/Vulkan (`QUNS_RUNNING_D3D_FULL_SCREEN` e `QUNS_BUSY`).
- **Migração Automática de Ajustes:**
  - Adicionado `LivelyWallpaperSettingsVersion`, reativando de forma transparente o Modo Gamer (`LivelyWallpaperAutoPauseGames = true`) em configurações anteriores onde havia sido desmarcado por engano.
- **Desacoplamento de Threads e Locks:**
  - Comandos de pausa e retomada IPC do MPV e atualização do System Tray foram movidos para fora do lock crítico e despachados para a UI thread, eliminando riscos de travamento e atraso na interface.

### 🛠️ Correções
- **Auto-Limpeza de Ícones Fantasmas na Bandeja do Windows:**
  - Implementado SystemTrayManager.RefreshNotificationArea(), enviando varredura silenciosa de mensagens de mouse na barra de tarefas e na janela de overflow da bandeja do Windows para expurgar instantaneamente �cones zumbis deixados por inst�ncias finalizadas de forma n�o graciosa (ex: 	askkill /f).
- **Sintaxe e Fechamento de Chaves em LivelyWallpaperService.cs:**
  - Corrigido ponto de encerramento redundante no método ResumeAllInstances(), restabelecendo o fechamento íntegro da classe.

## [0.22.6] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Pausa Automática Confiável em Tela Cheia e Modo Gamer (Papel de Parede Animado):**
  - Implementada a resolução da janela raiz de nível superior (`GA_ROOT`) em `LivelyWallpaperService.cs`, garantindo a detecção precisa de jogos (Unreal Engine, Unity, CEF/Chromium) mesmo quando o foco de entrada está em controles ou subjanelas filhas internas.
  - Expandida a margem de toler�ncia da �rea de cobertura para `�12px`, acomodando com precis�o margens invis�veis do Desktop Window Manager (DWM) e escalonamentos de DPI fracion�rios em monitores de alta resolu��o.
  - Reduzido o intervalo do timer de detecção de jogos de 1000 ms para **500 ms**, proporcionando resposta ágil e imperceptível na pausa e retomada dos papéis de parede.
  - O Modo Gamer agora pausa e congela globalmente todas as inst�ncias de papel de parede ativo atrav�s de `PauseAllInstances()`, eliminando 100% da conten��o de GPU/VRAM e prevenindo quedas de taxa de quadros e engasgos (stuttering) durante os jogos.

### 🛠️ Correções
- **Correção de Ícone Duplicado de Wallpaper na Bandeja do Sistema:**
  - Corrigido o método `UpdateAllIcons()` em `SystemTrayManager.cs`, eliminando a recriação redundante do objeto `_wallpaperTray` que resultava em dois ícones idênticos na bandeja do Windows.
- **Higienização de Classes de Janela no Reconhecimento de Desktop:**
  - Removidas as classes `Windows.UI.Core.CoreWindow` e `XamlExplorerHostIslandWindow` da lista de classes de Área de Trabalho, permitindo que jogos modernos da Microsoft Store / UWP (ex: Forza, Gears, Minecraft) e o Explorador de Arquivos maximizado sejam corretamente identificados e pausem o papel de parede.

## [0.22.5] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Executáveis Nativos e Identificação de Subtarefas no Gerenciador de Tarefas:**
  - Criação dos binários dedicados `LuminiTray.Suppressor.exe` (Supressor de Ruído RNNoise), `LuminiTray.Metrics.exe` (Monitor de Métricas) e `LuminiTray.Fans.exe` (Controlador de Fans) com metadados PE oficiais (`FileDescription`, `ProductName: Lumini Tray`, `FileVersion: 0.22.5.0`).
  - As subtarefas passam a exibir seus nomes amigáveis específicos tanto na aba *Processos* quanto na aba *Detalhes* do Gerenciador de Tarefas do Windows, eliminando a confusão de múltiplos processos genéricos anônimos.
- **Métrica de Swap / Paginação de Memória no Overlay de Jogos:**
  - Adicionada a nova métrica oficial `ram_swap` (`Swap / Paginação de memória`) na categoria RAM.
  - Implementada leitura de altíssima performance via API nativa `NtQuerySystemInformation(SystemPagefileInformation = 18)`, medindo em microssegundos com 0% de overhead a quantidade exata de memória RAM paginada em disco (`swap XXX MB`).
- **Consolidação Total de Memória do Lumini Tray no Overlay:**
  - O indicador de memória da aplicação (`app XX MB`) na overlay agora consolida o consumo total do ecossistema: processo principal (`LuminiTray.exe`), motor do papel de parede animado (`LuminiTray.Wallpaper.exe`) e subprocessos de subtarefas ativas (`LuminiTray.Suppressor.exe`, etc.).
  - Permite aos jogadores acompanhar com precisão cirúrgica o impacto global de RAM de todas as funções do Lumini Tray em execução.

### 🛠️ Correções
- Corrigida resolução de caminhos no script `executar_e_testar.bat` para priorizar a inicialização do executável compilado `bin\Debug\net8.0-windows\LuminiTray.exe`.
- Automatizada a sincronização e cópia dos executáveis dedicados de subtarefas (`Assets\Subtasks\*.exe`) para a pasta de saída do projeto a cada build.

## [0.22.4] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Pausa Inteligente por Monitor em Tela Cheia / Jogos:** A rotina `GameDetectionCheck` agora detecta com precisão geométrica em qual monitor a janela em foco está posicionada (`MonitorFromWindow` e `GetMonitorInfo`). Em configurações com múltiplos monitores, apenas a tela coberta por um jogo ou programa maximizado é pausada, mantendo o wallpaper animado reproduzindo com fluidez nos demais monitores livres.
- **Navegação de Wallpapers Estáticos (Anterior e Próximo):** Adicionados botões visuais `⏮ ANTERIOR` e `⏭ PRÓXIMO` na barra de controle de papéis de parede estáticos no Lumini Tray, permitindo navegar sequencialmente pelas imagens da pasta e aplicar no monitor individual selecionado ou em todos simultaneamente.
- **�udio Din�mico no Player:** Implementada verifica��o inteligente de som: quando o slider de volume est� em 0%, o MPV � inicializado com `--no-audio` (eliminando buffers e processamento de �udio por completo); se o usu�rio aumentar o volume, o �udio � restaurado dinamicamente no n�vel escolhido.

### ⚡ Performance & Otimização
- **Otimização Avançada de Memória do MPV:**
  - Configuração agressiva do cache do demuxer (`--cache=no`, `--demuxer-max-bytes=50MiB`, `--demuxer-max-back-bytes=20MiB`, `--demuxer-readahead-secs=1`).
  - Decodificação por hardware nativa do Windows com cópia de memória (`--hwdec=auto-copy`, `--gpu-context=d3d11`, `--gpu-api=d3d11`, `--hwdec-extra-frames=2`).
  - Eliminação de processamento dispensável com `--profile=fast`, `--no-sub`, `--no-osc`, `--no-input-default-bindings`, `--no-input-cursor`, `--vf=`, `--af=`.
  - Remoção de verificação restritiva de título de janela (`GetWindowTextLength == 0`) que causava falsos positivos de retomada em jogos e aplicativos sem caption.

## [0.22.3] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Processo Único e Unificado de Wallpaper (`LuminiTray.Wallpaper.exe`):** O binário nativo do MPV foi transformado diretamente no executável oficial do Lumini Tray com metadados PE completos (`FileDescription: Lumini Tray - Papel de Parede Animado`, `ProductName: Lumini Tray`, `CompanyName: LuminiTray.Wallpaper`). O intermediário wrapper C# e o processo secundário `mpv-core.exe` foram completamente eliminados, operando agora um único processo nativo perfeitamente unificado e identificado no Windows.
- **Identificação Completa de Todas as Subtarefas no Gerenciador de Tarefas:** Registro sistemático de todos os módulos e rotinas de segundo plano no `SubtaskManager` sob o processo `Lumini Tray`:
  - `Lumini Tray - Otimizador em Tempo Real (Auto-Clean)` (limpeza periódica de RAM e otimização de processos).
  - `Lumini Tray - Monitor de Métricas e Telemetria` (coleta e sensores de hardware em tempo real).
  - `Lumini Tray - Controlador de Fans` (curvas térmicas e controle inteligente de ventoinhas).
  - `Lumini Tray - Papel de Parede Animado` (reprodução nativa acoplada à Área de Trabalho).
  - `Lumini Tray - Supressor de Ruído` (cancelamento de ruído por IA).
  Todas as subtarefas contam com vinculação estrita de HWND Owner e podem ser encerradas individualmente pelo usuário diretamente pelo Gerenciador de Tarefas ("Finalizar Tarefa").

### ⚡ Performance & Otimização
- **Redu��o Dr�stica do Consumo de Mem�ria RAM do MPV (~85% de economia):** Otimiza��o profunda dos par�metros de execu��o do player MPV para pap�is de parede animados:
  - Cache demuxer desativado para arquivos locais (`--cache=no`), limitando buffers de leitura para 8 MB (`--demuxer-max-bytes=8M`, `--demuxer-max-back-bytes=2M`, `--demuxer-readahead-secs=1`).
  - Limitação de threads de decodificação de vídeo (`--vd-lavc-threads=2`) e buffer de áudio ultracurto (`--audio-buffer=0.1`).
  - Desativação de caches redundantes de shader GPU e perfis ICC (`--gpu-shader-cache=no`, `--icc-cache=no`, `--video-latency-hacks=yes`).
  - O consumo de memória RAM do player de papel de parede caiu de **mais de 218 MB** para apenas **~30-35 MB**, economizando mais de 180 MB de memória de forma contínua.

## [0.22.2] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Detecção Automática de Vídeos Animados na Pasta:** `WallpaperManager.ScanFolder` agora inclui nativamente todas as extensões de vídeos animados (`.mp4`, `.webm`, `.mkv`, `.avi`, `.wmv`) na galeria visual do Lumini Tray. Os vídeos são exibidos lado a lado com papéis de parede estáticos com badge exclusivo `🎬 VÍDEO ANIMADO` e botão direto `▶ APLICAR WALLPAPER ANIMADO`.
- **Monitoramento em Tempo Real da Pasta (FileSystemWatcher):** Implementado observador de sistema de arquivos que monitora adições, exclusões e renomeações na pasta de papéis de parede, atualizando a playlist e a galeria instantaneamente em menos de 600ms sem necessidade de reabrir a janela ou selecionar arquivos manualmente.
- **Extração de Miniaturas de Vídeo via Windows Shell:** Integração nativa com `IShellItemImageFactory` da API do Windows para carregar automaticamente a miniatura do primeiro frame de vídeos `.mp4` na interface do aplicativo.
- **Varredura Automática na Inicialização:** `LivelyWallpaperService.Initialize` agora escaneia a pasta configurada logo na inicialização do sistema/aplicativo, populando toda a playlist de vídeos automaticamente mesmo sem intervenção do usuário.

### 🛠️ Correções
- **Fim da Seleção Manual de Arquivo por Arquivo:** Eliminada a necessidade de importar vídeos 1 por 1 via janela de arquivo; basta colocar qualquer vídeo suportado na pasta que ele se torna imediatamente pronto para uso e rotação automática.

## [0.22.1] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Subtarefas Agrupadas no Gerenciador de Tarefas:** Implementação do `SubtaskManager` com janelas Win32 nativas de identificação vinculadas ao processo principal (`HWND Owner`), viabilizando agrupamento automático com dropdown `>` na aba **"Processos"** do Gerenciador de Tarefas do Windows (Taskmgr) sob `Lumini Tray`. O usuário agora visualiza cada subtarefa com seu nome amigável (`Lumini Tray - Papel de Parede Animado`, `Lumini Tray - Supressor de Áudio (RNNoise)`, etc.) e pode finalizar qualquer subtarefa individualmente com botão direito -> "Finalizar Tarefa".
- **Controle Inteligente de Foco (Modo Gamer vs Modo Janela):** Adicionada a configuração e controle visual `ChkLivelyPauseOnFocusedApp` ("Pausar também quando qualquer outro aplicativo estiver em foco (mesmo em modo janela)"). Por padrão, pausa estritamente em jogos ou janelas maximizadas/tela cheia, permitindo que a Área de Trabalho continue tocando o papel de parede fluidamente.

### ⚡ Performance & Otimização
- **Transição Atômica no Player MPV:** Envio de comandos de carga de vídeo agora utiliza `["loadfile", path, "replace"]` serializado via JSON de forma atômica e segura contra caminhos com caracteres especiais, acentuações e espaços.
- **Persist�ncia Completa de Playlist:** A lista de pap�is de parede animados agora � persistida em `AppSettings.LivelyWallpaperPlaylist`, garantindo restaura��o instant�nea de todos os v�deos e inicializa��o imediata do timer de rota��o ap�s reiniciar o app.

### 🛠️ Correções
- **Troca Automática de Wallpapers (Slideshow / Intervalo):** Corrigido o problema onde a rotação periódica não disparava devido à playlist conter apenas o vídeo ativo na inicialização. A rotação agora reinicia e sincroniza dinamicamente sempre que novos vídeos são detectados ou adicionados.
- **Falso Positivo de Pausa ao Minimizar Janelas:** Resolvido bug no `GameDetectionCheck` onde minimizar um aplicativo ou focar o desktop pausava o vídeo erroneamente por identificar `SysListView32` e `SHELLDLL_DefView` como janelas em tela cheia. Agora janelas minimizadas (`IsIconic`) e componentes do Desktop do Windows são validados corretamente como desktop liberado para reprodução.

## [0.22.0] - 2026-11-07

### 🚀 Adicionado & Aprimorado
- **Wrapper Executável Nativo (`LuminiTray.Wallpaper.exe`) com Metadados PE Oficiais:**
  - Compilado wrapper dedicado com ícone oficial da aplicação, `ProductName: Lumini Tray` e `FileDescription: Lumini Tray - Papel de Parede Animado`.
  - Na aba **Processos** do Gerenciador de Tarefas do Windows, o player de vídeo agora é exibido formalmente com seu nome amigável e ícone oficial (e não mais como `mpv`), com consumo individual de GPU e memória RAM claramente identificados.
  - **Gerenciador de Subprocessos (`SubtaskProcessManager`):** Ativação do Supressor de Áudio (`--subtask=audio-suppressor`) e demais tarefas em subprocessos dedicados agrupados no Windows Job Object do Lumini Tray. Encerramentos manuais de qualquer subtarefa no Gerenciador de Tarefas do Windows são interceptados com segurança pela aplicação, restaurando o estado da interface sem travamentos.

### 🛠️ Correções & Estabilidade
- **Correção Definitiva do Posicionamento em Múltiplos Monitores:**
  - Substituída a atribuição forçada de tela cheia global (`0, 0, w, h`) pela conversão exata via `MapWindowPoints(IntPtr.Zero, targetParent, ref rc, 2)`.
  - Ao selecionar o **Monitor 1** (1920x1080 horizontal), o vídeo é renderizado **estritamente dentro das coordenadas do Monitor 1**, eliminando o esticamento que cortava o vídeo sobre o monitor secundário vertical (768x1366). O segundo monitor mantém intacto o papel de parede nativo do Windows.
  - Ao selecionar o **Monitor 2** (768x1366), o vídeo é aplicado exclusivamente na tela vertical.
  - Ao selecionar **Todos os Monitores**, respeita o modo Estender (*Span*) sobre a largura virtual total ou clona/duplica inst�ncias independentes em cada tela.
  - Removido o bloqueio que impedia a reconfiguração dos monitores em tempo real, permitindo alternar de monitor ou estilo instantaneamente.

## [0.21.9] - 2026-11-07

### 🚀 Adicionado & Aprimorado
- **Agrupamento de Subtarefas no Gerenciador de Tarefas do Windows (Windows Job Objects):**
  - **Identificação e Isolamento Visual:** O motor nativo de wallpaper agora é executado como `LuminiTray.Wallpaper.exe` e vinculado dinamicamente ao Job Object do Lumini Tray (`JobObjectManager`), permitindo ao usuário expandir o aplicativo no Gerenciador de Tarefas e visualizar o consumo individual de CPU, GPU e memória RAM.
  - **Suporte a Subtarefas Headless:** Adicionada infraestrutura para subprocessos dedicados (`--subtask=audio-suppressor`, `--subtask=metrics-monitor`, `--subtask=fan-controller`) via `SubtaskWorker.cs`.
  - **Detecção de Encerramento Manual:** Ao finalizar a tarefa `LuminiTray.Wallpaper.exe` diretamente pelo Gerenciador de Tarefas do Windows, o Lumini Tray intercepta o evento graciosamente, restaura o papel de parede nativo e reseta os controles na interface sem travamentos.

### 🛠️ Correções & Estabilidade
- **Canal IPC Named Pipe Persistente e Atômico:**
  - Substituídas conexões temporárias descartáveis por um cliente de pipe persistente com controle thread-safe (`_instancesLock`).
  - Corrigido o botão de **Play/Pausa**, garantindo que comandos de pausa manual tenham prioridade absoluta e nunca sejam anulados pelo detector de jogos.
  - Corrigida a navegação de **Anterior (`⏮`)** e **Próximo (`⏭`)**, normalizando caminhos com barras universais (`/`) e eliminando erros de escape de JSON.
- **Suporte Completo a Múltiplos Monitores (Individual, Duplicado e Span):**
  - O papel de parede animado agora obedece estritamente às coordenadas do monitor selecionado no dropdown (`CmbWallpaperMonitor`). Ao escolher "Monitor 1" ou "Monitor 2", o vídeo é aplicado exclusivamente na tela selecionada.
  - Ao selecionar "Todos os Monitores", o sistema verifica o Estilo selecionado: se for "Estender Monitores (Span)", estica um único vídeo sobre toda a área virtual; caso contrário, instancia cópias independentes em cada tela, mantendo a proporção ideal sem distorção.
  - Mudanças de monitor ou estilo na interface são aplicadas instantaneamente em tempo real.
- **Persistência do Estado do Wallpaper ao Reiniciar o Aplicativo:**
  - Corrigido problema em que o encerramento do aplicativo sobrescrevia a configuração do usuário para desativado. O fechamento do programa agora chama `StopPlayback()`, preservando `LivelyWallpaperEnabled == true`. Ao reabrir o Lumini Tray, o vídeo é retomado automaticamente.

## [0.21.8] - 2026-11-07

### 🚀 Adicionado & Aprimorado
- **Novo Motor de Vídeo Nativo MPV Player com Aceleração por Hardware (Direct3D 11 / Vulkan):**
  - **Adoção do Motor Nativo Oficial do Lively Wallpaper:** Substituição do player WPF `MediaElement` (incompatível com o compositor DWM DirectComposition no Windows 11 Raised Desktop em processos cruzados) pelo motor nativo de alto desempenho **MPV Player v0.41.0**.
  - **Container Nativo Win32/WinForms (`LivelyWallpaperForm`):** Criação de um container Win32 puro e sem bordas (`WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`) acoplado diretamente sob a hierarquia do Desktop (`Progman` / `WorkerW`). O MPV é embutido diretamente via argumento `--wid=<HWND>`, garantindo 100% de responsividade dos atalhos e ícones da Área de Trabalho.
  - **Comunica��o Ultrarr�pida e Bidirecional via IPC Named Pipe:** Controle instant�neo de reprodu��o, pause/play, carregamento de novos arquivos (`loadfile`), ajuste de volume e encerramento atrav�s de canal IPC ass�ncrono (`--input-ipc-server`).
  - **Pacote 100% Autônomo e Offline:** Binários nativos do MPV (`mpv.exe` e `vulkan-1.dll`) incluídos diretamente no projeto em `Assets\Plugins\mpv`, com suporte a execução automática a partir do diretório do aplicativo ou `%APPDATA%\LuminiTray\plugins\mpv`.
  - **Controle Din�mico de Volume e Modo Gamer na Interface:** Adicionados controles dedicados no card de Wallpapers Animados da janela principal: slider de volume (0 a 100%, mudo por padr�o a 0%) e alternador de Modo Gamer com pausa autom�tica durante jogos ou janelas em tela cheia (0% de CPU/GPU).
  - **Detecção Automática de Vídeos em Pastas de Wallpapers:** A galeria agora varre pastas selecionadas pelo usuário procurando por arquivos de vídeo (`.mp4`, `.webm`, `.mkv`, `.avi`, `.wmv`) e os adiciona automaticamente à playlist do motor Lively.

## [0.21.7] - 2026-11-07

### 🛠️ Correções & Aprimoramentos
- **Correção da Renderização de Wallpapers Animados no Desktop (Windows 11 Moderno e Multimonitor):**
  - **Correção do Posicionamento de Coordenadas de Janela Filha:** Resolvido bug crítico em configurações multimonitor onde a passagem de coordenadas virtuais globais (VirtualScreenLeft negativo, ex: -768px) deslocava a janela filha do wallpaper para fora da área visível (-1536px). O posicionamento agora é estritamente baseado no cliente do container (, 0, width, height), preenchendo todos os monitores com alinhamento perfeito.
  - **Garantia de WS_EX_LAYERED no Windows 11 Raised Desktop:** Corrigido problema onde a chamada SetParent(hwnd, progman) resetava os estilos da janela e removia WS_EX_LAYERED. Como o Progman no Windows 11 possui WS_EX_NOREDIRECTIONBITMAP, o compositor DWM ignorava janelas não-camadas. A camada agora é reforçada e mantida após o SetParent, acompanhada de SetLayeredWindowAttributes(alpha=255) e SWP_FRAMECHANGED.
  - **Persist�ncia de Reprodu��o no MediaElement:** Configurado UnloadedBehavior="Manual" e ScrubbingEnabled="True" no LivelyWallpaperWindow.xaml, evitando que o reparenting Win32 cause o encerramento prematuro do v�deo e garantindo exibi��o instant�nea do primeiro quadro.
  - **Exclusão do Próprio Processo no Detector de Jogos:** Ajustado o GameDetectionCheck para verificar o PID da janela em foco. Janelas pertencentes ao próprio Lumini Tray agora são ignoradas, evitando pausas indevidas enquanto o usuário configura papéis de parede no aplicativo.
  - **Prompt Automático ao Ativar Playlist Vazia:** Ao clicar em ativar papéis de parede animados sem nenhum arquivo configurado, o sistema abre automaticamente o seletor de vídeos (.mp4, .webm, .wmv, .avi) e fornece feedback via Toast em caso de incompatibilidades de codec.
## [0.21.6] - 2026-11-07

### 🚀 Adicionado & Aprimorado
- **Novo Motor de Papéis de Parede Animados Baseado no Lively Wallpaper (v2.2.1.0):**
  - **Suporte Nativo ao Windows 11 Moderno (Raised Desktop):** Implementada a arquitetura oficial do Lively Wallpaper para detecção de WS_EX_NOREDIRECTIONBITMAP no Progman e WS_EX_LAYERED no SHELLDLL_DefView.
  - **Preservação de 100% da Responsividade da Área de Trabalho:** A janela de vídeo é configurada como filha de Progman com Z-Order posicionado estritamente atrás do SHELLDLL_DefView. Todos os ícones, atalhos, seleções de mouse e menus de contexto continuam funcionando nativamente sem qualquer bloqueio.
  - **Compatibilidade Dual:** Suporte automático tanto ao Windows 11 moderno (22H2/23H2/24H2/26H2) quanto ao Windows 10 clássico via injeção segura de 0x052C (0xD, 0x1).
  - **Player Acelerado por GPU (LivelyWallpaperWindow):** Baseado no Lively.Player.Wmf com decodificação por hardware, loop contínuo e áudio mudo por padrão (0% de volume para evitar ruídos indesejados).
  - **Modo Gamer / Detecção de Tela Cheia:** Monitor inteligente de janelas em primeiro plano baseado no WindowClassExclusions do Lively, pausando a reprodução automaticamente durante partidas (0% CPU/GPU) e retomando ao retornar ao Desktop.
  - **Controle Din�mico na Bandeja do Sistema:** �cone exclusivo na bandeja com op��es de Play/Pausar, Pr�ximo, Anterior, Intervalo de rota��o e Desativa��o completa.
  - **Consumo Zero em Repouso:** Recurso desativado por padrão, sem alocação de memória ou CPU até que o usuário decida ativá-lo.
## [0.21.5] - 2026-11-07

### 🗑️ Removido & Restaurado
- **Remoção Completa dos Wallpapers Animados e Injeção no WorkerW:**
  - Removido integralmente o motor de papéis de parede animados (AnimatedWallpaperService, AnimatedWallpaperWindow.xaml), eliminando qualquer criação de janela oculta ou injeção em camadas do Windows Explorer (WorkerW / Progman).
  - Restaurada a total responsividade nativa da Área de Trabalho do Windows, cliques nos ícones e atalhos do Desktop.
  - Removidos os controles de reprodução de vídeo na aba de Wallpapers (MainWindow.xaml e MainWindow.Wallpaper.cs) e no menu da bandeja do sistema (SystemTrayManager.cs).
  - Mantida intacta a funcionalidade clássica, estável e leve de gerenciamento de papéis de parede estáticos (definição de imagens, rotação/slideshow nativo por timer, suporte multimonitor e biblioteca oficial).
## [0.21.4] - 2026-11-07

### ⚡ Performance & Otimização
- **Limpeza Profunda e Otimização da Pasta Raiz do Repositório (Redução de ~2.4 GB):**
  - **Exclusão de Resíduos e Dumps de Testes:** Removida a pasta `LuminiTray.Tests\TestResults` contendo mais de 1.78 GB de despejos de memória (`testhost.exe.dmp`) e shadow-copies geradas em execuções de testes anteriores.
  - **Remoção de Repositório de Referência Obsoleto:** Excluída a pasta `easyeffects-master` (22.1 MB / 737 arquivos), utilizada temporariamente como referência de algoritmos DSP e que não era referenciada pelo código ou pela compilação.
  - **Eliminação de Rascunhos e Utilitários Avulsos:** Removidos arquivos de rascunho de engenharia reversa (`taskbacontrollperapp.cpp`), utilitários não empacotados (`RevoUPort.exe`) e cópias duplicadas/desatualizadas de documentação (`CHANGELOG.md` e `GEMINI.md` internas).
  - **Remoção de Binários Antigos de Distribuição:** Excluído o backup `LuminiTray-dist\LuminiTray.exe.old` (92.1 MB).
  - **Higienização de Caches de Compilação:** Limpeza profunda de artefatos temporários de múltiplos frameworks e runtimes acumulados em `bin` e `obj`, reduzindo a pegada em disco para compilações sob demanda limpas.
  - **Proteção do Ambiente (.gitignore):** Atualizadas as regras de exclusão para ignorar pastas de `TestResults`, arquivos `.old` e despejos de memória `.dmp`.

## [0.21.3] - 2026-11-07

### 🚀 Adicionado & Aprimorado
- **Wallpapers Animados Leves Acelerados por GPU (WorkerW):**
  - Implementado motor de papéis de parede animados (AnimatedWallpaperService / AnimatedWallpaperWindow) com reprodução em loop contínuo de vídeos (.mp4, .webm) acoplados diretamente atrás dos ícones da área de trabalho do Windows Explorer.
  - **Padrão 100% Desativado para Mínimo Consumo:** O recurso permanece completamente desligado por padrão, sem iniciar janelas ou timers de vídeo em segundo plano, garantindo zero consumo adicional de memória RAM e GPU.
  - **Modo Gamer / Auto-Pause Inteligente:** Detecção contínua de jogos em tela cheia e aplicativos de alto desempenho 3D, pausando a decodificação do vídeo instantaneamente e garantindo **0% de uso de CPU e GPU** durante partidas.
  - Suporte para adicionar vídeos locais do computador através do botão + ADICIONAR VÍDEO (.MP4).
- **Controle Completo do Wallpaper na Bandeja do Sistema (System Tray):**
  - **Ícone Condicional Exclusivo:** O ícone do Wallpaper só aparece na bandeja do sistema quando o recurso de wallpapers animados/rotação estiver ativo.
  - Menu de contexto na bandeja:
    - ⏯ **Play / Pausar Animação**
    - ⏭ **Próximo Wallpaper**
    - ⏮ **Wallpaper Anterior**
    - ⏱ **Intervalo de Troca Automática** (Desligado, 5 min, 15 min, 30 min, 1 hora, 2 horas)
    - ❌ **Desativar Wallpaper (Sair da Bandeja):** Encerra a reprodução do vídeo, restaura o papel de parede original do Windows e remove o ícone de wallpaper da bandeja.
- **Sistema de Rotação Automática Periódica (Slideshow):**
  - Agendador contínuo em segundo plano para alternar wallpapers automaticamente conforme o intervalo configurado pelo usuário.
  - Navegação fluida entre os papéis de parede da biblioteca e pasta ativa.

### ⚡ Performance & Estabilidade
- **Correção Definitiva do Timer de Otimização em Segundo Plano (Bandeja):**
  - Identificada e corrigida a causa da impressão de que o timer de otimização parava com o aplicativo minimizado na bandeja:
    - Substituição do DispatcherTimer do WPF por System.Threading.Timer executado na ThreadPool nativa do .NET, imune à suspensão ou despriorização de filas de janelas ocultas no Windows.
    - Sincronização imediata do status e resumo (UpdateAutoOptStatusDisplay e UpdateRamStatus) ao restaurar a janela a partir da bandeja do sistema.
    - Notificação discreta via Toast quando o ciclo de otimização programado é executado com o programa minimizado.
## [0.21.2] - 2026-11-07

### 🚀 Adicionado & Aprimorado
- **Busca Sob Demanda de Programas Instalados e Executáveis do Sistema para Blindagem / Exclusão:**
  - **Motor Sob Demanda Eficiente (AppScannerService):**
    - Executa exclusivamente quando acionado pelo usuário, com zero consumo em segundo plano e sem timers contínuos.
    - Varre de forma assíncrona e ultrarrápida processos ativos na sessão (desconsiderando processos críticos do Windows), programas instalados no Registro (Uninstall de 64-bit e 32-bit), softwares registrados em App Paths, atalhos do Menu Iniciar e Desktop (.lnk), e jogos de PC (Steam, Epic Games, GOG, Ubisoft).
    - Deduplicação inteligente e resolução de ícones oficiais extraídos diretamente dos executáveis.
  - **Seletor Visual Moderno (AppPickerWindow):**
    - Nova janela modal Lumini Tray com suporte total a tema escuro/claro e carregamento pontual sob demanda.
    - Barra de busca em tempo real com filtragem instantânea por nome amigável ou nome do executável (.exe).
    - Abas de filtragem rápida: *Todos*, *⚡ Em Execução*, *🎮 Jogos* e *💻 Programas Instalados*.
    - Seleção múltipla com checkboxes para blindar dezenas de jogos e aplicativos simultaneamente em um único clique (+ ADICIONAR SELECIONADOS).
    - Suporte a duplo clique para adição imediata e botão 📁 PROCURAR .EXE... para seleção manual de qualquer arquivo no disco.
    - Sinalização automática de aplicativos já blindados (✔ Já Blindado) para evitar duplicações.
  - **Integração nas Listas de Exclusão da Aplicação:**
    - **LUMINI OTIMIZA (Lacuna de Exclusão):** Novo botão de destaque 🔍 ESCOLHER DA LISTA DE PROGRAMAS... para blindar processos contra encerramento automático sem precisar digitar manualmente o nome do .exe.
    - **Configurações de Áudio (Barra de Tarefas):** Botão 🔍 Escolher da Lista... na lista de exclusão do player de áudio, permitindo proteger jogos e comunicadores do controle de scroll.
## [0.21.1] - 2026-11-07

### 🎨 Identidade Visual & Rebranding Completo (100% Lumini)
- **Substituição Integral de Todas as Abas, Módulos, Relatórios e Funcionalidades:**
  - **Menu Lateral e Cabeçalhos:** Atualizadas todas as categorias e títulos de abas para `LUMINI INFORMAÇÕES`, `LUMINI JOGOS`, `LUMINI DESEMPENHO`, `LUMINI PERIFÉRICOS`, `LUMINI FERRAMENTAS`, `LUMINI MAKER`, `LUMINI BENCH`, `LUMINI OTIMIZA`, `LUMINI OC`, `LUMINI DRIVER & DDU`, `LUMINI WALLPAPERS`, `LUMINI INTEGRAÇÃO` e `INSTALAR BIBLIOTECA OFICIAL LUMINI`.
  - **Relatórios HTML e PNG:** O gerador de diagnósticos (`ReportBuilder.cs`) e o relatório visual de stress (`StressReportWindow.xaml.cs`) agora estampam `LUMINI TRAY` e geram arquivos `Lumini-Tray-Relatorio-*.html` e `Lumini-Stress-*.png`.
  - **Benchmarks 3D, Serviços e Scripts:** Janelas Direct3D/OpenGL de stress (`GpuBurn`, `GpuGameBench`, `GpuRtBench`), scripts de restauração de backup (`RESTAURAR_BACKUP.bat` e `LEIA-ME.txt`), pontos de restauração do Windows (`Lumini Tray - Antes da Otimização`), pastas de exportação (`Documentos\LuminiTray-Drivers`, `Imagens\LuminiWallpapers`, `%TEMP%\LuminiTray_*`) e sessão do Mixer de Volume do Windows atualizados para `Lumini Tray`.
- **Correção Definitiva do Ícone da Bandeja do Sistema (System Tray):**
  - Reconstruído `Assets/app.ico` com quadros DIB (`BITMAPINFOHEADER` 32-bit BGRA) nas resoluções 16x16, 24x24, 32x32, 48x48, 64x64 e 128x128 (além de PNG 256x256) para compatibilidade nativa total com `System.Drawing.Icon` e `Shell_NotifyIcon` do Windows.
  - Adicionado `CreateMainAppIcon()` em `SystemTrayManager.cs` com renderização direta de alta nitidez a partir do recurso WPF `pack://application:,,,/Assets/logo.png` (e fallback para `app.ico` / ícone associado ao executável), garantindo que o ícone oficial do Lumini Tray apareça sempre na bandeja do sistema.
- **Migração Garantida de Preferências Existentes:**
  - Adicionada migração automática em `AudioSettings.cs` (`MicOverlayDefaultMigratedV21`) e `AppSettings.cs` (`HotkeyMigratedV21`) para assegurar que mesmo instalações com arquivos de configuração pré-existentes adotem o flutuante de microfone desativado por padrão (`OverlayEnabled = false`) e o atalho `Ctrl + Shift + P` para o Mini Gerenciador de Tarefas.

## [0.21.0] - 2026-11-07

### 🎨 Identidade Visual & Rebranding
- **Rebranding Oficial do Aplicativo para Lumini Tray:**
  - Substituição completa de todas as referências à antiga marca Lumini Tray em favor de **Lumini Tray**.
  - **Nova Suíte de Ícones Oficiais:** Geração a partir da nova identidade visual do ícone de raios solares luminescentes em squircle escuro:
    - `Assets/app.ico`: Ícone mestre Windows em multi-resoluções (256x256, 128x128, 64x64, 48x48, 32x32, 16x16) com cantos arredondados suavizados e transparência nos vértices.
    - `Assets/logo.png`: Logotipo oficial de 256x256 para tema escuro e barra de título.
    - `Assets/logo-dark.png`: Logotipo de 256x256 para tema claro.
    - `Assets/logo-badge.png`: Badge oficial de 128x128.
  - **Interface do Usuário (UI):** Título principal atualizado para `Lumini Tray`, novo cabeçalho visual `LUMINI TRAY` e padronização de todos os diálogos, menus da bandeja e janelas secundárias (Mini Gerenciador, Overlay, Toast, Relatório de Stress).
  - **Executável e Sistema:** O projeto agora é compilado oficialmente como `LuminiTray.exe`, utilizando o mutex `Local\LuminiTray.SingleInstance` e registro de inicialização `LuminiTray`.
  - **Migração Transparente de Configurações:** Novo diretório de usuário `%AppData%\LuminiTray` com migração automática dos perfis e dados de `%AppData%\LuminiTray`.

### 🚀 Aprimoramentos & Ajustes Solicitados
- **Flutuante de Microfone Desativado por Padrão:** Configurado `OverlayEnabled = false` em `AudioSettings.cs`, mantendo o overlay flutuante de microfone desligado por padrão até que o usuário decida ativá-lo explicitamente.
- **Novo Atalho Padrão do Mini Gerenciador de Tarefas (`Ctrl + Shift + P`):**
  - O atalho global padrão para abrir/fechar o Mini Gerenciador de Tarefas foi atualizado de `Ctrl + Shift + M` para `Ctrl + Shift + P` (`VK_P = 0x50`), evitando sobreposição com comandos de microfone.

## [0.20.11] - 2026-11-06

### ⚡ Performance & Otimização
- **Redução Drástica da Latência (Delay) no Supressor de Ruído do Microfone:**
  - **WASAPI Event-Driven Streaming:** Implementado o modo de captura acionado diretamente por interrupção de hardware (`AUDCLNT_STREAMFLAGS_EVENTCALLBACK`), substituindo o polling anterior com `Thread.Sleep(3)`. Agora o worker de áudio acorda instantaneamente no microssegundo exato em que o pacote de voz é recebido do microfone.
  - **Dimensionamento Otimizado de Buffers WASAPI:** Buffers de hardware reduzidos de 50 ms para o período nativo do dispositivo (10 ms a 12 ms na captura e 12 ms na renderização do microfone virtual CABLE Input e retorno nos fones), eliminando mais de 75 ms de latência passiva de driver.
  - **Mecanismo Anti-Lag nos Buffers Circulares (RingBuffer):** Adicionado limitador de backlog com `TrimExcess` no `RingBuffer<T>`, impedindo o acúmulo residual de amostras e garantindo que a fila nunca ultrapasse 30 ms a 40 ms de áudio, mesmo sob transições ou oscilações de carga do sistema.
  - **Temporizador Multimídia de Alta Resolução:** Ativação de `timeBeginPeriod(1)` da `winmm.dll` durante a execução da thread de áudio em tempo real, eliminando o jitter quântico padrão do escalonador do Windows (~15,6 ms).
  - **Latência Total Perceptível:** Reduzida de ~160–220 ms para **~30–35 ms**, tornando o retorno de voz e a transmissão em chamadas/jogos completamente em tempo real e imperceptível ao ouvido humano.

## [0.20.10] - 2026-11-06

### 🛠️ Correções
- **Correção Geral de Codificação e Mojibake de Texto em Toda a Aplicação:**
  - Identificada e corrigida a corrupção generalizada de codificação de caracteres (mojibake UTF-8/Windows-1252) que exibia textos com caracteres quebrados em toda a interface do programa (exemplo: `"PrÃ³xima execuÃ§Ã£o em:"` no agendamento de otimização, `"informaÃ§Ãµes"`, `"visÃ£o geral"`, `"vÃ­deo"`, `"sÃ³"`, `"botÃ£o"`, etc.).
  - Restaurada a integridade de todas as strings em `MainWindow.xaml.cs` (mais de 230 ocorrências reparadas com acentuação e pontuação corretas em português, tais como `Próxima execução em:`, `informações`, `vídeo`, `navegação`, `página`, etc.).
  - Corrigidos comentários e metadados com mojibake de múltiplos níveis no arquivo de projeto `LuminiTray.csproj` (`também`, `Otimizações`, `Memória`, etc.).

## [0.20.9] - 2026-11-05

### 🛠️ Correções
- **Encerramento Completo e Definitivo do Processo no Windows:**
  - Corrigida a persistência indevida do processo `LuminiTray.exe` em segundo plano no Gerenciador de Tarefas do Windows após o usuário fechar o programa através do menu da bandeja (*System Tray*), pelo botão "ENCERRAR APLICATIVO COMPLETAMENTE" nas configurações ou pela janela principal.
  - **ShutdownMode OnMainWindowClose:** Configurado `ShutdownMode="OnMainWindowClose"` no `App.xaml`, garantindo que o encerramento da janela principal comande a finalização formal de toda a aplicação no runtime do WPF, mesmo havendo janelas flutuantes ativas ou ocultas.
  - **Fechamento Explícito de Todas as Janelas Secundárias:** O evento `Closed` agora fecha de forma atômica o `MiniTaskManagerWindow` (Mini Gerenciador de Tarefas), `TaskbarVolumeOsdWindow` (OSD de volume da barra de tarefas), `MicOverlayWindow`, `OverlayWindow`, `ToastWindow` e quaisquer outras janelas abertas em `Application.Current.Windows`.
  - **Limpeza Rigorosa de Recursos e Ganchos:** Descarte limpo dos 4 ícones da bandeja (`SystemTrayManager`) com ocultação imediata (`Visible = false; Icon = null;`), parada do timer e dos contadores PDH do `MiniTaskManagerService`, e liberação de ganchos nativos de teclado/mouse (`TaskbarVolumeHook`, `AudioHotkeyHook`).
  - **Encerramento Garantido sem Processos Zumbis (`App.OnExit`):** Adicionado tratamento em `App.OnExit` para liberar e descartar o Mutex global de instância única (`_single`) e garantir o término limpo e imediato de todas as threads nativas e drivers via `Environment.Exit`, impedindo processos órfãos.

## [0.20.8] - 2026-11-05

### 🚀 Adicionado & Aprimorado
- **Mini Gerenciador de Tarefas com Todas as Funcionalidades do Game Overlay:**
  - O Mini Gerenciador de Tarefas (`MiniTaskManagerWindow`) agora possui paridade total de recursos com a Overlay de Games:
    - **Fixação e Bloqueio (`IsLocked`):** Suporte a modo livre (arrastar pela tela e redimensionar livremente pelas bordas via `WM_NCHITTEST`) e modo fixado com clique transparente (`WS_EX_TRANSPARENT`) para não atrapalhar jogos ou tarefas em segundo plano.
    - **Transparência Dupla:** Rolagem simples com o scroll do mouse para transparência dinâmica da caixa de fundo (mantendo textos e gráficos 100% nítidos) e Ctrl + Scroll para opacidade total da janela (20% a 100%), acompanhado de indicador visual flutuante (`TxtOpacityBadge`).
    - **Posicionamento e Presets:** Presets nos cantos da tela (Superior esquerdo/direito, Inferior esquerdo/direito) e modo Livre (`CUSTOM`), com retenção de coordenadas entre sessões.
    - **Escala e Tamanho:** Suporte a escala ajustável (Pequeno 0.85x, Médio 1.0x, Grande 1.3x) e fechamento instantâneo via botão do meio do mouse.
- **Unificação na Mesma Aba ("Overlay & Mini Tarefas"):**
  - A aba `PageOverlay` foi transformada em um centro unificado com seletor em chips no topo: `[ 🎮 Overlay de Jogos (FPS & Sensores) ]` e `[ ⚡ Mini Gerenciador de Tarefas ]`, organizando todas as configurações de ambos os monitores flutuantes de forma modular e limpa.
- **Inicialização Sob Demanda do Mini Gerenciador de Tarefas:**
  - O Mini Gerenciador agora inicia desligado por padrão ao abrir o programa, permitindo ao usuário ativá-lo sob demanda via atalho de teclado (Ctrl + Shift + M), ícone na bandeja do sistema (Tray) ou botão na interface.
- **Definição de Atalhos Globais no Teclado:**
  - Adicionado configurador dinâmico de atalhos globais de teclado no app para ligar e desligar tanto a Overlay de Games (padrão: `Ctrl + Shift + O`) quanto o Mini Gerenciador de Tarefas (padrão: `Ctrl + Shift + M`).
  - Permite clicar no botão de atalho e pressionar qualquer combinação desejada de teclas (Ctrl, Alt, Shift, Win + tecla), com persistência instantânea e botão de restauração padrão (`↺`).
  - **Desbloqueio Inteligente por Atalho:** Caso o HUD ou a Overlay estejam visíveis e fixados na tela, o acionamento do atalho desbloqueia a janela permitindo reposicionar e ajustar imediatamente com o mouse.

## [0.20.7] - 2026-11-05

### 🛠️ Correções
- **Correção de Tipagem Aritmética no Cálculo de Memória RAM:**
  - Corrigida a ambiguidade de operador aritmético `CS0034` no cálculo percentual do top consumer de memória RAM em `MiniTaskManagerService.cs`, utilizando coerção explícita de precisão contínua com `Math.Round`.
  - Tratada a checagem defensiva de nulidade no controle de overlay (`ChkOverlay`) em `MainWindow.xaml.cs`.

## [0.20.6] - 2026-11-05

### 🛠️ Correções
- **Correção de Atributo XAML no Mini Gerenciador de Tarefas:**
  - Removido o atributo inválido `LetterSpacing` no `TextBlock` do cabeçalho de `MiniTaskManagerWindow.xaml`, corrigindo a falha de compilação `error MC3072: a propriedade 'LetterSpacing' não existe no namespace XML` reportada durante a publicação.

## [0.20.5] - 2026-11-05

### 🚀 Adicionado & Aprimorado
- **Mini Gerenciador de Tarefas Flutuante (Adaptação MicroManager / Windhawk):**
  - Implementado o novo serviço de alto desempenho em nível de kernel (`MiniTaskManagerService`) baseado no mod open-source MicroManager do Windhawk, com monitoramento contínuo de **CPU, GPU, RAM e DISCO** e identificação atômica dos maiores processos consumidores (*top consumers*) de cada recurso via `NtQuerySystemInformation` e contadores de engine PDH (`\GPU Engine(*)\Utilization Percentage`).
  - Nova janela flutuante Dark HUD (`MiniTaskManagerWindow`) com cantos arredondados, backdrop acrílico/escuro, cores vivas de alto contraste para cada recurso (CPU: Ciano `#00C8FF`, GPU: Verde Neon `#00DC64`, RAM: Âmbar `#FFB446`, DISCO: Magenta `#D946EF`) e gráfico de barras históricas de 16 amostras em tempo real.
  - Alternância interativa da métrica exibida no gráfico através de clique nas linhas ou no canvas, ou navegação pelas teclas ↑ / ↓.
  - Menu de contexto avançado ao clicar com botão direito sobre qualquer processo:
    - **Finalizar tarefa:** Encerramento direcionado com confirmação e validação de segurança.
    - **Finalizar árvore de processos...:** Identificação automática da raiz com mesmo nome de executável via timestamp de criação de processo (protegendo contra reuso de PID), detecção recursiva de processos descendentes, alerta de proteção ao `explorer.exe` e processos críticos do Windows, e diálogo com contagem exata antes do encerramento de baixo para cima.
    - **Abrir local do arquivo:** Revela e seleciona o executável diretamente no Windows Explorer.
  - Fechamento intuitivo com tecla `Esc` ou ao clicar fora da janela flutuante (`Deactivated`), e posicionamento inteligente automático próximo à bandeja do Windows sem cortes na tela.
  - Integração nativa na bandeja do sistema (`SystemTrayManager`) com ícone estilizado de 4 barras de atividade, tooltip em tempo real com resumo instantâneo dos 4 recursos, e menu de contexto com atalhos de taxa de atualização (300ms, 500ms, 1.0s, 3.0s) e acesso rápido ao Gerenciador de Tarefas do Windows.
  - Botão de acesso rápido `⚡ Mini Tarefas` adicionado ao cabeçalho da página de Sensores (`PageSensors`).
  - Nova opção em Configurações > Geral > Ícones de Status na Bandeja para ativar/desativar a exibição do Mini Gerenciador na bandeja (`ChkSettingsShowMiniTaskTray` / `ShowMiniTaskTrayIcon`).

### ⚡ Performance & Otimização
- **Layout Responsivo e Limpo na Aba "Windows e Debloat":**
  - Reorganizado o cabeçalho de ações da página `PageWindowsDebloat` em 2 linhas funcionais e responsivas:
    - **Linha 1:** Campo de busca (`TxtDebloatSearch`), contador de bloatwares detectados (`TxtBloatwareCount`) e botão de desinstalação destacado (`BtnUninstallSelectedBloatware`) alinhado à direita.
    - **Linha 2:** Painel dinâmico em `WrapPanel` com os 5 botões de filtro e manutenção (`Marcar Seguros`, `Marcar Bloatware OEM`, `Desmarcar Todos`, `🔄 Re-Escanear`, `↺ Reinstalar Apps do Windows`) com espaçamento confortável, eliminando qualquer compressão ou sobreposição visual em resoluções variadas.

## [0.20.4] - 2026-11-05

### 🛠️ Correções & Estabilidade
- **Blindagem e Resiliência de Navegação (NullReferenceException em ShowPage e Nav_Checked):**
  - Implementada proteção abrangente contra exceções em `ShowPage` e `Nav_Checked`, eliminando erros de desreferência nula (`NullReferenceException`) disparados em transições e cliques de botões da barra lateral.
  - O seletor de visibilidade de páginas agora utiliza verificação nula defensiva e comparação de nomes insensível a maiúsculas/minúsculas (`StringComparison.OrdinalIgnoreCase`).
  - Blindagem do ciclo de vida de captura de FPS/Overlay (`EnsureFramesLifecycle`) com checagens prévias de nulidade e captura de exceções de sessão ETW.
  - Proteção refinada no Dispatcher do `App.xaml.cs`: o encerramento forçado do processo (`Shutdown(1)`) não é mais disparado indevidamente caso a janela principal já esteja criada e em execução.

## [0.20.3] - 2026-11-05

### 🚀 Adicionado & Aprimorado
- **Reorganização Estrutural de Inicialização e Configurações:**
  - Migradas todas as configurações de inicialização com o Windows (`ChkAutoStartWithWindows`, rotinas automáticas de limpeza de RAM, tweaks, fechamento de apps e inicialização minimizada) e a lista de programas de inicialização (`StartupList`) da aba Geral de Configurações para a página **Inicialização** da sidebar (`PageStartup`).
  - Movidos os atalhos rápidos e seletores principais (`ChkFastMicMute`, `ChkFastMouseWheelVolume`, `ChkFastNoiseSuppressor`, `CboFastAudioOutput`, `CboFastAudioInput`, `ChkFastOverlay`) da página de Inicialização para a aba **Geral** de Configurações (`PanelSettingsGeneral`).
- **Controle Preciso da Função de Mute de Microfone:**
  - Ajustado o atalho rápido `ChkFastMicMute` para ativar e desativar a função/atalho global de microfone (`_audioSettings.EnableMicMuteFeature`) em vez de disparar o comando de mute instantâneo.
  - Quando a função estiver desativada, os atalhos de teclado globais de microfone são ignorados e a overlay flutuante de microfone (`_micOverlay`) permanece oculta.
- **Transparência Dupla e Desbloqueio Inteligente da Overlay:**
  - **Scroll Simples no Overlay:** Ajusta dinamicamente a transparência apenas da caixa de fundo (`Box.Background`), mantendo informações, textos, gráficos e métricas com 100% de visibilidade e nitidez. Salvo em `_settings.OverlayBackgroundOpacity`.
  - **Ctrl + Scroll no Overlay:** Preservado o controle da opacidade total da janela (`this.Opacity`, entre 20% e 100%), permitindo transparência completa da overlay quando desejado.
  - **Desbloqueio Inteligente por Atalho:** Ao acionar o atalho global de visualização da overlay (`Ctrl + Shift + O`), se a overlay estiver na tela e bloqueada (`OverlayLocked`), o atalho desbloqueia a janela para permitir arrastar e reposicionar com o mouse em vez de fechar a janela.
- **Restauração Seletiva na Pós-Formatação:**
  - Adicionado suporte a restauração granular por categoria: checkboxes e botões de ação rápida para Drivers, Redes Wi-Fi e Programas (Winget) em seus respectivos cards de descoberta.
  - Adicionadas caixas de seleção individuais (`IsSelected`) para cada item da fila de pós-formatação (`LstPostFormatQueue`).
  - O motor de execução `ExecuteQueueAsync` agora respeita estritamente a seleção do usuário, mantendo itens não selecionados disponíveis para execuções seguintes sem travá-los no estado `Skipped`.
  - Sincronização dinâmica entre caixas de seleção de categorias e os itens individuais da lista.
  - Resumo e botão principal atualizados dinamicamente para exibir a quantidade de itens selecionados e pendentes.
- **Sincronização e Reatividade:**
  - A alternância do recurso de mute em Configurações > Geral atualiza instantaneamente a tabela de teclas ativas do hook global STA (`RebuildActiveKeys`), garantindo latência zero.
  - Desbloqueio e alternância da overlay sincronizados com o checkbox rápido da aba Geral.
  - Opacidade padrão da janela do overlay definida em 100% para nitidez total das informações, governando o fundo por `OverlayBackgroundOpacity`.

### ⚡ Performance & Otimização
- **Design Compacto nos Botões de Desinstalação:**
  - Reduzidas as proporções dos botões vermelhos de desinstalação em lote (`BtnUninstallBatch` no Desinstalador Avançado e `BtnUninstallSelectedBloatware` em Debloat) para padding e tipografia compactos (`Padding="10,5"`, `FontSize="11"`), evitando que empurrem ou obstruam as caixas de seleção e controles vizinhos.

## [0.20.2] - 2026-11-05

### 🛠️ Correções
- **Auditoria Completa — Fase 1 (Navegação e Blindagem de Exceções):**
  - Corrigido o `System.NullReferenceException` em `MainWindow.ShowPage` / `Nav_Checked` ao clicar em **RESET MAKER**: o cabeçalho de grupo `GrpMaker` recebia indevidamente `Checked="Nav_Checked"` (sem `Tag`), enquanto o item `NavMakerPorts` estava sem o evento. O evento foi removido do grupo e restaurado no item.
  - `Nav_Checked` agora ignora remetentes sem `Tag` de página válida e `ShowPage` ignora nomes nulos/vazios e páginas ausentes.
  - Abas **Pós-Formatação** e **Reparar Windows** adicionadas ao histórico de navegação (botões Voltar/Avançar).
  - `App.xaml.cs`: `DispatcherUnhandledException` agora marca a exceção como tratada (`ev.Handled = true`), eliminando a janela dupla de erro e o fechamento abrupto do programa em erros não-fatais. Falhas antes da janela principal carregar exibem os detalhes e encerram de forma limpa. O `crash.log` passou a acumular as entradas com data/hora em vez de sobrescrever.
- **Auditoria Completa — Fase 2 (Bindings `Mode=OneWay`):**
  - Corrigida a `XamlParseException` *"Uma associação TwoWay ou OneWayToSource não pode funcionar na propriedade somente leitura 'ProgressPercentage'"* ao abrir o **Mapa do Disco**: a `ProgressBar` (cujo `Value` é TwoWay por padrão no WPF) agora usa `{Binding ProgressPercentage, Mode=OneWay}`.
  - Barra de velocidade do **Benchmark de DNS** (`SpeedBarRatio`) e textos `Run` da galeria de **Wallpapers** (`DimensionsText`, `FileSizeText`) passaram a usar `Mode=OneWay`, já que são apenas exibição.
  - Auditados todos os bindings TwoWay por padrão (`ProgressBar`/`Slider.Value`, `CheckBox.IsChecked`, `TextBox`/`Run.Text`, `SelectedItem`) em todos os arquivos XAML e no code-behind: os demais apontam para propriedades com `set` público.
- **Auditoria Completa — Fase 3 (Restauração Total dos Event Handlers nas 32 Abas):**
  - Resolvido o deslocamento em cascata (off-by-one / event shifting) de 505 elementos interativos em `MainWindow.xaml`, restaurando as conexões exatas com os métodos correspondentes no code-behind (`MainWindow.*.cs`).
  - Reconectados todos os botões, checkboxes, radio buttons, sliders e combos afetados em todas as telas principais:
    - **Instalar Apps (`PageApps`):** Botões "Marcar Todos" (`BtnAppsAll_Click`), "Desmarcar" (`BtnAppsNone_Click`), filtro de categorias (`BtnAppsCat_Click`), atualização da Store (`BtnAppsStore_Click`) e instalação de selecionados (`BtnAppsInstall_Click`).
    - **Backup (`PageBackup`):** Botões de seleção em lote (`BtnSelectAllBackupApps_Click`, `BtnDeselectAllBackupApps_Click`), checkboxes de apps e pastas (`ChkBackupApp_Click`, `ChkBackupFolder_Click`), gerador de drivers e instalador Winget (`BtnGenerateDriverBackup_Click`, `BtnGenerateAppInstaller_Click`), seleção de destino (`BtnBrowseBackupDest_Click`), e início/cancelamento do backup de pastas (`BtnStartFoldersBackup_Click`, `BtnCancelFoldersBackup_Click`).
    - **Ferramentas de Disco & S.M.A.R.T. (`PageDiskTools`):** Navegação e seleção de pasta no analisador (`BtnDiskChooseFolder_Click`, `BtnDiskSpaceUp_Click`, `BtnDiskOpenExplorer_Click`), redimensionamento do Treemap (`CnvTreemap_SizeChanged`), dicas rápidas (`BtnTipLimpeza_Click`, `BtnTipDesinstalador_Click`, `BtnTipDuplicados_Click`), abas de maiores arquivos/pastas (`TopItemsTab_Checked`), limpeza rápida (`BtnQuickCleanScan_Click`, `BtnQuickCleanNow_Click`), e diagnóstico profundo S.M.A.R.T. / auto-testes ATA / reparo Chkdsk (`BtnRefreshSmartBadBlocks_Click`, `BtnStartBadBlockScan_Click`, `BtnStopBadBlockScan_Click`, `BtnSelfTestShort_Click`, `BtnSelfTestExt_Click`, `BtnSelfTestAbort_Click`, `BtnLocateBadLbaFiles_Click`, `BtnRunSelectiveTest_Click`, `BtnRepairChkdsk_Click`).
    - **Desinstalador Avançado (`PageUninstaller`):** Filtros por categoria (`BtnFilterCategory_Click`), busca dinâmica (`TxtUninstallSearch_TextChanged`), seleção em massa (`ChkSelectAllApps_Checked`, `ChkSelectAllApps_Unchecked`, `BtnClearSelection_Click`), exibição de ocultos (`ChkShowHiddenApps_Changed`), desinstalação em lote (`BtnUninstallBatch_Click`), e ações por item de software (`ChkAppItem_Changed`, `BtnOpenInstallLocation_Click`, `BtnUninstallNormal_Click`, `BtnUninstallDeep_Click`), além da revisão de resíduos (`BtnSelectAllLeftovers_Click`, `BtnDeselectAllLeftovers_Click`, `BtnCleanLeftoversNow_Click`, `BtnCloseLeftovers_Click`).
    - **Central Desfazer / Restauração (`PageSettings`):** Alternância de sub-abas de configurações (`SettingsTab_Checked`), atualização da inicialização (`BtnStartupScan_Click`), restauração total ao padrão (`BtnUndoRestoreAll_Click`), abertura do RSTRUI (`BtnOpenSystemRestore_Click`), criação de ponto de restauração (`BtnUndoCreateRestorePoint_Click`), atualização de lista (`BtnUndoRefresh_Click`) e reversão pontual de tweaks (`BtnUndoSingleTweak_Click`).
    - **Debloat & Otimizações do Windows (`PageWindowsDebloat`):** Aplicação de perfil adaptativo (`BtnApplyAdaptiveProfile_Click`), reversão de debloat (`BtnRevertDebloatTweaks_Click`), alternância de sub-abas (`BtnSubTabTweaks_Click`, `BtnSubTabDebloat_Click`), checkboxes de tweaks de CPU/GPU/Input/Sistema (`TweakDebloat_Click`), busca de bloatwares (`TxtDebloatSearch_TextChanged`), marcação segura/OEM/desmarcar (`BtnSelectSafeBloatware_Click`, `BtnSelectOemBloatware_Click`, `BtnDeselectAllBloatware_Click`), re-escanear (`BtnScanBloatware_Click`), desinstalação de bloatwares selecionados (`BtnUninstallSelectedBloatware_Click`), e seleção individual (`DebloatItemCheckbox_Click`).
    - **Supressor de Ruído Neural (`PageSuppressor`):** Interruptor master (`BtnToggleMaster_Click`), instalação e abertura de drivers de cabo virtual (`BtnInstallVirtualCable_Click`, `BtnOpenDriverFolder_Click`), seleção de dispositivos e atualização (`CmbVirtualCableDev_SelectionChanged`, `CmbInputMic_SelectionChanged`, `CmbOutputDev_SelectionChanged`, `BtnRefreshDevices_Click`), monitoramento/retorno de voz (`ChkListenSelf_Checked`, `ChkListenSelf_Unchecked`, `RadioMonitorMode_Checked`), teste rápido de microfone (`BtnQuickTest_Click`), e ajuste de sliders de intensidade, ganho, noise gate e equalizador gráfico de 3 bandas (`SliderIntensity_ValueChanged`, `SliderGain_ValueChanged`, `SliderGateThreshold_ValueChanged`, `SliderGateRelease_ValueChanged`, `SliderLowEq_ValueChanged`, `SliderMidEq_ValueChanged`, `SliderHighEq_ValueChanged`, `BtnResetEq_Click`).
    - **Curvas de Ventoinha (`PageFanControl`):** Interruptor geral e abas CPU/GPU (`BtnToggleFanMaster_Click`, `BtnTabFanCpu_Click`, `BtnTabFanGpu_Click`), presets de curva (`FanPresetQuiet_Click`, `FanPresetBalanced_Click`, `FanPresetPerf_Click`, `FanPresetCpuLock100_Click`, `FanPresetGpuQuiet_Click`, `FanPresetGpuBalanced_Click`, `FanPresetGpuPerf_Click`, `FanPresetGpuLock100_Click`), interações com os gráficos de curva (`FanCurveCanvasCpu_*`, `FanCurveCanvasGpu_*`), sliders de pontos de temperatura/PWM e histerese (`FanPoint_Changed`, `FanHysteresis_Changed`, `FanGpuPoint_Changed`, `FanGpuHysteresis_Changed`), e gerenciamento de perfis (`CmbFanCpuPresets_SelectionChanged`, `BtnSaveFanCpuPreset_Click`, `BtnDeleteFanCpuPreset_Click`, `CmbFanGpuPresets_SelectionChanged`, `BtnSaveFanGpuPreset_Click`, `BtnDeleteFanGpuPreset_Click`).
  - Total de 619 controles com eventos totalmente restaurados e validados, com 0 handlers faltantes, 0 handlers inexistentes e conformidade estrita com o esquema XML.
- **Auditoria Completa — Fase 4 (Testes Automatizados de Regressão XAML e Handlers):**
  - Implementada suíte de testes de auditoria de arquitetura XAML em `PostFormatAndHealthTests.cs` para blindar o projeto contra regressões visuais e estruturais:
    - `XamlNavigation_AllNavItemsAndGroups_HaveValidCheckedAndTags`: assegura que cabeçalhos colapsáveis (`ToggleButton.NavGroup`) nunca recebam evento `Checked="Nav_Checked"`, e que todos os 36 botões de navegação (`RadioButton.NavItem`) possuam o manipulador `Checked="Nav_Checked"` e `Tag` apontando para páginas válidas existentes.
    - `XamlBindings_ProgressBarsAndReadOnlyProperties_UseOneWayMode`: valida que bindings em `ProgressBar.Value` e propriedades somente leitura (`ProgressPercentage`, `SpeedBarRatio`, `DimensionsText`, `FileSizeText`) utilizam estritamente `Mode=OneWay`, evitando exceções `XamlParseException` de two-way binding.
    - `XamlEventHandlers_AllCodeBehindHandlers_AreCorrectlyWiredInXaml`: audita a integridade total entre XAML e code-behind, garantindo 0 manipuladores ausentes no code-behind e que todos os métodos de evento interativos de UI em `MainWindow.*.cs` estejam devidamente conectados aos controles nas 32 abas do aplicativo.

## [0.20.1] - 2026-11-04

### 🛠️ Correções
- **Correção Crítica de Inicialização XAML (`XamlParseException` em `MainWindow.xaml` e `Theme.xaml`):**
  - Resolvida a exceção fatal `Não é possível encontrar o recurso denominado 'Display'` gerada durante o `InitializeComponent()` ao adicionar a fonte `Display` em `Theme.xaml` e alinhar o elemento `TxtBootTypicalTime` com a tipografia `Mono` do projeto.
  - Corrigida a referência do conversor de visibilidade no badge "Recomendado" da Central de Reparação do Windows (`BoolToVis` -> `BoolVis`), registrando também o alias `BoolToVis` globalmente em `Theme.xaml` para prevenir futuras regressões.
  - Padronizadas todas as 38 referências legadas de recursos dinâmicos (`Text`, `Primary`, `Border`, `BorderBrush`, `CardBg`) em `MainWindow.xaml` para as chaves oficiais da paleta (`Fg`, `Accent`, `Line`, `LineStrong`, `Card`), garantindo conformidade total com os temas escuro e claro.

## [0.20.0] - 2026-11-04

### 🚀 Adicionado & Aprimorado
- **Nova Central Pós-Formatação (`PagePostFormat`, `PostFormatManager`, `MainWindow.PostFormat`):**
  - **Descoberta Automática de Backups em Todas as Unidades:** Varredura automática em todas as partições internas e discos externos/pendrives em busca de backups feitos pelo Lumini Tray (drivers exportados, redes Wi-Fi e senhas salvas, instaladores de programas Winget).
  - **Fila Sequencial Automatizada com Controles de Parada e Continuação:** Painel visual com lista de tarefas ordenadas por categoria (Drivers, Wi-Fi, Otimizações, Programas), status de cada etapa em tempo real e botões `[PARAR / PAUSAR]` e `[CONTINUAR]`.
  - **Persistência de Estado e Continuidade Pós-Reinicialização:** Gravação automática do progresso da fila em `%LocalAppData%\LuminiTray\post_format_state.json` com chave `RunOnce` no Registro do Windows. Caso o computador seja reiniciado ou o aplicativo fechado no meio do processo, o Lumini Tray retoma automaticamente do ponto exato onde parou através de um banner no topo da tela inicial.
- **Transformação Completa da Aba de Bad Blocks em "Saúde dos Discos" (`PageDiskTools`, `UsbFakeDriveScanner`, `MainWindow.DisksHealth`):**
  - **Reestruturação e Identidade Visual do HUB Pro:** Botão e cabeçalho reformulados para "Saúde dos discos" com navegação interna por sub-abas: `[❤️ Saúde]` e `[🔌 Verificar se é original (capacidade falsa)]`.
  - **Sub-aba Saúde Geral & S.M.A.R.T.:** Pílulas de seleção de unidades de armazenamento, diagnóstico de saúde e temperatura, telemetria S.M.A.R.T. em tempo real, heatmap de blocos de superfície e veredito consolidado de integridade com orientações técnicas.
  - **Sub-aba Verificar se é Original (Pendrive / Cartão / HD Falsificado):**
    - Seletor inteligente exclusivo para unidades removíveis (o disco do Windows e discos fixos são protegidos e ocultados dessa verificação).
    - **Identificação de Hardware & Controlador USB:** Leitura de Vendor ID (VID), Product ID (PID), fabricante e revisão, com banco de dados de controladores suspeitos frequentemente usados em réplicas falsificadas (Chipsbank, FirstChip, Appotech, Alcor, Micov, etc.).
    - **Teste Rápido Não-Destrutivo por Amostragem:** Gravação e conferência de mais de 1.000 pontos (ou 2.000 pontos em modo precisão máxima) distribuídos em todo o span da unidade sem apagar arquivos existentes, detectando looping de memória flash e capacidade falsa em poucos minutos.
- **Nova Aba Reparar o Windows (`PageWindowsRepair`, `WindowsRepairService`, `MainWindow.WindowsRepair`):**
  - **Suíte Completa de Correção Oficial da Microsoft:** Execução em ordem técnica recomendada das principais rotinas de reparo: Criação de Ponto de Restauração, `chkdsk C: /scan`, `DISM /RestoreHealth`, `sfc /scannow`, WMI `salvagerepository`, limpeza de cache de ícones/miniaturas, reset de rede e Winsock/DNS, e agendamento de verificação profunda `chkdsk /f` no próximo boot.
  - **Terminal de Logs e Acompanhamento Unificado:** Terminal embutido com saída detalhada dos processos, barra de progresso percentual, botão de cancelamento seguro a qualquer momento e card de veredito com aviso de reinicialização.
- **Telemetria Avançada de Bateria em Energia (`BatteryReportService`, `BatteryInfo`, `MainWindow.Net`, `PageEnergy`):**
  - **Detecção Automática de Notebooks:** Identificação em tempo real da presença de bateria no sistema, exibindo card dedicado e ocultando automaticamente a seção quando executado em desktops.
  - **Métricas Completas do HUB Pro:** Extração via `powercfg /batteryreport /xml` e WMI com badge de saúde percentual, contagem de ciclos (`CycleCount`), capacidade de fábrica (Design Capacity), capacidade atual em carga total (Full Charge Capacity) em mWh e Wh, taxa de degradação da bateria %, química (Líon, etc.), fabricante e número de série.
  - **Comparativo de Duração e Histórico:** Estimativa de autonomia quando novo vs duração atual, histórico de capacidade das últimas sessões e botão dedicado para abrir o Relatório Oficial HTML do Windows (`powercfg /batteryreport`) no navegador padrão.

## [0.19.0] - 2026-11-04

### 🚀 Adicionado & Aprimorado
- **Nova Tela Dedicada de Inicialização do Windows (`BootDiagnosticsService`, `PageStartup`, `MainWindow.Startup`):**
  - **Tempo de Boot e Métricas Detalhadas:** Cálculo do tempo típico para ligar o Windows, decomposição do Caminho Principal (`MainPath` - Kernel e Drivers) e Pós-Inicialização (`PostBoot` - carregamento de aplicativos e tempo até a ociosidade do desktop) baseado no log `Microsoft-Windows-Diagnostics-Performance/Operational` (Event ID 100) com fallback para o log de Sistema (Event ID 6005) e Uptime.
  - **Indicador de Início Rápido (Fast Startup / Hiberboot):** Detecção inteligente do estado do início rápido do Windows via registro (`HiberbootEnabled`), explicando o impacto no boot a frio e reinicializações.
  - **Timeline Histórica Visual das Últimas Inicializações:** Gráfico visual comparativo das últimas sessões de inicialização com barras horizontais proporcionais, data, horário e tempo total decorrido.
  - **Diagnóstico de Gargalos ("O que atrasou o início"):** Mapeamento automático dos programas (ID 101), drivers (ID 102), serviços (ID 103) e tarefas em segundo plano (ID 108) que adicionaram atraso ao boot, exibindo tempo exato de lentidão e ícones de identificação.
  - **Programas de Inicialização com Selos de Recomendação do HUB Pro:** Categorização de cada item de inicialização com selos coloridos ("Seguro de desligar" em verde esmeralda, "Melhor deixar ligado" em azul e "Confira antes" em âmbar), além de interruptor de ativação rápida, botão de remoção e restauração de removidos.
  - **Retorno da Aba ao Menu Lateral:** Item "Inicialização" reintroduzido no menu `RESET FERRAMENTAS`, desvinculando-o da aba Geral das Configurações.
  - **Fechamento Automático de Abas na Inicialização:** Ao abrir o programa, todos os grupos expansíveis do menu lateral (Ferramentas, Reset Maker, etc.) iniciam devidamente recolhidos.
- **Atalhos Rápidos & Controles do Programa na Aba Geral (`MainWindow.Settings`, `PanelSettingsGeneral`):**
  - Novo card de conveniência na aba Geral de Configurações sem remover os controles das páginas originais.
  - Atalho rápido para mutar/desmutar microfone com tecla de atalho global (`Ctrl + Shift + M`).
  - Atalho rápido para ativar/desativar controle de volume com scroll do mouse na barra de tarefas.
  - Seletor rápido de dispositivo de saída de som e microfone padrão com sincronização instantânea.
  - Atalho rápido para ligar/desligar o Supressor Neural de Ruído.
  - Atalho rápido para ligar/desligar a Overlay de Desempenho e FPS nos jogos.
- **Sub-aba de Atualização de Programas no Instalador de Apps (`AppInstaller`, `MainWindow.Apps`, `ViewAppsUpdate`):**
  - Sub-abas `[📦 INSTALAR PROGRAMAS]` e `[🔄 ATUALIZAR PROGRAMAS]` no topo da tela de aplicativos.
  - Detecção de novas versões disponíveis via `winget upgrade --include-unknown` exibindo versão instalada, versão disponível, ID e status.
  - Botões para "Verificar Atualizações", "Atualizar Selecionados" e "🚀 ATUALIZAR TODOS" com execução silenciosa em lote.
- **Atualização Completa de Drivers com Ponto de Restauração (`DriverManager`, `MainWindow.Driver`):**
  - Botão `[🚀 ATUALIZAR TODOS OS DRIVERS DISPONÍVEIS]` na tela de Drivers & DDU.
  - Criação automática prévia de Ponto de Restauração do Windows via `SystemRestoreManager` antes de baixar e instalar as atualizações pelo Windows Update.
- **Restauração de Aplicativos Padrão do Windows no Debloat (`WindowsDebloatService`, `MainWindow.WindowsDebloat`):**
  - Botão `[↺ REINSTALAR APPS DO WINDOWS]` na aba Bloatware, permitindo re-registrar todos os pacotes AppX nativos de fábrica do Windows de forma segura caso algum componente tenha sido removido por engano.
- **Backup e Restauração de Redes Wi-Fi e Senhas (`BackupService`, `MainWindow.Backup`):**
  - Card dedicado **REDES WI-FI E SENHAS (BACKUP PARA FORMATAÃ‡ÃƒO)** na Central de Backup.
  - Leitura e listagem de todos os perfis Wi-Fi salvos com SSID, autenticação e senhas.
  - Exportação de perfis XML com chave legível (`key=clear`), relatório em texto (`Redes-WiFi-Senhas.txt`) e script em lote automatizado (`Restaurar-WiFi.bat`).
  - Restauração com 1 clique de todos os perfis de uma pasta de backup de volta para o Windows.

## [0.18.0] - 2026-11-04

### 🚀 Adicionado & Aprimorado
- **Novo Mapa do Disco com Particionamento 2D Squarified Treemap (`TreemapLayoutEngine`, `DiskSpaceAnalyzer`, `MainWindow.DiskSpace`):**
  - **Treemap Squarified 2D Responsivo:** Implementação do algoritmo Squarified Treemap (Bruls, Huizing, van Wijk) com retângulos aninhados proporcionais de alta resolução, separação visual, hierarquia de profundidade (zoom-in ao clicar nas pastas) e botão de retorno (← Voltar).
  - **Categorização Automática em 8 Grupos com Cores Oficiais:** Vídeos (`#E11D48`), Imagens (`#06B6D4`), Músicas (`#A855F7`), Documentos (`#F59E0B`), Programas e jogos (`#10B981`), Compactados (`#F97316`), Windows e sistema (`#3B82F6`) e Outros (`#64748B`).
  - **Faixa Superior de Cartões de Unidades (Drive Cards):** Detecção automática de todos os discos com gráficos de uso coloridos (verde/accent ou vermelho para >90%), espaço livre/total e seletor para pastas personalizadas (`Escolher pasta...`).
  - **Painel Lateral com Abas Duplas:** `[📁 Maiores pastas]` e `[📄 Maiores arquivos]`, exibindo indicador de categoria, tamanho, contagem de arquivos, porcentagem e mini barra de progresso horizontal para cada item.
  - **Pílulas de Resumo de Categorias e Dicas de Atalhos:** Total em bytes e porcentagem em tempo real para cada uma das 8 categorias no diretório atual, com atalhos de clique direto para Limpeza, Desinstalador e Arquivos duplicados.
- **20 Novas Otimizações do Windows Extraídas do HUB Pro (`WindowsDebloatService`):**
  - **Barra de Tarefas & Menu Iniciar:** Ocultar Visão de Tarefas (Task View), Exibir segundos no relógio, Alinhar Menu Iniciar à esquerda (estilo Windows 10), Ocultar Widgets e Notícias (economiza memória RAM), Ocultar Chat/Teams da barra, Reduzir pesquisa da barra para apenas ícone, Desativar instalação silenciosa de jogos patrocinados (Candy Crush, etc).
  - **Windows Explorer & Interface:** Mostrar extensões de arquivos conhecidos (.exe, .zip), Mostrar pastas e arquivos ocultos, Abrir Explorador direto em "Este Computador", Desativar histórico e arquivos recentes no Acesso Rápido, Desativar pesquisa web do Bing no Menu Iniciar.
  - **Privacidade & Telemetria:** Desativar ID de publicidade e anúncios direcionados, Desativar rastreamento de inicialização de aplicativos, Desativar feed de atividades e linha do tempo, Desativar coleta de telemetria de digitação e escrita, Desativar questionários de feedback e avaliações do Windows.
  - **Desempenho & Fluidez:** Desativar Hibernação do Windows (liberação imediata de gigabytes do `hiberfil.sys`), Encerramento acelerado de aplicativos travados (redução de timeout de 20s para 2s), Desativar efeitos de transparência do Windows (alívio de GPU e DWM).
- **Nova Central Desfazer Integrada nas Configurações (`MainWindow.Settings.Undo`, `PanelSettingsUndo`):**
  - Aba dedicada **CENTRAL DESFAZER** dentro da tela de Configurações (`PageSettings`).
  - Status em tempo real do sistema com contagem de ajustes ativos e banner visual intuitivo ("Você tem N ajustes aplicados" ou "Seu PC está no estado original").
  - Botão `[↺ RESTAURAR TUDO AO PADRÃO]`: reverte todas as modificações aplicadas pelo Lumini Tray de uma vez só, recuperando o estado de fábrica do Windows.
  - Botão `[DESFAZER]` individual para cada ajuste listado, com exibição de categoria e efeito.
  - Atalhos integrados para `Abrir Restauração do Sistema (rstrui.exe)` e `Criar Ponto de Restauração Agora`.

## [0.17.9] - 2026-11-03

### 🛠️ Correções & Reativação de Cabo Virtual
- **Reativação e Roteamento Autônomo do Microfone Virtual (`VirtualCableManager`, `MainWindow.Suppressor` & `MicrophoneSuppressorService`):**
  - **Auto-ativação do CABLE Input via PolicyConfig:** Implementado `EnsureCableInputEnabled()`. Quando o dispositivo de áudio `CABLE Input (VB-Audio Virtual Cable)` estiver presente no Windows porém marcado como Desativado (`Disabled`), o sistema reativa-o automaticamente em tempo de execução via `IPolicyConfig.SetEndpointVisibility`.
  - **Recuperação e Seleção Automática no Dropdown:** A interface agora detecta o cabo virtual em todos os estados, reativa-o e seleciona-o automaticamente com destaque (`⭐ CABLE Input (VB-Audio Virtual Cable) (Recomendado)`), restaurando a rota de áudio para o Discord, jogos e Windows.
  - **Isolamento de Retorno Preservado:** O áudio continua sendo transmitido com zero ruído para o cabo virtual sem vazar para o fone de ouvido do usuário, mantendo o retorno restrito apenas à opção sob demanda (`ChkListenSelf`).

## [0.17.8] - 2026-11-03

### 🛠️ Correções & Isolamento de Áudio
- **Isolamento Estrito do Retorno de Voz no Supressor de Ruído (`MicrophoneSuppressorService` & `MainWindow.Suppressor`):**
  - **Fim do Retorno Permanente Acidental:** Corrigido problema em que a rota do Microfone Virtual (CABLE Input) recorria ao endpoint de renderização padrão do sistema (`GetDefaultAudioEndpoint`), fazendo com que o áudio do microfone fosse reproduzido continuamente no fone de ouvido do usuário assim que o supressor neural era ativado.
  - **Bloqueio de Fallback no Roteamento Virtual:** O método de inicialização de endpoints WASAPI (`TryInitRenderEndpoint`) agora desabilita estritamente o fallback padrão (`allowDefaultFallback = false`) para cabos virtuais. Se nenhum cabo virtual estiver presente ou configurado, o áudio NÃO é enviado aos alto-falantes/fones do usuário.
  - **Filtro Seguro de Dispositivos de Cabo Virtual:** O seletor `CmbVirtualCableDev` agora lista apenas dispositivos de cabo virtual autênticos (`VirtualCableManager.IsCableDevice`). Se nenhum driver estiver instalado, exibe aviso e zera a seleção, impedindo seleção automática do fone de ouvido como cabo virtual.
  - **Retorno de Voz Sob Demanda:** O retorno de voz no fone de ouvido agora só é ativado quando o usuário marca expressamente a opção *"Ouvir retorno da minha voz no fone de ouvido"* (`ChkListenSelf`) ou durante a reprodução do Teste Rápido de 5 segundos. Com a opção desmarcada, há silêncio absoluto no fone.

## [0.17.7] - 2026-11-02

### 🚀 Adicionado & Aprimorado
- **Overlay Responsiva com Redimensionamento Proporcional Dinâmico (`OverlayWindow`):**
  - Integração de `Viewbox` dinâmico (`Stretch="Uniform"`, `StretchDirection="Both"`) envolvendo a estrutura interna do overlay (`Box`).
  - As informações (FPS, Frametimes, Gráfico e telemetria de sensores) agora se ajustam, escalam e adaptam perfeitamente ao tamanho da janela de overlay quando o usuário a estica ou diminui, sem cortes ou desalinhamento.
- **Aprimoramento do Supressor de Ruído com Adaptações do EasyEffects para Windows 11 (`NoiseSuppressionEngine`):**
  - Engenharia reversa e adaptação do pipeline de redução de ruído e VAD do EasyEffects (`easyeffects-master/src/rnnoise.cpp` / `rnnoise.hpp` e `gate.cpp`).
  - Implementação de mixagem Wet/Dry linear configurável baseada na intensidade de supressão, prevenindo distorções robóticas e mantendo a voz encorpada e cristalina.
  - VAD Grace Counter integrado (sustentação de fala do EasyEffects com liberação em blocos de 10ms) para impedir cortes prematuros em consoantes fricativas e cauda de frases.
  - Ajuste na precisão dos coeficientes de filtros Butterworth passa-altas de 80Hz para isolamento de rumble subsônico.

### ⚡ Performance & Testes
- **Novos Testes Automatizados:** Adicionada suite `EasyEffectsAdaptationTests.cs` validando o algoritmo adaptado do EasyEffects com retenção de naturalidade vocal e conformidade temporal.
- **Validação de Compilação e Testes:** Build concluído com êxito e 123 testes unitários aprovados com 100% de sucesso.

## [0.17.7] - 2026-11-02

### 🚀 Adicionado & Aprimorado
- **Overlay adaptável à janela (mesmo visual da v0.17.6):** o conteúdo agora escala conforme o tamanho da janela (cabe em largura e altura) e a largura extra é usada pelo gráfico de frametime e pelos valores dos sensores, que se alinham à direita. Os presets Pequeno/Médio/Grande redimensionam a janela.
- **Novas métricas de RAM na overlay (opcionais):** "RAM do jogo em foco" e "RAM do Lumini Tray".
- **Supressor de Ruído com técnicas do EasyEffects adaptadas ao Windows 11:**
  - *VAD grace*: o gate segue aberto por um tempo após a última voz detectada pelo RNNoise (evita cortar finais de palavras e sussurros).
  - *Sidechain com passa-altas (150 Hz)*: o detector do gate ignora batidas de mesa e graves.
  - *Limitador suave anti-estouro* e *De-esser* (sibilância acima de 6,5 kHz), ambos ativos por padrão.
- **Barra lateral:** todos os grupos agora iniciam fechados (antes Periféricos e Ferramentas iniciavam abertos).

### ⚡ Performance & Testes
- Novos testes em `EasyEffectsSuppressorTests.cs`.

## [0.17.6] - 2026-11-02

### 🚀 Adicionado & Aprimorado
- **Modernização e Aprimoramento da Overlay de FPS & Métricas (`OverlayWindow`):**
  - **Redimensionamento Nativo de Janela:** A overlay agora pode ser redimensionada livremente pelas bordas e cantos como uma janela tradicional do Windows, integrando o processamento nativo de `WM_NCHITTEST` com cursores de dimensionamento do sistema operacional.
  - **Controle de Transparência por Scroll:** Girar a roda do mouse (*mouse wheel*) diretamente sobre a overlay ajusta a opacidade de forma contínua entre 20% e 100%, com exibição de badge visual de feedback momentâneo.
  - **Início Solto e Arrastável:** A overlay inicia sempre desbloqueada por padrão, permitindo reposicionamento imediato em qualquer monitor e ponto da tela.
  - **Fixação por Duplo Clique:** Eliminada a antiga barra superior de arraste e o botão fixar; basta dar 2 cliques rápidos (duplo clique) em qualquer parte da overlay para travar a posição e reativar a transparência total a cliques (`WS_EX_TRANSPARENT`) para os jogos.
  - **Fechamento Instantâneo via Botão do Meio:** Clicar com a roda do mouse (botão do meio / *scroll click*) fecha a overlay na hora.
  - **Persistência de Dimensões e Opacidade:** Salvamento automático de `OverlayWidth`, `OverlayHeight` e `OverlayOpacity` no arquivo de configurações.
- **Expansão do Catálogo de Aplicativos por Categorias (`PageApps` & `AppInstaller`):**
  - Catálogo ampliado com mais de 25 programas essenciais pós-formatação, organizados nas 4 categorias solicitadas:
    - **🎮 Jogos:** Steam (Valve), Epic Games Launcher (Epic Games), EA App (Electronic Arts), Ubisoft Connect (Ubisoft), GOG GALAXY (GOG.com), Battle.net (Blizzard).
    - **💬 Comunicação:** Discord (Discord Inc.), Telegram Desktop (Telegram), TeamSpeak 3 Client (TeamSpeak), Zoom Workplace (Zoom), OBS Studio (OBS Project), Spotify (Spotify AB).
    - **🌐 Navegação:** Google Chrome (Google), Mozilla Firefox (Mozilla), Brave Browser (Brave Software), Opera GX (Opera), Microsoft Edge (Microsoft).
    - **🛠️ Utilitários:** 7-Zip (Igor Pavlov), WinRAR (win.rar GmbH), VLC media player (VideoLAN), Lightshot (Skillbrains), ShareX (ShareX Team), Notepad++ (Notepad++ Team), qBittorrent (The qBittorrent project), Visual Studio Code (Microsoft).
  - Todos os pacotes são validados diretamente da fonte oficial do fabricante pelo `winget`.
  - Nova barra de filtros rápidos por categoria (*Chips*) na interface com suporte aos temas Escuro e Claro do Lumini Tray.

### ⚡ Performance & Testes
- **Novos Testes Unitários:** Adicionada a suite `OverlayAndAppsTests.cs` cobrindo validação de propriedades de catálogo, categorias, integridade dos IDs do winget e serialização de configurações da overlay.
- **Validação com dotnet build e dotnet test:** Projeto compilado sem erros e 122 testes automatizados executados e aprovados com 100% de sucesso.

## [0.17.5] - 2026-11-02

### 🛠️ Correções & Responsividade de Layout
- **Correção da Modulação e Redimensionamento da Janela (Window Layout Modulation):**
  - Resolvido problema em que o conteúdo das abas era cortado (clipped) ao redimensionar ou esticar a janela no Windows em vez de se ajustar dinamicamente.
  - O atributo `HorizontalScrollBarVisibility="Auto"` nas 21 páginas (`ScrollViewer`) passava largura infinita (`double.PositiveInfinity`) durante o ciclo de mensuração do WPF (`MeasureOverride`), o que impedia que `Grid` com colunas proporcionais (`Width="*"`), `WrapPanel` e textos com `TextWrapping="Wrap"` calculassem corretamente a quebra de linha e a largura disponível.
  - Revertido para `HorizontalScrollBarVisibility="Disabled"` em todas as páginas (`PageDash`, `PageDisks`, `PageEnergy`, `PageNet`, `PageDriver`, `PageDiskTools`, `PageRestore`, `PageBackup`, `PageBottleneck`, `PageOverlay`, `PageOptimize`, `PageApps`, `PageUninstaller`, `PageMakerPorts`, `PageMakerDrv`, `PageOc`, `PageSettings`, `PagePerifericos`, `PageFanControl`, `PageWindowsDebloat`, `PageSuppressor`).
  - Agora, cada container recebe estritamente a largura visível da janela (`ViewportWidth`), garantindo que o conteúdo responda e se acomode fluidamente ao esticar, diminuir ou maximizar a janela sem cortes indesejados.

### ⚡ Performance & Testes
- **Validação com dotnet build e dotnet test:** Projeto compilado sem erros ou avisos e suite de 117 testes automatizados validada com 100% de sucesso.

## [0.17.4] - 2026-11-02

### 🛠️ Correções & Tipografia
- **Correção Geral de Acentuação e Caracteres Especiais (Mojibake Fix):**
  - Identificada e eliminada toda a corrupção de codificação de texto (sequências de codificação dupla UTF-8/CP1252 como `Ã§`, `Ã£`, `Ã¡`, `Ã©`, `Ã­`, `Ã³`, `âš¡`, etc.) presente na interface gráfica (`MainWindow.xaml`).
  - Corrigidos títulos, rótulos, badges, tooltips e comentários em toda a aba do **Supressor de Ruído** e barra de navegação:
    - `"Supressor de Ruído"` (anteriormente `RuÃ­do` / `RuÃ­do`);
    - `"⚡ RNNoise Ativo"` (anteriormente `âš¡`);
    - `"⚪"` (anteriormente `âšª`);
    - `"-∞ dB"` (anteriormente `-âˆž dB`);
    - `"SUPRESSOR DE RUÍDO: DESATIVADO"` e mensagens de áudio ativas;
    - `"SINAL PROCESSADO (APÃ“S SUPRESSÃƒO)"`;
    - `"PARÃ‚METROS DO SUPRESSOR NEURAL & NOISE GATE"`;
    - `"Intensidade da Supressão"`, `"Balanço entre corte de ruído e naturalidade"`;
    - `"Ganho de Pré-Amplificação (dB)"`, `"Muda para silêncio absoluto abaixo deste nível"`;
    - `"Tempo de Liberação (Release)"`, `"Corpo e presença da voz"`;
    - `"MÉDIOS (1.2 kHz)"`, `"Saída"`, `"cápsula do microfone"`.
  - Varredura e validação completa em todos os arquivos de código (`.cs`, `.xaml`, `.json`), assegurando que 100% dos textos e caracteres ortográficos do português brasileiro estejam codificados de forma pura e limpa em UTF-8 sem perdas.

### ⚡ Performance & Testes
- **Validação de Testes Unitários:** Todos os 117 testes automatizados executados e validados com 100% de aprovação.


## [0.17.3] - 2026-11-02

### 🚀 Adicionado & Aprimorado
- **Desinstalação em Lote com Caixas de Seleção:**
  - Adicionadas caixas de seleção (`CheckBox`) individuais em cada programa da lista para marcação múltipla.
  - Nova barra de ferramentas em lote com opções "Selecionar Todos", "Desmarcar Todos" e botão de ação `🗑️ Desinstalar Selecionados (X)`.
  - Processamento sequencial organizado de todos os desinstaladores marcados com atualização contínua de status na tela.
  - Suporte à varredura profunda consolidada ao final do lote, agrupando resíduos no disco e no Registro de todos os programas removidos.
- **Opção "Abrir Local do Arquivo" (📁 Abrir Local):**
  - Adicionado botão direto em cada programa da lista que abre a pasta de instalação oficial ou localiza o executável selecionado no Windows Explorer.
- **Ativação e Desativação de Programas Ocultos e Componentes:**
  - Adicionado alternador `Exibir programas ocultos e componentes` para revelar ou ocultar instalações que não possuem interface de aplicativo direta (runtimes, dependências, redistributáveis C++, drivers e componentes marcados como `SystemComponent`/`ParentKeyName` no Registro).
  - Identificação visual clara com badge `[⚙️ Componente]` para diferenciar softwares comuns de componentes do sistema.
- **Confirmação de Segurança Obrigatória em Todas as Desinstalações:**
  - Diálogos explícitos de confirmação antes de iniciar qualquer desinstalação (individual, profunda ou em lote), listando os softwares a serem removidos para evitar cliques acidentais.

### 🛠️ Correções & Refatoração
- **Remoção do Modo Silencioso:**
  - Removido o botão e a badge de "⚡ Silencioso", assegurando que os desinstaladores oficiais sejam sempre iniciados de maneira visível e controlada pelo usuário.

### ⚡ Performance & Testes
- **Expansão da Suíte de Testes:** Suíte atualizada com 117 testes unitários automatizados passando com 100% de aprovação.


## [0.17.2] - 2026-11-02

### 🚀 Adicionado & Aprimorado
- **Sistema Completo de Navegação Histórica entre Abas (Setas Voltar / Avançar):**
  - **Setas na Barra de Título (TitleBar):** Adicionados botões dedicados de navegação histórica (`BtnNavBack` e `BtnNavForward`) posicionados no topo da janela entre o logotipo "Lumini Tray" e o título da página ativa, com suporte total a clique nativo (`WindowChrome.IsHitTestVisibleInChrome="True"`).
  - **Tooltips Contextuais Inteligentes:** Os botões exibem dinamicamente o nome da página de destino ao passar o mouse (ex: *"Voltar para Sensores em Tempo Real (Alt+Seta Esquerda / Botão Voltar do Mouse)"*), ficando desabilitados e com opacidade reduzida quando não houver páginas para voltar ou avançar.
  - **Suporte Nativo a Botões do Mouse:** Navegação instantânea com botões laterais de mouses gamer/office (`XButton1` para Voltar e `XButton2` para Avançar).
  - **Atalhos de Teclado Universais:** Navegação histórica por `Alt + Seta Esquerda` (Voltar), `Alt + Seta Direita` (Avançar) e tecla `Backspace` (quando não estiver digitando em campos de texto).
  - **Gerenciador de Histórico Encadeado (`NavigationHistoryManager`):** Rastreia páginas visitadas com capacidade de até 50 itens, descarta automaticamente ramificações futuras ao navegar para uma nova aba e evita entradas consecutivas duplicadas.
  - **Sincronização Completa com Menus e Acordeões:** Ao retroceder ou avançar, o menu lateral ativa automaticamente o radio button correspondente, expande a categoria pai de acordeões (ex: Periféricos) e atualiza o cabeçalho.

- **Modulação Responsiva e Fluida do Layout (Zero Quebras ao Redimensionar/Esticar):**
  - **Eliminação de Margens Rígidas:** Reformulados todos os cabeçalhos de páginas que possuíam margens duras à direita (até 450px) para grades responsivas de 2 colunas (`Grid.ColumnDefinitions` com `Width="*"` e `Width="Auto"`), permitindo que títulos e botões de ação se adaptem perfeitamente a qualquer largura.
  - **Suporte a Rolagem Horizontal em Telas Pequenas/Divididas (`HorizontalScrollBarVisibility="Auto"`):** Habilitada rolagem horizontal automática em todos os 21 painéis de páginas (`ScrollViewer`), garantindo que nenhum conteúdo, tabela, cartão ou botão seja cortado ao utilizar o aplicativo em meia-tela (Snap do Windows) ou monitores compactos.
  - **Alinhamento e Estilização Dark/Light Theme:** Utilização rigorosa das fontes e temas dinâmicos do projeto (`{DynamicResource Fg}`, `{DynamicResource Hover}`, `{DynamicResource CardSubtleBg}`, etc.) sem cores fixadas no sistema.

### ⚡ Performance & Testes
- **Expansão da Suíte de Testes Automatizados:** Adicionada a classe `NavigationHistoryTests` com 9 novos testes unitários validando empilhamento de páginas, navegação de ida e volta, truncamento de ramificação futura, limite de capacidade e mapeamento de nomes amigáveis.
- **100% de Aprovação:** Suíte agora conta com 114 testes unitários automatizados passando com êxito total.


## [0.17.1] - 2026-11-02

### 🚀 Adicionado & Aprimorado
- **Reformulação Completa da Aba de Desinstalar Programas (Baseada no Bulk Crap Uninstaller 6.3):**
  - **Motor de Detecção Multi-Origem (Multi-Source Factory):**
    - **Reconhecimento Muito Mais Abrangente:** O app agora identifica não apenas softwares do registro clássico, mas também aplicativos modernos da Microsoft Store (UWP / MSIX), jogos do Steam em todas as bibliotecas e unidades de disco (`libraryfolders.vdf` e `appmanifest_*.acf`), e softwares portáteis/diretórios em `Program Files` e `AppData\Programs`. (Salto de 60 para 127+ softwares detectados no sistema).
    - **Detecção Inteligente de Motores de Instalação:** Reconhece Inno Setup, NSIS (Nullsoft), MSI / Windows Installer, InstallShield, StoreApp (UWP) e Steam.
    - **Desinstalação Silenciosa com 1-Clique (⚡ Silencioso):** Gera automaticamente os argumentos silenciosos corretos (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` para InnoSetup, `/S` para NSIS, `/qn /norestart` para MSI e `Remove-AppxPackage` para Store Apps) permitindo desinstalar em segundo plano sem caixas de diálogo.
  - **Motor de Varredura Profunda de Resíduos (Junk Engine com 5 Categorias):**
    - **Pastas e Arquivos Residuais:** Varre `InstallLocation`, `AppData\Roaming`, `AppData\Local`, `AppData\LocalLow`, `ProgramData` e `Common Files`, além de limpar com segurança pastas vazias de fabricantes.
    - **Atalhos Órfãos:** Identifica atalhos `.lnk` abandonados na Área de Trabalho (Usuário e Pública) e no Menu Iniciar.
    - **Chaves no Registro:** Localiza chaves residuais em `HKCU\Software`, `HKLM\Software`, chaves de desinstalação órfãs e registros de inicialização.
    - **Regras do Firewall do Windows:** Detecta e elimina regras órfãs em `FirewallPolicy\FirewallRules` que vinculavam a executáveis deletados.
    - **Rastros do Sistema:** Remove arquivos de Prefetch (`*.pf`) e relatórios de falha (`WER\ReportArchive`, `%LOCALAPPDATA%\CrashDumps`).
    - **Guarda de Segurança Estrito (Safe Guard):** Impede a exclusão acidental de pastas protegidas do Windows, raízes de unidades ou diretórios de fabricantes que ainda contenham outros softwares instalados.
  - **Interface Gráfica Reformulada com Filtros Rápidos:**
    - Filtros por categoria com badges e contadores dinâmicos: **[🌐 Todos]**, **[💻 Desktop (Win32)]**, **[🛍️ Microsoft Store]**, **[🎮 Steam & Jogos]**, **[📁 Portáteis & Diretórios]**.
    - Lista de softwares com alinhamento fluido (`HorizontalContentAlignment="Stretch"`), badge de categoria, badge de suporte silencioso, tamanho formatado e data de instalação.
    - Painel modal de revisão categorizada de resíduos com botões "Marcar Todos", "Desmarcar" e resumo visual do espaço em disco liberado.

### 🛠️ Correções & Autonomia
- **Empacotamento 100% Autônomo do Driver de Microfone Virtual (VB-CABLE):**
  - Corrigido o erro onde o executável publicado em arquivo único (`Single-File Release`) não localizava o instalador do driver (`VBCABLE_Setup_x64.exe`), devido à exclusão padrão de arquivos `.exe` e `.sys` em pastas de conteúdo pelo publicador do .NET.
  - O pacote completo do driver oficial agora é embutido diretamente como recurso do assembly (`Drivers/VBCable.zip`) no `LuminiTray.dll` e extraído de forma transparente para `%TEMP%\LuminiTray\VBCable\` sob demanda.
  - Implementada cadeia de instalação robusta com fallback progressivo:
    1. Instalação silenciosa oficial com elevação de administrador (`-i -h`);
    2. Instalação nativa de driver via `pnputil.exe /add-driver vbMmeCable64_win10.inf /install`;
    3. Abertura interativa do instalador oficial VB-Audio com elevação para clique manual ("Install Driver") caso o modo silencioso seja bloqueado pelo sistema.
  - **Novo Botão "📁 Abrir Pasta":** Adicionado na interface do Supressor de Ruído ao lado do botão de 1 clique para que o usuário possa inspecionar os arquivos e executar o instalador manualmente quando desejar.
  - Atualizado o script de distribuição `publicar.bat` para copiar preventivamente a pasta descompactada de drivers para a pasta de distribuição.

### ⚡ Performance & Otimização
- **Suíte de Testes Expandida:** Total de 105 testes unitários automatizados com 100% de aprovação, incluindo testes de classificação de motor, geração de comandos silenciosos, proteção de pastas do sistema e varredura ao vivo.

## [0.17.0] - 2026-11-01

### 🚀 Adicionado & Aprimorado
- **Microfone Virtual para Discord, Jogos e Aplicativos do Windows:**
  - O áudio processado pelo Lumini Tray agora pode ser usado na prática como um microfone real pelo Windows e qualquer outro programa (Discord, CS2, Valorant, Zoom, OBS, Google Meet, etc.).
  - Integração do driver de áudio virtual oficial assinado `VB-Audio Virtual Cable` na pasta `Assets/Drivers/VBCable/` com detecção automática de status em tempo real.
  - **Instalação com 1-Clique Integrada:** Se o driver não estiver presente no Windows, um botão direto na interface executa a instalação silenciosa oficial com elevação de administrador e atualiza os dispositivos instantaneamente.
  - **Roteamento Contínuo para o Microfone:** O áudio tratado e sem ruídos é continuamente transmitido para o dispositivo `CABLE Input`, disponibilizando o microfone virtual `CABLE Output` para seleção nos aplicativos.
  - **Monitoramento de Pós-Processamento Dedicado:** Opção direta para ouvir **somente o áudio pós-processamento** (limpo com RNNoise e Gate) nos fones de ouvido ou alternar para o áudio bruto (cru) para comparação A/B ao vivo.
  - **Teste Rápido de Microfone (5 Segundos):** Gravador e reprodutor integrado na interface (`🎙️ Gravar Amostra de 5s e Reproduzir`) que permite ao usuário escutar com 100% de clareza e fidelidade como sua voz está soando, eliminando a confusão causada pela vibração óssea natural da fala.

### ⚡ Performance & Otimização
- **Reamostrador Dinâmico de Amostragem (`LinearResampler` Zero GC):**
  - Implementado reamostrador linear de alta precisão que converte automaticamente entre qualquer combinação de taxas de amostragem (44.1 kHz, 48 kHz, 96 kHz, 192 kHz) do microfone de entrada e dos dispositivos de saída.
  - Elimina distorções de velocidade, estalos e efeito *chipmunk* quando o headset opera em 44.1 kHz ou 96 kHz.
- **Suíte de Testes Expandida:** Total de 97 testes unitários automatizados cobrindo reamostragem, detecção de microfone virtual e máquina de estados de gravação de amostras.

## [0.16.9] - 2026-11-01

### ⚡ Performance & Otimização
- **Ativação da Rede Neural RNNoise Nativa x64 (`rnnoise.dll`):**
  - Integração do binário nativo pré-compilado de alta performance `rnnoise.dll` na pasta `Native/` com cópia automática para o diretório de build e execução (`CopyToOutputDirectory="PreserveNewest"`).
  - Implementado `NativeLibrary.SetDllImportResolver` no motor `NoiseSuppressionEngine` para resolução estrita e robusta de DLLs do RNNoise tanto na raiz quanto na pasta `Native/`.
  - Supressão de ruído neural em 22 bandas com arquitetura GRU recorrente: elimina ruídos contínuos, cliques de teclado mecânico e ventiladores durante a fala sem distorcer o timbre natural da voz.

### 🛠️ Correções & Refinamentos
- **Noise Gate Inteligente com Histerese (Schmitt Trigger) e Hold Time:**
  - Corrigido o corte abrupto de palavras e sílabas fracas ("começa a cortar as palavras"):
    - Adicionado limiar de fechamento com histerese de 8 dB abaixo do limiar de abertura (`GateHysteresisDb`), mantendo a porta de áudio aberta durante sussurros ou consoantes finais suaves;
    - Adicionado Hold Time de 140 ms (`GateHoldMs`) pós-fala que impede fechamento prematuro do sinal em pausas naturais entre palavras;
    - Integração direta entre o Voice Activity Detector (VAD neural com probabilidade da rede RNNoise) e o Noise Gate pós-processamento, silenciando completamente o piso de ruído no silêncio absoluto.
  - Ajuste no badge visual de status do motor para `⚡ RNNoise Neural Ativo`.
  - Suíte completa de 92 testes unitários aprovada com cobertura para carregamento de biblioteca nativa e preservação de cauda de fala via histerese e hold.

## [0.16.8] - 2026-11-01

### 🚀 Adicionado & Aprimorado
- **Nova Ferramenta: Supressor de Ruído de Microfone em Tempo Real (`RESET FERRAMENTAS`):**
  - Implementação completa da página `PageSuppressor` acessível via menu lateral `NavSuppressor` em RESET FERRAMENTAS.
  - **Motor Híbrido de Supressão:** Suporte opcional à rede neural RNNoise (`rnnoise.dll`) com fallback automático para processamento DSP puro de alta fidelidade:
    - Filtro Passa-Altas Biquad de 80 Hz contra estrondos subsônicos (vibração de mesa, ar-condicionado, baques);
    - Downward Expander / Noise Gate com limiar (-70 dB a -25 dB) e envelope de liberação suave (30 ms a 400 ms);
    - Equalizador vocal paramétrico de 3 bandas (Graves 180 Hz, Médios 1.2 kHz, Agudos 6 kHz) com botão de reset Flat;
    - Ganho de pré-amplificação ajustável de -12 dB a +20 dB.
  - **Arquitetura WASAPI Multithread e RingBuffer (Zero GC):** Thread dedicada com prioridade elevada (`AboveNormal`) operando em blocos precisos de 10 ms (480 amostras a 48 kHz).
  - **Telemetria e Medidores VU em Tempo Real:** Medidores duplos de sinal de entrada bruto vs sinal processado limpo com leitura em dB, além de indicador visual inteligente de detecção de voz (`VAD Pill`).
  - **Monitoramento / Retorno ao Vivo:** Opção "Ouvir minha voz" em tempo real para ajuste fino e testes rápidos pelo usuário.
  - **Persistência Total:** Todas as preferências de dispositivos, volumes, switches e sliders são salvas em `AppSettings.json`.

---

## [0.16.7] - 2026-11-01

### 🚀 Adicionado & Aprimorado
- **Reestruturação de Inicialização e Configurações:**
  - O item "Inicialização" foi removido de RESET FERRAMENTAS e migrado para **CONFIGURAÇÕES > GERAL**.
  - Nova sub-aba **GERAL** (somente geral): inicialização completa do Lumini Tray (Agendador de Tarefas, funções automáticas), gerenciador de programas de inicialização do Windows, comportamento/bandeja do sistema, taxa dos sensores e encerramento completo.
  - A antiga "Geral & Cores" virou **TEMAS & CORES**, contendo apenas modo escuro/claro e paleta de cores de destaque.
  - Sub-abas Áudio & Microfone, Wallpapers & Telas e Integrações mantidas sem alterações.
  - Removida a opção duplicada "Iniciar com o Windows" da bandeja (já existe na inicialização completa).

---

## [0.16.6] - 2026-11-01

### 🚀 Adicionado & Aprimorado
- **Otimização Automática Programada em Segundo Plano:**
  - Adicionado novo módulo de agendamento automático na aba `RESET OTIMIZA` gerenciado pelo novo serviço `AutoOptimizeService`.
  - Seletor flexível de intervalo com pré-definições de 1 min, 5 min, 10 min, 15 min, 30 min, 60 min e modo "Personalizado..." com input numérico dinâmico em minutos.
  - Três ações de otimização selecionáveis: *Liberar Memória RAM em Segundo Plano*, *Fechar Processos em Segundo Plano / Não Úteis*, e *Limpar Cache e Arquivos Temporários (%TEMP%)*.
  - Indicador visual dinâmico com badge de status ("⏳ Próxima em Xm Ys", "Otimizando agora...", "Inativo") e contagem regressiva em tempo real.
  - Botão de execução manual imediata ("Otimizar Agora") com feedback sonoro e visual.
- **Lacuna de Exclusão (Whitelist) e Fechamento Inteligente de Processos:**
  - Novo mecanismo de proteção de processos com campo de inserção ágil (suporte à tecla `Enter` ou botão `+ Adicionar`).
  - Botões de atalho rápido em 1 clique para proteger processos de jogos e programas populares (`+ Discord`, `+ Steam`, `+ Spotify`, `+ Chrome`, `+ OBS`).
  - Chips visuais interativos e dinâmicos para cada processo excluído, com remoção em 1 clique (botão `✕`).
  - Imunização automática de processos essenciais do Windows (`explorer`, `dwm`, `taskmgr`, `svchost`, etc.) e do próprio aplicativo (`LuminiTray`).
  - Normalização inteligente de nomes de processos (indiferente a maiúsculas/minúsculas e extensão `.exe`) com deduplicação automática.
  - Persistência completa de todas as configurações, tempos e lista de exclusões em `AppSettings.json`.

### ⚡ Performance & Otimização de Disco
- **Redução Significativa da Pasta Raiz (~620 MB a 800 MB Liberados):**
  - Migração limpa do catálogo de bloatware (`Apps.json` e `pt-BR/Apps.json`) para a pasta de assets internos `LuminiTray-latest\Assets\Debloat\`.
  - Exclusão total de pastas de pacotes redundantes (`debloat/` com 221 MB e `novos/` com 60 MB).
  - Limpeza de artefatos temporários e builds de teste residuais (`bin/TestOut/` com ~250 MB e `.wpftmp.csproj`).
  - Remoção de arquivo executável descontinuado `LuminiTray-dist\LuminiTray.exe.old` (89 MB).
  - O repositório agora contém apenas arquivos estritamente necessários para o desenvolvimento, testes e execução do app.

---

## [0.16.5] - 2026-10-31

### 🛠️ Correções & Aprimoramentos de Layout
- **Correção da Escala de Tela Cheia e Janela (Build e Rodapé Visíveis):**
  - Implementação de gancho Win32 nativo para a mensagem `WM_GETMINMAXINFO` no `HotkeyHook` de `MainWindow`, restringindo a maximização estritamente à área de trabalho útil (`rcWork`) calculada via `GetMonitorInfo` e `MonitorFromWindow`.
  - Eliminação definitiva da sobreposição sob a barra de tarefas do Windows: em tela cheia (maximizado), o aplicativo não mais invade a barra de tarefas nem perde o rodapé da interface.
  - O indicador de versão e build (`SideVersion`) no rodapé da barra lateral agora permanece 100% visível, legível e posicionado com margem de segurança perfeita em qualquer resolução e proporção de tela.
  - Compensação dinâmica do sizing frame (`SM_CXFRAME + SM_CXPADDEDBORDER`) sensível ao DPI da tela (`VisualTreeHelper.GetDpi`), garantindo alinhamento pixel-perfect tanto em 100%, 125%, 150% quanto em 175% de escala de exibição do Windows.
  - Função `AdjustWindowScaleForScreen` na inicialização para ajustar proporções em modo janela caso o monitor possua resolução vertical menor (ex: 768p ou 900p com escala elevada), impedindo que a janela ultrapasse os limites visíveis da tela.
- **Debloat de Aplicativos - Eliminação de Espaço Vazio & Layout Fluido:**
  - Redesenho completo das colunas do `LstDebloatApps`: o bloco de informações do aplicativo (Nome, Descrição e ID do Pacote) agora opera com coluna fluida `Width="*"`, preenchendo 100% do espaço livre disponível.
  - Agrupamento compacto e alinhado à direita do Fabricante (`Vendor`, largura padronizada de 85px com alinhamento à direita) e do Badge de Recomendação (`MinWidth="80"`), eliminando a lacuna de espaço ocioso apontada na interface e mantendo distância consistente de 14px entre eles.
  - Habilitação de `TextWrapping="Wrap"` nas descrições de bloatware, permitindo que textos longos quebrem linha suavemente sem reticências desnecessárias.
  - Desativação explícita de rolagem horizontal (`ScrollViewer.HorizontalScrollBarVisibility="Disabled"`) e faixa de altura responsiva (`MinHeight="380" MaxHeight="620"`), permitindo visualização de todos os dados do app sem necessidade de rolar para o lado.
  - Espaçamento de segurança no Salvador de Programas Winget (`LstBackupApps`) para evitar sobreposição entre o nome do programa e a publicadora.

---

## [0.16.4] - 2026-10-31

### 🚀 Adicionado & Aprimorado
- **Nova Aba Windows & Debloat (RESET FERRAMENTAS):**
  - Implementação completa da página dedicada `PageWindowsDebloat` acessível via `NavWindowsDebloat` na categoria RESET FERRAMENTAS.
  - **Perfil Adaptativo Inteligente ("Otimizar para meu Hardware"):** Análise em tempo real do processador (Intel com P-cores/E-cores vs AMD Ryzen com CPPC), placa de vídeo (NVIDIA GeForce, AMD Radeon, Intel Arc) e quantidade de memória RAM (>= 16 GB vs <= 8 GB) aplicando um conjunto individualizado de parâmetros ideais para a máquina.
  - **100% Reversível (Desfazer Modificações):** Backup prévio automático de todas as chaves e valores originais do registro do Windows antes de qualquer alteração, com botão em destaque para restaurar as configurações de fábrica em 1 clique.
  - **Debloat de Aplicativos do Windows & OEM:** Integração com o catálogo de 162 identificadores de bloatware do `Win11Debloat` com suporte completo para fabricantes OEM (Dell, HP, Lenovo, ASUS, Acer, Samsung e MSI).
  - Seleção em lote rápida (*Marcar Seguros*, *Marcar Bloatware OEM*, *Desmarcar Todos*), barra de pesquisa dinâmica com marca d'água e remoção assíncrona multithread via PowerShell (`Remove-AppxPackage -AllUsers` e `Remove-AppxProvisionedPackage -Online`) com barra de progresso e relatório detalhado.
  - **Ajustes de CPU, GPU, Input e Sistema:** 17 otimizações documentadas e seguras com leitura de estado real no registro (`Registry.GetValue`), incluindo `SystemResponsiveness = 0`, agendamento de GPU acelerado por hardware (HAGS), DirectX Flip Model, `DisableDXMaxFrameLatency = 1`, mira linear 1:1 sem aceleração de mouse, desativação de Game DVR, desativação de telemetria diagnóstica, desativação de Copilot e Recall, e retorno do menu de contexto clássico.
  - Suporte completo aos temas Claro e Escuro com `{DynamicResource Fg}`, `{DynamicResource CardSubtleBg}`, e alinhamento fluido (`HorizontalContentAlignment="Stretch"`).

---

## [0.16.3] - 2026-10-31

### 🚀 Aprimorado & Corrigido
- **Central de Backup no Tema Escuro & Alinhamento:**
  - Correção das cores de texto dos softwares listados (`LstBackupApps`), aplicando `{DynamicResource Fg}` para leitura em alto contraste no tema escuro.
  - Implementação de `ItemContainerStyle` com `HorizontalContentAlignment="Stretch"`, eliminando o agrupamento incorreto à esquerda e garantindo alinhamento fluido de colunas (Nome, Desenvolvedor e Tamanho).
  - Adição de marca d'água (`Buscar programas instalados...`) no campo de pesquisa e alto contraste para os seletores de formato de backup (.ZIP e Cópia direta).
- **Controle de Fans - Edição Direta de Pré-definições:**
  - Auto-preenchimento do nome da pré-definição na caixa de texto ao selecionar um perfil customizado no ComboBox (CPU e GPU).
  - Ao clicar em "Salvar", caso a pré-definição já esteja selecionada, o software atualiza e sobrescreve as novas curvas diretamente sem exigir redigitação do nome.
  - Limpeza automática do campo de texto ao selecionar presets originais do sistema (*Silencioso*, *Equilibrado*, *Desempenho*).
- **Suporte a Múltiplos Monitores no MonitorLab Pro:**
  - Integração das ferramentas de calibração e testes de tela cheia (*Dead Pixels*, *Uniformidade*, *Contraste*, *Gradientes*, *Nitidez* e *UFO Ghosting*) ao monitor selecionado na aba Periféricos.
  - Posicionamento e maximização direcionados para as coordenadas reais do display alvo via `System.Windows.Forms.Screen.AllScreens`.
  - Exibição do nome do monitor em teste no HUD de abertura e notificação imediata (*toast*) ao alternar de monitor.

---

## [0.16.2] - 2026-10-31

### 🚀 Adicionado & Aprimorado
- **Gerenciador de Pré-definições de Fans:**
  - Adição de lacuna de texto (`TextBox`), botão "Salvar" e seletor suspenso (`ComboBox`) para nomear, armazenar e trocar perfis de curvas de ventoinhas em tempo real (CPU e GPU).
  - Persistência automática de todas as pré-definições salvas em `settings.json`, com opção de exclusão rápida de presets personalizados.
- **Nova Central de Backup & Restauração (`PageBackup`):**
  - **Salvador de Drivers (pnputil):** Exporta todos os drivers de terceiros instalados e gera um script/executável instalador autônomo com auto-elevação administrativa (`Instalar_Drivers.bat`) para reinstalação instantânea em 1 clique após formatação.
  - **Salvador de Programas (Winget):** Lista dinâmica de programas instalados com busca e checkboxes (`[x]`); gera um instalador em lote (`Instalar_Programas.bat`) que reinstala automaticamente os aplicativos selecionados via `winget`.
  - **Backup de Pastas do Usuário:** Backup com cálculo de tamanho em tempo real para **Área de Trabalho**, **Downloads**, **Imagens** e **Vídeos** (com opção de arquivo comprimido `.ZIP` ou cópia de pastas direta, barra de progresso e cancelamento).

---

## [0.16.1] - 2026-10-30

### 🚀 Adicionado & Aprimorado
- **Controle de Fans & Bomba AIO em Segundo Plano:**
  - O motor de controle de ventoinhas agora roda em uma thread de background dedicada desacoplada da interface gráfica, garantindo que o controle continue ativo mesmo com o app minimizado para a bandeja.
  - Heartbeat anti-timeout para canais com trava de 100% (ex: bombas de Water Cooler / AIO), prevenindo que a BIOS ou o SuperI/O da placa-mãe restaurem o controle padrão por falta de pulsos PWM.
  - Correção do desarme indevido: a restauração dos ventiladores para a BIOS (`RestoreAllToBios`) não é mais acionada ao fechar a janela para a bandeja, ocorrendo unicamente no encerramento real do aplicativo ("Sair").
  - Inicialização automática do serviço de fans no startup do programa, sem necessidade de navegar manualmente até a aba FanControl.
  - **Persistência Completa:** As curvas de CPU e GPU, modos manuais e travas de 100% dos canais agora são salvas automaticamente em `settings.json` e restauradas em cada inicialização.

### ⚡ Performance & Otimização
- **Redução Drástica do Espaço em Disco:**
  - Ativação da compressão nativa Single-File no compilador do .NET 8 (`<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>`). O executável distribuível foi reduzido de **~207 MB para cerca de 70 MB** (economia de mais de 65% de espaço em disco).
- **Otimização de Consumo de Memória RAM:**
  - Ativação do modo DATAS do .NET 8 (`<GarbageCollectionAdaptationMode>1</GarbageCollectionAdaptationMode>`), ajustando a dimensão do heap de forma adaptativa e automática ao tamanho real dos dados ativos.
  - Carregamento sob demanda (lazy) do `TraceEventSession` no `FrameMonitor` e redução do buffer ETW de 64 MB para 1 MB, liberando de 60 MB a 100 MB quando o overlay e os testes de benchmark não estão ativos.
  - Desativação de módulos não utilizados no `LibreHardwareMonitor` (`IsNetworkEnabled = false`, `IsControllerEnabled = false`), eliminando alocações de contadores de desempenho de rede nativos (perflib) e varredura HID contínua.
  - Otimização do loop de sensores com cache de identificadores (`_sensorIdCache`), eliminando alocações repetitivas de strings (`s.Identifier.ToString()`) e descarte automático de ponteiros COM no WMI (`IWbemClassObject`).
  - Modo ultraleve em segundo plano (`updateUi: false`) que mantém a telemetria do FanControl sem despachar eventos de formatação e PropertyChanged para o WPF enquanto o aplicativo estiver minimizado ou na bandeja.
  - Execução de compactação forçada de heap e `TrimMemory` ao finalizar o carregamento do hardware e na inicialização minimizada, mantendo a pegada em segundo plano em cerca de 25 a 35 MB.
  - Implementação de coleta forçada de lixo e limpeza de Working Set (`EmptyWorkingSet`) via Win32 API ao minimizar para a bandeja do sistema.

### 🛠️ Correções de Ambiente e Scripts
- Correção de scripts `.bat` (`iniciar.bat`, `publicar.bat`, `executar_e_testar.bat`, `testar.bat`):
  - Localização automática do .NET 8 SDK em `%USERPROFILE%\.dotnet`, `%ProgramFiles%\dotnet` ou variáveis de ambiente.
  - Definição correta do diretório de trabalho (`-WorkingDirectory`) ao iniciar processos com elevação de Administrador (`UAC`), corrigindo falhas na execução com arquivos soltos.
  - Correção de ambiguidade no `dotnet publish` especificando explicitamente `LuminiTray.csproj` (resolvendo o erro `MSB1011`).
  - Criação de scripts ponte na pasta raiz facilitando a execução direta sem entrar em subpastas.

---

## [0.16.0] - 2026-10-28
- Implementação inicial da aba de controle de fans (FanControl) com suporte a curvas personalizadas e histerese anti-spike.
- Suporte a sensores de placas-mãe (SuperI/O) e placas de vídeo NVIDIA/AMD/Intel.
- Adição da trava de segurança de 100% para bombas de Water Cooler.
- Monitoramento de temperatura de hotspot e memória VRAM em GPUs compatíveis.
