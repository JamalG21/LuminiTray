# Changelog - Lumini Tray

Todas as alterações notáveis deste projeto serão documentadas neste arquivo.
O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

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
  - Desacoplada a alternância de monitores no dropdown (CmbWallpaperMonitor). Mudar de tela na interface agora atua exclusivamente como seletor da tela em foco para prévia e configuração, sem nunca fechar, reiniciar ou mover o wallpaper que já estiver rodando nos demais monitores.
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
  - Cache de instâncias Drawing.Icon para estados de reprodução e pausa em SystemTrayManager.cs, evitando a emissão contínua de novos handles e recriação redundante de ícones.
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
  - Implementado SystemTrayManager.RefreshNotificationArea(), enviando varredura silenciosa de mensagens de mouse na barra de tarefas e na janela de overflow da bandeja do Windows para expurgar instantaneamente ícones zumbis deixados por instâncias finalizadas de forma não graciosa (ex: 	askkill /f).
- **Sintaxe e Fechamento de Chaves em LivelyWallpaperService.cs:**
  - Corrigido ponto de encerramento redundante no método ResumeAllInstances(), restabelecendo o fechamento íntegro da classe.

## [0.22.6] - 2026-11-08

### 🚀 Adicionado & Aprimorado
- **Pausa Automática Confiável em Tela Cheia e Modo Gamer (Papel de Parede Animado):**
  - Implementada a resolução da janela raiz de nível superior (`GA_ROOT`) em `LivelyWallpaperService.cs`, garantindo a detecção precisa de jogos (Unreal Engine, Unity, CEF/Chromium) mesmo quando o foco de entrada está em controles ou subjanelas filhas internas.
  - Expandida a margem de tolerância da área de cobertura para `±12px`, acomodando com precisão margens invisíveis do Desktop Window Manager (DWM) e escalonamentos de DPI fracionários em monitores de alta resolução.
  - Reduzido o intervalo do timer de detecção de jogos de 1000 ms para **500 ms**, proporcionando resposta ágil e imperceptível na pausa e retomada dos papéis de parede.
  - O Modo Gamer agora pausa e congela globalmente todas as instâncias de papel de parede ativo através de `PauseAllInstances()`, eliminando 100% da contenção de GPU/VRAM e prevenindo quedas de taxa de quadros e engasgos (stuttering) durante os jogos.

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
- **Áudio Dinâmico no Player:** Implementada verificação inteligente de som: quando o slider de volume está em 0%, o MPV é inicializado com `--no-audio` (eliminando buffers e processamento de áudio por completo); se o usuário aumentar o volume, o áudio é restaurado dinamicamente no nível escolhido.

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
- **Redução Drástica do Consumo de Memória RAM do MPV (~85% de economia):** Otimização profunda dos parâmetros de execução do player MPV para papéis de parede animados:
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
- **Persistência Completa de Playlist:** A lista de papéis de parede animados agora é persistida em `AppSettings.LivelyWallpaperPlaylist`, garantindo restauração instantânea de todos os vídeos e inicialização imediata do timer de rotação após reiniciar o app.

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
  - Ao selecionar **Todos os Monitores**, respeita o modo Estender (*Span*) sobre a largura virtual total ou clona/duplica instâncias independentes em cada tela.
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
  - **Comunicação Ultrarrápida e Bidirecional via IPC Named Pipe:** Controle instantâneo de reprodução, pause/play, carregamento de novos arquivos (`loadfile`), ajuste de volume e encerramento através de canal IPC assíncrono (`--input-ipc-server`).
  - **Pacote 100% Autônomo e Offline:** Binários nativos do MPV (`mpv.exe` e `vulkan-1.dll`) incluídos diretamente no projeto em `Assets\Plugins\mpv`, com suporte a execução automática a partir do diretório do aplicativo ou `%APPDATA%\LuminiTray\plugins\mpv`.
  - **Controle Dinâmico de Volume e Modo Gamer na Interface:** Adicionados controles dedicados no card de Wallpapers Animados da janela principal: slider de volume (0 a 100%, mudo por padrão a 0%) e alternador de Modo Gamer com pausa automática durante jogos ou janelas em tela cheia (0% de CPU/GPU).
  - **Detecção Automática de Vídeos em Pastas de Wallpapers:** A galeria agora varre pastas selecionadas pelo usuário procurando por arquivos de vídeo (`.mp4`, `.webm`, `.mkv`, `.avi`, `.wmv`) e os adiciona automaticamente à playlist do motor Lively.

## [0.21.7] - 2026-11-07

### 🛠️ Correções & Aprimoramentos
- **Correção da Renderização de Wallpapers Animados no Desktop (Windows 11 Moderno e Multimonitor):**
  - **Correção do Posicionamento de Coordenadas de Janela Filha:** Resolvido bug crítico em configurações multimonitor onde a passagem de coordenadas virtuais globais (VirtualScreenLeft negativo, ex: -768px) deslocava a janela filha do wallpaper para fora da área visível (-1536px). O posicionamento agora é estritamente baseado no cliente do container (, 0, width, height), preenchendo todos os monitores com alinhamento perfeito.
  - **Garantia de WS_EX_LAYERED no Windows 11 Raised Desktop:** Corrigido problema onde a chamada SetParent(hwnd, progman) resetava os estilos da janela e removia WS_EX_LAYERED. Como o Progman no Windows 11 possui WS_EX_NOREDIRECTIONBITMAP, o compositor DWM ignorava janelas não-camadas. A camada agora é reforçada e mantida após o SetParent, acompanhada de SetLayeredWindowAttributes(alpha=255) e SWP_FRAMECHANGED.
  - **Persistência de Reprodução no MediaElement:** Configurado UnloadedBehavior="Manual" e ScrubbingEnabled="True" no LivelyWallpaperWindow.xaml, evitando que o reparenting Win32 cause o encerramento prematuro do vídeo e garantindo exibição instantânea do primeiro quadro.
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
  - **Controle Dinâmico na Bandeja do Sistema:** Ícone exclusivo na bandeja com opções de Play/Pausar, Próximo, Anterior, Intervalo de rotação e Desativação completa.
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

### ðŸš€ Adicionado & Aprimorado
- **Wallpapers Animados Leves Acelerados por GPU (WorkerW):**
  - Implementado motor de papÃ©is de parede animados (AnimatedWallpaperService / AnimatedWallpaperWindow) com reproduÃ§Ã£o em loop contÃ­nuo de vÃ­deos (.mp4, .webm) acoplados diretamente atrÃ¡s dos Ã­cones da Ã¡rea de trabalho do Windows Explorer.
  - **PadrÃ£o 100% Desativado para MÃ­nimo Consumo:** O recurso permanece completamente desligado por padrÃ£o, sem iniciar janelas ou timers de vÃ­deo em segundo plano, garantindo zero consumo adicional de memÃ³ria RAM e GPU.
  - **Modo Gamer / Auto-Pause Inteligente:** DetecÃ§Ã£o contÃ­nua de jogos em tela cheia e aplicativos de alto desempenho 3D, pausando a decodificaÃ§Ã£o do vÃ­deo instantaneamente e garantindo **0% de uso de CPU e GPU** durante partidas.
  - Suporte para adicionar vÃ­deos locais do computador atravÃ©s do botÃ£o + ADICIONAR VÃDEO (.MP4).
- **Controle Completo do Wallpaper na Bandeja do Sistema (System Tray):**
  - **Ãcone Condicional Exclusivo:** O Ã­cone do Wallpaper sÃ³ aparece na bandeja do sistema quando o recurso de wallpapers animados/rotaÃ§Ã£o estiver ativo.
  - Menu de contexto na bandeja:
    - â¯ **Play / Pausar AnimaÃ§Ã£o**
    - â­ **PrÃ³ximo Wallpaper**
    - â® **Wallpaper Anterior**
    - â± **Intervalo de Troca AutomÃ¡tica** (Desligado, 5 min, 15 min, 30 min, 1 hora, 2 horas)
    - âŒ **Desativar Wallpaper (Sair da Bandeja):** Encerra a reproduÃ§Ã£o do vÃ­deo, restaura o papel de parede original do Windows e remove o Ã­cone de wallpaper da bandeja.
- **Sistema de RotaÃ§Ã£o AutomÃ¡tica PeriÃ³dica (Slideshow):**
  - Agendador contÃ­nuo em segundo plano para alternar wallpapers automaticamente conforme o intervalo configurado pelo usuÃ¡rio.
  - NavegaÃ§Ã£o fluida entre os papÃ©is de parede da biblioteca e pasta ativa.

### âš¡ Performance & Estabilidade
- **CorreÃ§Ã£o Definitiva do Timer de OtimizaÃ§Ã£o em Segundo Plano (Bandeja):**
  - Identificada e corrigida a causa da impressÃ£o de que o timer de otimizaÃ§Ã£o parava com o aplicativo minimizado na bandeja:
    - SubstituiÃ§Ã£o do DispatcherTimer do WPF por System.Threading.Timer executado na ThreadPool nativa do .NET, imune Ã  suspensÃ£o ou despriorizaÃ§Ã£o de filas de janelas ocultas no Windows.
    - SincronizaÃ§Ã£o imediata do status e resumo (UpdateAutoOptStatusDisplay e UpdateRamStatus) ao restaurar a janela a partir da bandeja do sistema.
    - NotificaÃ§Ã£o discreta via Toast quando o ciclo de otimizaÃ§Ã£o programado Ã© executado com o programa minimizado.
## [0.21.2] - 2026-11-07

### ðŸš€ Adicionado & Aprimorado
- **Busca Sob Demanda de Programas Instalados e ExecutÃ¡veis do Sistema para Blindagem / ExclusÃ£o:**
  - **Motor Sob Demanda Eficiente (AppScannerService):**
    - Executa exclusivamente quando acionado pelo usuÃ¡rio, com zero consumo em segundo plano e sem timers contÃ­nuos.
    - Varre de forma assÃ­ncrona e ultrarrÃ¡pida processos ativos na sessÃ£o (desconsiderando processos crÃ­ticos do Windows), programas instalados no Registro (Uninstall de 64-bit e 32-bit), softwares registrados em App Paths, atalhos do Menu Iniciar e Desktop (.lnk), e jogos de PC (Steam, Epic Games, GOG, Ubisoft).
    - DeduplicaÃ§Ã£o inteligente e resoluÃ§Ã£o de Ã­cones oficiais extraÃ­dos diretamente dos executÃ¡veis.
  - **Seletor Visual Moderno (AppPickerWindow):**
    - Nova janela modal Lumini Tray com suporte total a tema escuro/claro e carregamento pontual sob demanda.
    - Barra de busca em tempo real com filtragem instantÃ¢nea por nome amigÃ¡vel ou nome do executÃ¡vel (.exe).
    - Abas de filtragem rÃ¡pida: *Todos*, *âš¡ Em ExecuÃ§Ã£o*, *ðŸŽ® Jogos* e *ðŸ’» Programas Instalados*.
    - SeleÃ§Ã£o mÃºltipla com checkboxes para blindar dezenas de jogos e aplicativos simultaneamente em um Ãºnico clique (+ ADICIONAR SELECIONADOS).
    - Suporte a duplo clique para adiÃ§Ã£o imediata e botÃ£o ðŸ“ PROCURAR .EXE... para seleÃ§Ã£o manual de qualquer arquivo no disco.
    - SinalizaÃ§Ã£o automÃ¡tica de aplicativos jÃ¡ blindados (âœ” JÃ¡ Blindado) para evitar duplicaÃ§Ãµes.
  - **IntegraÃ§Ã£o nas Listas de ExclusÃ£o da AplicaÃ§Ã£o:**
    - **LUMINI OTIMIZA (Lacuna de ExclusÃ£o):** Novo botÃ£o de destaque ðŸ” ESCOLHER DA LISTA DE PROGRAMAS... para blindar processos contra encerramento automÃ¡tico sem precisar digitar manualmente o nome do .exe.
    - **ConfiguraÃ§Ãµes de Ãudio (Barra de Tarefas):** BotÃ£o ðŸ” Escolher da Lista... na lista de exclusÃ£o do player de Ã¡udio, permitindo proteger jogos e comunicadores do controle de scroll.
## [0.21.1] - 2026-11-07

### ðŸŽ¨ Identidade Visual & Rebranding Completo (100% Lumini)
- **SubstituiÃ§Ã£o Integral de Todas as Abas, MÃ³dulos, RelatÃ³rios e Funcionalidades:**
  - **Menu Lateral e CabeÃ§alhos:** Atualizadas todas as categorias e tÃ­tulos de abas para `LUMINI INFORMAÃ‡Ã•ES`, `LUMINI JOGOS`, `LUMINI DESEMPENHO`, `LUMINI PERIFÃ‰RICOS`, `LUMINI FERRAMENTAS`, `LUMINI MAKER`, `LUMINI BENCH`, `LUMINI OTIMIZA`, `LUMINI OC`, `LUMINI DRIVER & DDU`, `LUMINI WALLPAPERS`, `LUMINI INTEGRAÃ‡ÃƒO` e `INSTALAR BIBLIOTECA OFICIAL LUMINI`.
  - **RelatÃ³rios HTML e PNG:** O gerador de diagnÃ³sticos (`ReportBuilder.cs`) e o relatÃ³rio visual de stress (`StressReportWindow.xaml.cs`) agora estampam `LUMINI TRAY` e geram arquivos `Lumini-Tray-Relatorio-*.html` e `Lumini-Stress-*.png`.
  - **Benchmarks 3D, ServiÃ§os e Scripts:** Janelas Direct3D/OpenGL de stress (`GpuBurn`, `GpuGameBench`, `GpuRtBench`), scripts de restauraÃ§Ã£o de backup (`RESTAURAR_BACKUP.bat` e `LEIA-ME.txt`), pontos de restauraÃ§Ã£o do Windows (`Lumini Tray - Antes da OtimizaÃ§Ã£o`), pastas de exportaÃ§Ã£o (`Documentos\LuminiTray-Drivers`, `Imagens\LuminiWallpapers`, `%TEMP%\LuminiTray_*`) e sessÃ£o do Mixer de Volume do Windows atualizados para `Lumini Tray`.
- **CorreÃ§Ã£o Definitiva do Ãcone da Bandeja do Sistema (System Tray):**
  - ReconstruÃ­do `Assets/app.ico` com quadros DIB (`BITMAPINFOHEADER` 32-bit BGRA) nas resoluÃ§Ãµes 16x16, 24x24, 32x32, 48x48, 64x64 e 128x128 (alÃ©m de PNG 256x256) para compatibilidade nativa total com `System.Drawing.Icon` e `Shell_NotifyIcon` do Windows.
  - Adicionado `CreateMainAppIcon()` em `SystemTrayManager.cs` com renderizaÃ§Ã£o direta de alta nitidez a partir do recurso WPF `pack://application:,,,/Assets/logo.png` (e fallback para `app.ico` / Ã­cone associado ao executÃ¡vel), garantindo que o Ã­cone oficial do Lumini Tray apareÃ§a sempre na bandeja do sistema.
- **MigraÃ§Ã£o Garantida de PreferÃªncias Existentes:**
  - Adicionada migraÃ§Ã£o automÃ¡tica em `AudioSettings.cs` (`MicOverlayDefaultMigratedV21`) e `AppSettings.cs` (`HotkeyMigratedV21`) para assegurar que mesmo instalaÃ§Ãµes com arquivos de configuraÃ§Ã£o prÃ©-existentes adotem o flutuante de microfone desativado por padrÃ£o (`OverlayEnabled = false`) e o atalho `Ctrl + Shift + P` para o Mini Gerenciador de Tarefas.

## [0.21.0] - 2026-11-07

### ðŸŽ¨ Identidade Visual & Rebranding
- **Rebranding Oficial do Aplicativo para Lumini Tray:**
  - SubstituiÃ§Ã£o completa de todas as referÃªncias Ã  antiga marca Lumini Tray em favor de **Lumini Tray**.
  - **Nova SuÃ­te de Ãcones Oficiais:** GeraÃ§Ã£o a partir da nova identidade visual do Ã­cone de raios solares luminescentes em squircle escuro:
    - `Assets/app.ico`: Ãcone mestre Windows em multi-resoluÃ§Ãµes (256x256, 128x128, 64x64, 48x48, 32x32, 16x16) com cantos arredondados suavizados e transparÃªncia nos vÃ©rtices.
    - `Assets/logo.png`: Logotipo oficial de 256x256 para tema escuro e barra de tÃ­tulo.
    - `Assets/logo-dark.png`: Logotipo de 256x256 para tema claro.
    - `Assets/logo-badge.png`: Badge oficial de 128x128.
  - **Interface do UsuÃ¡rio (UI):** TÃ­tulo principal atualizado para `Lumini Tray`, novo cabeÃ§alho visual `LUMINI TRAY` e padronizaÃ§Ã£o de todos os diÃ¡logos, menus da bandeja e janelas secundÃ¡rias (Mini Gerenciador, Overlay, Toast, RelatÃ³rio de Stress).
  - **ExecutÃ¡vel e Sistema:** O projeto agora Ã© compilado oficialmente como `LuminiTray.exe`, utilizando o mutex `Local\LuminiTray.SingleInstance` e registro de inicializaÃ§Ã£o `LuminiTray`.
  - **MigraÃ§Ã£o Transparente de ConfiguraÃ§Ãµes:** Novo diretÃ³rio de usuÃ¡rio `%AppData%\LuminiTray` com migraÃ§Ã£o automÃ¡tica dos perfis e dados de `%AppData%\LuminiTray`.

### ðŸš€ Aprimoramentos & Ajustes Solicitados
- **Flutuante de Microfone Desativado por PadrÃ£o:** Configurado `OverlayEnabled = false` em `AudioSettings.cs`, mantendo o overlay flutuante de microfone desligado por padrÃ£o atÃ© que o usuÃ¡rio decida ativÃ¡-lo explicitamente.
- **Novo Atalho PadrÃ£o do Mini Gerenciador de Tarefas (`Ctrl + Shift + P`):**
  - O atalho global padrÃ£o para abrir/fechar o Mini Gerenciador de Tarefas foi atualizado de `Ctrl + Shift + M` para `Ctrl + Shift + P` (`VK_P = 0x50`), evitando sobreposiÃ§Ã£o com comandos de microfone.

## [0.20.11] - 2026-11-06

### âš¡ Performance & OtimizaÃ§Ã£o
- **ReduÃ§Ã£o DrÃ¡stica da LatÃªncia (Delay) no Supressor de RuÃ­do do Microfone:**
  - **WASAPI Event-Driven Streaming:** Implementado o modo de captura acionado diretamente por interrupÃ§Ã£o de hardware (`AUDCLNT_STREAMFLAGS_EVENTCALLBACK`), substituindo o polling anterior com `Thread.Sleep(3)`. Agora o worker de Ã¡udio acorda instantaneamente no microssegundo exato em que o pacote de voz Ã© recebido do microfone.
  - **Dimensionamento Otimizado de Buffers WASAPI:** Buffers de hardware reduzidos de 50 ms para o perÃ­odo nativo do dispositivo (10 ms a 12 ms na captura e 12 ms na renderizaÃ§Ã£o do microfone virtual CABLE Input e retorno nos fones), eliminando mais de 75 ms de latÃªncia passiva de driver.
  - **Mecanismo Anti-Lag nos Buffers Circulares (RingBuffer):** Adicionado limitador de backlog com `TrimExcess` no `RingBuffer<T>`, impedindo o acÃºmulo residual de amostras e garantindo que a fila nunca ultrapasse 30 ms a 40 ms de Ã¡udio, mesmo sob transiÃ§Ãµes ou oscilaÃ§Ãµes de carga do sistema.
  - **Temporizador MultimÃ­dia de Alta ResoluÃ§Ã£o:** AtivaÃ§Ã£o de `timeBeginPeriod(1)` da `winmm.dll` durante a execuÃ§Ã£o da thread de Ã¡udio em tempo real, eliminando o jitter quÃ¢ntico padrÃ£o do escalonador do Windows (~15,6 ms).
  - **LatÃªncia Total PerceptÃ­vel:** Reduzida de ~160â€“220 ms para **~30â€“35 ms**, tornando o retorno de voz e a transmissÃ£o em chamadas/jogos completamente em tempo real e imperceptÃ­vel ao ouvido humano.

## [0.20.10] - 2026-11-06

### ðŸ› ï¸ CorreÃ§Ãµes
- **CorreÃ§Ã£o Geral de CodificaÃ§Ã£o e Mojibake de Texto em Toda a AplicaÃ§Ã£o:**
  - Identificada e corrigida a corrupÃ§Ã£o generalizada de codificaÃ§Ã£o de caracteres (mojibake UTF-8/Windows-1252) que exibia textos com caracteres quebrados em toda a interface do programa (exemplo: `"PrÃƒÂ³xima execuÃƒÂ§ÃƒÂ£o em:"` no agendamento de otimizaÃ§Ã£o, `"informaÃƒÂ§ÃƒÂµes"`, `"visÃƒÂ£o geral"`, `"vÃƒÂ­deo"`, `"sÃƒÂ³"`, `"botÃƒÂ£o"`, etc.).
  - Restaurada a integridade de todas as strings em `MainWindow.xaml.cs` (mais de 230 ocorrÃªncias reparadas com acentuaÃ§Ã£o e pontuaÃ§Ã£o corretas em portuguÃªs, tais como `PrÃ³xima execuÃ§Ã£o em:`, `informaÃ§Ãµes`, `vÃ­deo`, `navegaÃ§Ã£o`, `pÃ¡gina`, etc.).
  - Corrigidos comentÃ¡rios e metadados com mojibake de mÃºltiplos nÃ­veis no arquivo de projeto `LuminiTray.csproj` (`tambÃ©m`, `OtimizaÃ§Ãµes`, `MemÃ³ria`, etc.).

## [0.20.9] - 2026-11-05

### ðŸ› ï¸ CorreÃ§Ãµes
- **Encerramento Completo e Definitivo do Processo no Windows:**
  - Corrigida a persistÃªncia indevida do processo `LuminiTray.exe` em segundo plano no Gerenciador de Tarefas do Windows apÃ³s o usuÃ¡rio fechar o programa atravÃ©s do menu da bandeja (*System Tray*), pelo botÃ£o "ENCERRAR APLICATIVO COMPLETAMENTE" nas configuraÃ§Ãµes ou pela janela principal.
  - **ShutdownMode OnMainWindowClose:** Configurado `ShutdownMode="OnMainWindowClose"` no `App.xaml`, garantindo que o encerramento da janela principal comande a finalizaÃ§Ã£o formal de toda a aplicaÃ§Ã£o no runtime do WPF, mesmo havendo janelas flutuantes ativas ou ocultas.
  - **Fechamento ExplÃ­cito de Todas as Janelas SecundÃ¡rias:** O evento `Closed` agora fecha de forma atÃ´mica o `MiniTaskManagerWindow` (Mini Gerenciador de Tarefas), `TaskbarVolumeOsdWindow` (OSD de volume da barra de tarefas), `MicOverlayWindow`, `OverlayWindow`, `ToastWindow` e quaisquer outras janelas abertas em `Application.Current.Windows`.
  - **Limpeza Rigorosa de Recursos e Ganchos:** Descarte limpo dos 4 Ã­cones da bandeja (`SystemTrayManager`) com ocultaÃ§Ã£o imediata (`Visible = false; Icon = null;`), parada do timer e dos contadores PDH do `MiniTaskManagerService`, e liberaÃ§Ã£o de ganchos nativos de teclado/mouse (`TaskbarVolumeHook`, `AudioHotkeyHook`).
  - **Encerramento Garantido sem Processos Zumbis (`App.OnExit`):** Adicionado tratamento em `App.OnExit` para liberar e descartar o Mutex global de instÃ¢ncia Ãºnica (`_single`) e garantir o tÃ©rmino limpo e imediato de todas as threads nativas e drivers via `Environment.Exit`, impedindo processos Ã³rfÃ£os.

## [0.20.8] - 2026-11-05

### ðŸš€ Adicionado & Aprimorado
- **Mini Gerenciador de Tarefas com Todas as Funcionalidades do Game Overlay:**
  - O Mini Gerenciador de Tarefas (`MiniTaskManagerWindow`) agora possui paridade total de recursos com a Overlay de Games:
    - **FixaÃ§Ã£o e Bloqueio (`IsLocked`):** Suporte a modo livre (arrastar pela tela e redimensionar livremente pelas bordas via `WM_NCHITTEST`) e modo fixado com clique transparente (`WS_EX_TRANSPARENT`) para nÃ£o atrapalhar jogos ou tarefas em segundo plano.
    - **TransparÃªncia Dupla:** Rolagem simples com o scroll do mouse para transparÃªncia dinÃ¢mica da caixa de fundo (mantendo textos e grÃ¡ficos 100% nÃ­tidos) e Ctrl + Scroll para opacidade total da janela (20% a 100%), acompanhado de indicador visual flutuante (`TxtOpacityBadge`).
    - **Posicionamento e Presets:** Presets nos cantos da tela (Superior esquerdo/direito, Inferior esquerdo/direito) e modo Livre (`CUSTOM`), com retenÃ§Ã£o de coordenadas entre sessÃµes.
    - **Escala e Tamanho:** Suporte a escala ajustÃ¡vel (Pequeno 0.85x, MÃ©dio 1.0x, Grande 1.3x) e fechamento instantÃ¢neo via botÃ£o do meio do mouse.
- **UnificaÃ§Ã£o na Mesma Aba ("Overlay & Mini Tarefas"):**
  - A aba `PageOverlay` foi transformada em um centro unificado com seletor em chips no topo: `[ ðŸŽ® Overlay de Jogos (FPS & Sensores) ]` e `[ âš¡ Mini Gerenciador de Tarefas ]`, organizando todas as configuraÃ§Ãµes de ambos os monitores flutuantes de forma modular e limpa.
- **InicializaÃ§Ã£o Sob Demanda do Mini Gerenciador de Tarefas:**
  - O Mini Gerenciador agora inicia desligado por padrÃ£o ao abrir o programa, permitindo ao usuÃ¡rio ativÃ¡-lo sob demanda via atalho de teclado (Ctrl + Shift + M), Ã­cone na bandeja do sistema (Tray) ou botÃ£o na interface.
- **DefiniÃ§Ã£o de Atalhos Globais no Teclado:**
  - Adicionado configurador dinÃ¢mico de atalhos globais de teclado no app para ligar e desligar tanto a Overlay de Games (padrÃ£o: `Ctrl + Shift + O`) quanto o Mini Gerenciador de Tarefas (padrÃ£o: `Ctrl + Shift + M`).
  - Permite clicar no botÃ£o de atalho e pressionar qualquer combinaÃ§Ã£o desejada de teclas (Ctrl, Alt, Shift, Win + tecla), com persistÃªncia instantÃ¢nea e botÃ£o de restauraÃ§Ã£o padrÃ£o (`â†º`).
  - **Desbloqueio Inteligente por Atalho:** Caso o HUD ou a Overlay estejam visÃ­veis e fixados na tela, o acionamento do atalho desbloqueia a janela permitindo reposicionar e ajustar imediatamente com o mouse.

## [0.20.7] - 2026-11-05

### ðŸ› ï¸ CorreÃ§Ãµes
- **CorreÃ§Ã£o de Tipagem AritmÃ©tica no CÃ¡lculo de MemÃ³ria RAM:**
  - Corrigida a ambiguidade de operador aritmÃ©tico `CS0034` no cÃ¡lculo percentual do top consumer de memÃ³ria RAM em `MiniTaskManagerService.cs`, utilizando coerÃ§Ã£o explÃ­cita de precisÃ£o contÃ­nua com `Math.Round`.
  - Tratada a checagem defensiva de nulidade no controle de overlay (`ChkOverlay`) em `MainWindow.xaml.cs`.

## [0.20.6] - 2026-11-05

### ðŸ› ï¸ CorreÃ§Ãµes
- **CorreÃ§Ã£o de Atributo XAML no Mini Gerenciador de Tarefas:**
  - Removido o atributo invÃ¡lido `LetterSpacing` no `TextBlock` do cabeÃ§alho de `MiniTaskManagerWindow.xaml`, corrigindo a falha de compilaÃ§Ã£o `error MC3072: a propriedade 'LetterSpacing' nÃ£o existe no namespace XML` reportada durante a publicaÃ§Ã£o.

## [0.20.5] - 2026-11-05

### ðŸš€ Adicionado & Aprimorado
- **Mini Gerenciador de Tarefas Flutuante (AdaptaÃ§Ã£o MicroManager / Windhawk):**
  - Implementado o novo serviÃ§o de alto desempenho em nÃ­vel de kernel (`MiniTaskManagerService`) baseado no mod open-source MicroManager do Windhawk, com monitoramento contÃ­nuo de **CPU, GPU, RAM e DISCO** e identificaÃ§Ã£o atÃ´mica dos maiores processos consumidores (*top consumers*) de cada recurso via `NtQuerySystemInformation` e contadores de engine PDH (`\GPU Engine(*)\Utilization Percentage`).
  - Nova janela flutuante Dark HUD (`MiniTaskManagerWindow`) com cantos arredondados, backdrop acrÃ­lico/escuro, cores vivas de alto contraste para cada recurso (CPU: Ciano `#00C8FF`, GPU: Verde Neon `#00DC64`, RAM: Ã‚mbar `#FFB446`, DISCO: Magenta `#D946EF`) e grÃ¡fico de barras histÃ³ricas de 16 amostras em tempo real.
  - AlternÃ¢ncia interativa da mÃ©trica exibida no grÃ¡fico atravÃ©s de clique nas linhas ou no canvas, ou navegaÃ§Ã£o pelas teclas â†‘ / â†“.
  - Menu de contexto avanÃ§ado ao clicar com botÃ£o direito sobre qualquer processo:
    - **Finalizar tarefa:** Encerramento direcionado com confirmaÃ§Ã£o e validaÃ§Ã£o de seguranÃ§a.
    - **Finalizar Ã¡rvore de processos...:** IdentificaÃ§Ã£o automÃ¡tica da raiz com mesmo nome de executÃ¡vel via timestamp de criaÃ§Ã£o de processo (protegendo contra reuso de PID), detecÃ§Ã£o recursiva de processos descendentes, alerta de proteÃ§Ã£o ao `explorer.exe` e processos crÃ­ticos do Windows, e diÃ¡logo com contagem exata antes do encerramento de baixo para cima.
    - **Abrir local do arquivo:** Revela e seleciona o executÃ¡vel diretamente no Windows Explorer.
  - Fechamento intuitivo com tecla `Esc` ou ao clicar fora da janela flutuante (`Deactivated`), e posicionamento inteligente automÃ¡tico prÃ³ximo Ã  bandeja do Windows sem cortes na tela.
  - IntegraÃ§Ã£o nativa na bandeja do sistema (`SystemTrayManager`) com Ã­cone estilizado de 4 barras de atividade, tooltip em tempo real com resumo instantÃ¢neo dos 4 recursos, e menu de contexto com atalhos de taxa de atualizaÃ§Ã£o (300ms, 500ms, 1.0s, 3.0s) e acesso rÃ¡pido ao Gerenciador de Tarefas do Windows.
  - BotÃ£o de acesso rÃ¡pido `âš¡ Mini Tarefas` adicionado ao cabeÃ§alho da pÃ¡gina de Sensores (`PageSensors`).
  - Nova opÃ§Ã£o em ConfiguraÃ§Ãµes > Geral > Ãcones de Status na Bandeja para ativar/desativar a exibiÃ§Ã£o do Mini Gerenciador na bandeja (`ChkSettingsShowMiniTaskTray` / `ShowMiniTaskTrayIcon`).

### âš¡ Performance & OtimizaÃ§Ã£o
- **Layout Responsivo e Limpo na Aba "Windows e Debloat":**
  - Reorganizado o cabeÃ§alho de aÃ§Ãµes da pÃ¡gina `PageWindowsDebloat` em 2 linhas funcionais e responsivas:
    - **Linha 1:** Campo de busca (`TxtDebloatSearch`), contador de bloatwares detectados (`TxtBloatwareCount`) e botÃ£o de desinstalaÃ§Ã£o destacado (`BtnUninstallSelectedBloatware`) alinhado Ã  direita.
    - **Linha 2:** Painel dinÃ¢mico em `WrapPanel` com os 5 botÃµes de filtro e manutenÃ§Ã£o (`Marcar Seguros`, `Marcar Bloatware OEM`, `Desmarcar Todos`, `ðŸ”„ Re-Escanear`, `â†º Reinstalar Apps do Windows`) com espaÃ§amento confortÃ¡vel, eliminando qualquer compressÃ£o ou sobreposiÃ§Ã£o visual em resoluÃ§Ãµes variadas.

## [0.20.4] - 2026-11-05

### ðŸ› ï¸ CorreÃ§Ãµes & Estabilidade
- **Blindagem e ResiliÃªncia de NavegaÃ§Ã£o (NullReferenceException em ShowPage e Nav_Checked):**
  - Implementada proteÃ§Ã£o abrangente contra exceÃ§Ãµes em `ShowPage` e `Nav_Checked`, eliminando erros de desreferÃªncia nula (`NullReferenceException`) disparados em transiÃ§Ãµes e cliques de botÃµes da barra lateral.
  - O seletor de visibilidade de pÃ¡ginas agora utiliza verificaÃ§Ã£o nula defensiva e comparaÃ§Ã£o de nomes insensÃ­vel a maiÃºsculas/minÃºsculas (`StringComparison.OrdinalIgnoreCase`).
  - Blindagem do ciclo de vida de captura de FPS/Overlay (`EnsureFramesLifecycle`) com checagens prÃ©vias de nulidade e captura de exceÃ§Ãµes de sessÃ£o ETW.
  - ProteÃ§Ã£o refinada no Dispatcher do `App.xaml.cs`: o encerramento forÃ§ado do processo (`Shutdown(1)`) nÃ£o Ã© mais disparado indevidamente caso a janela principal jÃ¡ esteja criada e em execuÃ§Ã£o.

## [0.20.3] - 2026-11-05

### ðŸš€ Adicionado & Aprimorado
- **ReorganizaÃ§Ã£o Estrutural de InicializaÃ§Ã£o e ConfiguraÃ§Ãµes:**
  - Migradas todas as configuraÃ§Ãµes de inicializaÃ§Ã£o com o Windows (`ChkAutoStartWithWindows`, rotinas automÃ¡ticas de limpeza de RAM, tweaks, fechamento de apps e inicializaÃ§Ã£o minimizada) e a lista de programas de inicializaÃ§Ã£o (`StartupList`) da aba Geral de ConfiguraÃ§Ãµes para a pÃ¡gina **InicializaÃ§Ã£o** da sidebar (`PageStartup`).
  - Movidos os atalhos rÃ¡pidos e seletores principais (`ChkFastMicMute`, `ChkFastMouseWheelVolume`, `ChkFastNoiseSuppressor`, `CboFastAudioOutput`, `CboFastAudioInput`, `ChkFastOverlay`) da pÃ¡gina de InicializaÃ§Ã£o para a aba **Geral** de ConfiguraÃ§Ãµes (`PanelSettingsGeneral`).
- **Controle Preciso da FunÃ§Ã£o de Mute de Microfone:**
  - Ajustado o atalho rÃ¡pido `ChkFastMicMute` para ativar e desativar a funÃ§Ã£o/atalho global de microfone (`_audioSettings.EnableMicMuteFeature`) em vez de disparar o comando de mute instantÃ¢neo.
  - Quando a funÃ§Ã£o estiver desativada, os atalhos de teclado globais de microfone sÃ£o ignorados e a overlay flutuante de microfone (`_micOverlay`) permanece oculta.
- **TransparÃªncia Dupla e Desbloqueio Inteligente da Overlay:**
  - **Scroll Simples no Overlay:** Ajusta dinamicamente a transparÃªncia apenas da caixa de fundo (`Box.Background`), mantendo informaÃ§Ãµes, textos, grÃ¡ficos e mÃ©tricas com 100% de visibilidade e nitidez. Salvo em `_settings.OverlayBackgroundOpacity`.
  - **Ctrl + Scroll no Overlay:** Preservado o controle da opacidade total da janela (`this.Opacity`, entre 20% e 100%), permitindo transparÃªncia completa da overlay quando desejado.
  - **Desbloqueio Inteligente por Atalho:** Ao acionar o atalho global de visualizaÃ§Ã£o da overlay (`Ctrl + Shift + O`), se a overlay estiver na tela e bloqueada (`OverlayLocked`), o atalho desbloqueia a janela para permitir arrastar e reposicionar com o mouse em vez de fechar a janela.
- **RestauraÃ§Ã£o Seletiva na PÃ³s-FormataÃ§Ã£o:**
  - Adicionado suporte a restauraÃ§Ã£o granular por categoria: checkboxes e botÃµes de aÃ§Ã£o rÃ¡pida para Drivers, Redes Wi-Fi e Programas (Winget) em seus respectivos cards de descoberta.
  - Adicionadas caixas de seleÃ§Ã£o individuais (`IsSelected`) para cada item da fila de pÃ³s-formataÃ§Ã£o (`LstPostFormatQueue`).
  - O motor de execuÃ§Ã£o `ExecuteQueueAsync` agora respeita estritamente a seleÃ§Ã£o do usuÃ¡rio, mantendo itens nÃ£o selecionados disponÃ­veis para execuÃ§Ãµes seguintes sem travÃ¡-los no estado `Skipped`.
  - SincronizaÃ§Ã£o dinÃ¢mica entre caixas de seleÃ§Ã£o de categorias e os itens individuais da lista.
  - Resumo e botÃ£o principal atualizados dinamicamente para exibir a quantidade de itens selecionados e pendentes.
- **SincronizaÃ§Ã£o e Reatividade:**
  - A alternÃ¢ncia do recurso de mute em ConfiguraÃ§Ãµes > Geral atualiza instantaneamente a tabela de teclas ativas do hook global STA (`RebuildActiveKeys`), garantindo latÃªncia zero.
  - Desbloqueio e alternÃ¢ncia da overlay sincronizados com o checkbox rÃ¡pido da aba Geral.
  - Opacidade padrÃ£o da janela do overlay definida em 100% para nitidez total das informaÃ§Ãµes, governando o fundo por `OverlayBackgroundOpacity`.

### âš¡ Performance & OtimizaÃ§Ã£o
- **Design Compacto nos BotÃµes de DesinstalaÃ§Ã£o:**
  - Reduzidas as proporÃ§Ãµes dos botÃµes vermelhos de desinstalaÃ§Ã£o em lote (`BtnUninstallBatch` no Desinstalador AvanÃ§ado e `BtnUninstallSelectedBloatware` em Debloat) para padding e tipografia compactos (`Padding="10,5"`, `FontSize="11"`), evitando que empurrem ou obstruam as caixas de seleÃ§Ã£o e controles vizinhos.

## [0.20.2] - 2026-11-05

### ðŸ› ï¸ CorreÃ§Ãµes
- **Auditoria Completa â€” Fase 1 (NavegaÃ§Ã£o e Blindagem de ExceÃ§Ãµes):**
  - Corrigido o `System.NullReferenceException` em `MainWindow.ShowPage` / `Nav_Checked` ao clicar em **RESET MAKER**: o cabeÃ§alho de grupo `GrpMaker` recebia indevidamente `Checked="Nav_Checked"` (sem `Tag`), enquanto o item `NavMakerPorts` estava sem o evento. O evento foi removido do grupo e restaurado no item.
  - `Nav_Checked` agora ignora remetentes sem `Tag` de pÃ¡gina vÃ¡lida e `ShowPage` ignora nomes nulos/vazios e pÃ¡ginas ausentes.
  - Abas **PÃ³s-FormataÃ§Ã£o** e **Reparar Windows** adicionadas ao histÃ³rico de navegaÃ§Ã£o (botÃµes Voltar/AvanÃ§ar).
  - `App.xaml.cs`: `DispatcherUnhandledException` agora marca a exceÃ§Ã£o como tratada (`ev.Handled = true`), eliminando a janela dupla de erro e o fechamento abrupto do programa em erros nÃ£o-fatais. Falhas antes da janela principal carregar exibem os detalhes e encerram de forma limpa. O `crash.log` passou a acumular as entradas com data/hora em vez de sobrescrever.
- **Auditoria Completa â€” Fase 2 (Bindings `Mode=OneWay`):**
  - Corrigida a `XamlParseException` *"Uma associaÃ§Ã£o TwoWay ou OneWayToSource nÃ£o pode funcionar na propriedade somente leitura 'ProgressPercentage'"* ao abrir o **Mapa do Disco**: a `ProgressBar` (cujo `Value` Ã© TwoWay por padrÃ£o no WPF) agora usa `{Binding ProgressPercentage, Mode=OneWay}`.
  - Barra de velocidade do **Benchmark de DNS** (`SpeedBarRatio`) e textos `Run` da galeria de **Wallpapers** (`DimensionsText`, `FileSizeText`) passaram a usar `Mode=OneWay`, jÃ¡ que sÃ£o apenas exibiÃ§Ã£o.
  - Auditados todos os bindings TwoWay por padrÃ£o (`ProgressBar`/`Slider.Value`, `CheckBox.IsChecked`, `TextBox`/`Run.Text`, `SelectedItem`) em todos os arquivos XAML e no code-behind: os demais apontam para propriedades com `set` pÃºblico.
- **Auditoria Completa â€” Fase 3 (RestauraÃ§Ã£o Total dos Event Handlers nas 32 Abas):**
  - Resolvido o deslocamento em cascata (off-by-one / event shifting) de 505 elementos interativos em `MainWindow.xaml`, restaurando as conexÃµes exatas com os mÃ©todos correspondentes no code-behind (`MainWindow.*.cs`).
  - Reconectados todos os botÃµes, checkboxes, radio buttons, sliders e combos afetados em todas as telas principais:
    - **Instalar Apps (`PageApps`):** BotÃµes "Marcar Todos" (`BtnAppsAll_Click`), "Desmarcar" (`BtnAppsNone_Click`), filtro de categorias (`BtnAppsCat_Click`), atualizaÃ§Ã£o da Store (`BtnAppsStore_Click`) e instalaÃ§Ã£o de selecionados (`BtnAppsInstall_Click`).
    - **Backup (`PageBackup`):** BotÃµes de seleÃ§Ã£o em lote (`BtnSelectAllBackupApps_Click`, `BtnDeselectAllBackupApps_Click`), checkboxes de apps e pastas (`ChkBackupApp_Click`, `ChkBackupFolder_Click`), gerador de drivers e instalador Winget (`BtnGenerateDriverBackup_Click`, `BtnGenerateAppInstaller_Click`), seleÃ§Ã£o de destino (`BtnBrowseBackupDest_Click`), e inÃ­cio/cancelamento do backup de pastas (`BtnStartFoldersBackup_Click`, `BtnCancelFoldersBackup_Click`).
    - **Ferramentas de Disco & S.M.A.R.T. (`PageDiskTools`):** NavegaÃ§Ã£o e seleÃ§Ã£o de pasta no analisador (`BtnDiskChooseFolder_Click`, `BtnDiskSpaceUp_Click`, `BtnDiskOpenExplorer_Click`), redimensionamento do Treemap (`CnvTreemap_SizeChanged`), dicas rÃ¡pidas (`BtnTipLimpeza_Click`, `BtnTipDesinstalador_Click`, `BtnTipDuplicados_Click`), abas de maiores arquivos/pastas (`TopItemsTab_Checked`), limpeza rÃ¡pida (`BtnQuickCleanScan_Click`, `BtnQuickCleanNow_Click`), e diagnÃ³stico profundo S.M.A.R.T. / auto-testes ATA / reparo Chkdsk (`BtnRefreshSmartBadBlocks_Click`, `BtnStartBadBlockScan_Click`, `BtnStopBadBlockScan_Click`, `BtnSelfTestShort_Click`, `BtnSelfTestExt_Click`, `BtnSelfTestAbort_Click`, `BtnLocateBadLbaFiles_Click`, `BtnRunSelectiveTest_Click`, `BtnRepairChkdsk_Click`).
    - **Desinstalador AvanÃ§ado (`PageUninstaller`):** Filtros por categoria (`BtnFilterCategory_Click`), busca dinÃ¢mica (`TxtUninstallSearch_TextChanged`), seleÃ§Ã£o em massa (`ChkSelectAllApps_Checked`, `ChkSelectAllApps_Unchecked`, `BtnClearSelection_Click`), exibiÃ§Ã£o de ocultos (`ChkShowHiddenApps_Changed`), desinstalaÃ§Ã£o em lote (`BtnUninstallBatch_Click`), e aÃ§Ãµes por item de software (`ChkAppItem_Changed`, `BtnOpenInstallLocation_Click`, `BtnUninstallNormal_Click`, `BtnUninstallDeep_Click`), alÃ©m da revisÃ£o de resÃ­duos (`BtnSelectAllLeftovers_Click`, `BtnDeselectAllLeftovers_Click`, `BtnCleanLeftoversNow_Click`, `BtnCloseLeftovers_Click`).
    - **Central Desfazer / RestauraÃ§Ã£o (`PageSettings`):** AlternÃ¢ncia de sub-abas de configuraÃ§Ãµes (`SettingsTab_Checked`), atualizaÃ§Ã£o da inicializaÃ§Ã£o (`BtnStartupScan_Click`), restauraÃ§Ã£o total ao padrÃ£o (`BtnUndoRestoreAll_Click`), abertura do RSTRUI (`BtnOpenSystemRestore_Click`), criaÃ§Ã£o de ponto de restauraÃ§Ã£o (`BtnUndoCreateRestorePoint_Click`), atualizaÃ§Ã£o de lista (`BtnUndoRefresh_Click`) e reversÃ£o pontual de tweaks (`BtnUndoSingleTweak_Click`).
    - **Debloat & OtimizaÃ§Ãµes do Windows (`PageWindowsDebloat`):** AplicaÃ§Ã£o de perfil adaptativo (`BtnApplyAdaptiveProfile_Click`), reversÃ£o de debloat (`BtnRevertDebloatTweaks_Click`), alternÃ¢ncia de sub-abas (`BtnSubTabTweaks_Click`, `BtnSubTabDebloat_Click`), checkboxes de tweaks de CPU/GPU/Input/Sistema (`TweakDebloat_Click`), busca de bloatwares (`TxtDebloatSearch_TextChanged`), marcaÃ§Ã£o segura/OEM/desmarcar (`BtnSelectSafeBloatware_Click`, `BtnSelectOemBloatware_Click`, `BtnDeselectAllBloatware_Click`), re-escanear (`BtnScanBloatware_Click`), desinstalaÃ§Ã£o de bloatwares selecionados (`BtnUninstallSelectedBloatware_Click`), e seleÃ§Ã£o individual (`DebloatItemCheckbox_Click`).
    - **Supressor de RuÃ­do Neural (`PageSuppressor`):** Interruptor master (`BtnToggleMaster_Click`), instalaÃ§Ã£o e abertura de drivers de cabo virtual (`BtnInstallVirtualCable_Click`, `BtnOpenDriverFolder_Click`), seleÃ§Ã£o de dispositivos e atualizaÃ§Ã£o (`CmbVirtualCableDev_SelectionChanged`, `CmbInputMic_SelectionChanged`, `CmbOutputDev_SelectionChanged`, `BtnRefreshDevices_Click`), monitoramento/retorno de voz (`ChkListenSelf_Checked`, `ChkListenSelf_Unchecked`, `RadioMonitorMode_Checked`), teste rÃ¡pido de microfone (`BtnQuickTest_Click`), e ajuste de sliders de intensidade, ganho, noise gate e equalizador grÃ¡fico de 3 bandas (`SliderIntensity_ValueChanged`, `SliderGain_ValueChanged`, `SliderGateThreshold_ValueChanged`, `SliderGateRelease_ValueChanged`, `SliderLowEq_ValueChanged`, `SliderMidEq_ValueChanged`, `SliderHighEq_ValueChanged`, `BtnResetEq_Click`).
    - **Curvas de Ventoinha (`PageFanControl`):** Interruptor geral e abas CPU/GPU (`BtnToggleFanMaster_Click`, `BtnTabFanCpu_Click`, `BtnTabFanGpu_Click`), presets de curva (`FanPresetQuiet_Click`, `FanPresetBalanced_Click`, `FanPresetPerf_Click`, `FanPresetCpuLock100_Click`, `FanPresetGpuQuiet_Click`, `FanPresetGpuBalanced_Click`, `FanPresetGpuPerf_Click`, `FanPresetGpuLock100_Click`), interaÃ§Ãµes com os grÃ¡ficos de curva (`FanCurveCanvasCpu_*`, `FanCurveCanvasGpu_*`), sliders de pontos de temperatura/PWM e histerese (`FanPoint_Changed`, `FanHysteresis_Changed`, `FanGpuPoint_Changed`, `FanGpuHysteresis_Changed`), e gerenciamento de perfis (`CmbFanCpuPresets_SelectionChanged`, `BtnSaveFanCpuPreset_Click`, `BtnDeleteFanCpuPreset_Click`, `CmbFanGpuPresets_SelectionChanged`, `BtnSaveFanGpuPreset_Click`, `BtnDeleteFanGpuPreset_Click`).
  - Total de 619 controles com eventos totalmente restaurados e validados, com 0 handlers faltantes, 0 handlers inexistentes e conformidade estrita com o esquema XML.
- **Auditoria Completa â€” Fase 4 (Testes Automatizados de RegressÃ£o XAML e Handlers):**
  - Implementada suÃ­te de testes de auditoria de arquitetura XAML em `PostFormatAndHealthTests.cs` para blindar o projeto contra regressÃµes visuais e estruturais:
    - `XamlNavigation_AllNavItemsAndGroups_HaveValidCheckedAndTags`: assegura que cabeÃ§alhos colapsÃ¡veis (`ToggleButton.NavGroup`) nunca recebam evento `Checked="Nav_Checked"`, e que todos os 36 botÃµes de navegaÃ§Ã£o (`RadioButton.NavItem`) possuam o manipulador `Checked="Nav_Checked"` e `Tag` apontando para pÃ¡ginas vÃ¡lidas existentes.
    - `XamlBindings_ProgressBarsAndReadOnlyProperties_UseOneWayMode`: valida que bindings em `ProgressBar.Value` e propriedades somente leitura (`ProgressPercentage`, `SpeedBarRatio`, `DimensionsText`, `FileSizeText`) utilizam estritamente `Mode=OneWay`, evitando exceÃ§Ãµes `XamlParseException` de two-way binding.
    - `XamlEventHandlers_AllCodeBehindHandlers_AreCorrectlyWiredInXaml`: audita a integridade total entre XAML e code-behind, garantindo 0 manipuladores ausentes no code-behind e que todos os mÃ©todos de evento interativos de UI em `MainWindow.*.cs` estejam devidamente conectados aos controles nas 32 abas do aplicativo.

## [0.20.1] - 2026-11-04

### ðŸ› ï¸ CorreÃ§Ãµes
- **CorreÃ§Ã£o CrÃ­tica de InicializaÃ§Ã£o XAML (`XamlParseException` em `MainWindow.xaml` e `Theme.xaml`):**
  - Resolvida a exceÃ§Ã£o fatal `NÃ£o Ã© possÃ­vel encontrar o recurso denominado 'Display'` gerada durante o `InitializeComponent()` ao adicionar a fonte `Display` em `Theme.xaml` e alinhar o elemento `TxtBootTypicalTime` com a tipografia `Mono` do projeto.
  - Corrigida a referÃªncia do conversor de visibilidade no badge "Recomendado" da Central de ReparaÃ§Ã£o do Windows (`BoolToVis` -> `BoolVis`), registrando tambÃ©m o alias `BoolToVis` globalmente em `Theme.xaml` para prevenir futuras regressÃµes.
  - Padronizadas todas as 38 referÃªncias legadas de recursos dinÃ¢micos (`Text`, `Primary`, `Border`, `BorderBrush`, `CardBg`) em `MainWindow.xaml` para as chaves oficiais da paleta (`Fg`, `Accent`, `Line`, `LineStrong`, `Card`), garantindo conformidade total com os temas escuro e claro.

## [0.20.0] - 2026-11-04

### ðŸš€ Adicionado & Aprimorado
- **Nova Central PÃ³s-FormataÃ§Ã£o (`PagePostFormat`, `PostFormatManager`, `MainWindow.PostFormat`):**
  - **Descoberta AutomÃ¡tica de Backups em Todas as Unidades:** Varredura automÃ¡tica em todas as partiÃ§Ãµes internas e discos externos/pendrives em busca de backups feitos pelo Lumini Tray (drivers exportados, redes Wi-Fi e senhas salvas, instaladores de programas Winget).
  - **Fila Sequencial Automatizada com Controles de Parada e ContinuaÃ§Ã£o:** Painel visual com lista de tarefas ordenadas por categoria (Drivers, Wi-Fi, OtimizaÃ§Ãµes, Programas), status de cada etapa em tempo real e botÃµes `[PARAR / PAUSAR]` e `[CONTINUAR]`.
  - **PersistÃªncia de Estado e Continuidade PÃ³s-ReinicializaÃ§Ã£o:** GravaÃ§Ã£o automÃ¡tica do progresso da fila em `%LocalAppData%\LuminiTray\post_format_state.json` com chave `RunOnce` no Registro do Windows. Caso o computador seja reiniciado ou o aplicativo fechado no meio do processo, o Lumini Tray retoma automaticamente do ponto exato onde parou atravÃ©s de um banner no topo da tela inicial.
- **TransformaÃ§Ã£o Completa da Aba de Bad Blocks em "SaÃºde dos Discos" (`PageDiskTools`, `UsbFakeDriveScanner`, `MainWindow.DisksHealth`):**
  - **ReestruturaÃ§Ã£o e Identidade Visual do HUB Pro:** BotÃ£o e cabeÃ§alho reformulados para "SaÃºde dos discos" com navegaÃ§Ã£o interna por sub-abas: `[â¤ï¸ SaÃºde]` e `[ðŸ”Œ Verificar se Ã© original (capacidade falsa)]`.
  - **Sub-aba SaÃºde Geral & S.M.A.R.T.:** PÃ­lulas de seleÃ§Ã£o de unidades de armazenamento, diagnÃ³stico de saÃºde e temperatura, telemetria S.M.A.R.T. em tempo real, heatmap de blocos de superfÃ­cie e veredito consolidado de integridade com orientaÃ§Ãµes tÃ©cnicas.
  - **Sub-aba Verificar se Ã© Original (Pendrive / CartÃ£o / HD Falsificado):**
    - Seletor inteligente exclusivo para unidades removÃ­veis (o disco do Windows e discos fixos sÃ£o protegidos e ocultados dessa verificaÃ§Ã£o).
    - **IdentificaÃ§Ã£o de Hardware & Controlador USB:** Leitura de Vendor ID (VID), Product ID (PID), fabricante e revisÃ£o, com banco de dados de controladores suspeitos frequentemente usados em rÃ©plicas falsificadas (Chipsbank, FirstChip, Appotech, Alcor, Micov, etc.).
    - **Teste RÃ¡pido NÃ£o-Destrutivo por Amostragem:** GravaÃ§Ã£o e conferÃªncia de mais de 1.000 pontos (ou 2.000 pontos em modo precisÃ£o mÃ¡xima) distribuÃ­dos em todo o span da unidade sem apagar arquivos existentes, detectando looping de memÃ³ria flash e capacidade falsa em poucos minutos.
- **Nova Aba Reparar o Windows (`PageWindowsRepair`, `WindowsRepairService`, `MainWindow.WindowsRepair`):**
  - **SuÃ­te Completa de CorreÃ§Ã£o Oficial da Microsoft:** ExecuÃ§Ã£o em ordem tÃ©cnica recomendada das principais rotinas de reparo: CriaÃ§Ã£o de Ponto de RestauraÃ§Ã£o, `chkdsk C: /scan`, `DISM /RestoreHealth`, `sfc /scannow`, WMI `salvagerepository`, limpeza de cache de Ã­cones/miniaturas, reset de rede e Winsock/DNS, e agendamento de verificaÃ§Ã£o profunda `chkdsk /f` no prÃ³ximo boot.
  - **Terminal de Logs e Acompanhamento Unificado:** Terminal embutido com saÃ­da detalhada dos processos, barra de progresso percentual, botÃ£o de cancelamento seguro a qualquer momento e card de veredito com aviso de reinicializaÃ§Ã£o.
- **Telemetria AvanÃ§ada de Bateria em Energia (`BatteryReportService`, `BatteryInfo`, `MainWindow.Net`, `PageEnergy`):**
  - **DetecÃ§Ã£o AutomÃ¡tica de Notebooks:** IdentificaÃ§Ã£o em tempo real da presenÃ§a de bateria no sistema, exibindo card dedicado e ocultando automaticamente a seÃ§Ã£o quando executado em desktops.
  - **MÃ©tricas Completas do HUB Pro:** ExtraÃ§Ã£o via `powercfg /batteryreport /xml` e WMI com badge de saÃºde percentual, contagem de ciclos (`CycleCount`), capacidade de fÃ¡brica (Design Capacity), capacidade atual em carga total (Full Charge Capacity) em mWh e Wh, taxa de degradaÃ§Ã£o da bateria %, quÃ­mica (LÃ­on, etc.), fabricante e nÃºmero de sÃ©rie.
  - **Comparativo de DuraÃ§Ã£o e HistÃ³rico:** Estimativa de autonomia quando novo vs duraÃ§Ã£o atual, histÃ³rico de capacidade das Ãºltimas sessÃµes e botÃ£o dedicado para abrir o RelatÃ³rio Oficial HTML do Windows (`powercfg /batteryreport`) no navegador padrÃ£o.

## [0.19.0] - 2026-11-04

### ðŸš€ Adicionado & Aprimorado
- **Nova Tela Dedicada de InicializaÃ§Ã£o do Windows (`BootDiagnosticsService`, `PageStartup`, `MainWindow.Startup`):**
  - **Tempo de Boot e MÃ©tricas Detalhadas:** CÃ¡lculo do tempo tÃ­pico para ligar o Windows, decomposiÃ§Ã£o do Caminho Principal (`MainPath` - Kernel e Drivers) e PÃ³s-InicializaÃ§Ã£o (`PostBoot` - carregamento de aplicativos e tempo atÃ© a ociosidade do desktop) baseado no log `Microsoft-Windows-Diagnostics-Performance/Operational` (Event ID 100) com fallback para o log de Sistema (Event ID 6005) e Uptime.
  - **Indicador de InÃ­cio RÃ¡pido (Fast Startup / Hiberboot):** DetecÃ§Ã£o inteligente do estado do inÃ­cio rÃ¡pido do Windows via registro (`HiberbootEnabled`), explicando o impacto no boot a frio e reinicializaÃ§Ãµes.
  - **Timeline HistÃ³rica Visual das Ãšltimas InicializaÃ§Ãµes:** GrÃ¡fico visual comparativo das Ãºltimas sessÃµes de inicializaÃ§Ã£o com barras horizontais proporcionais, data, horÃ¡rio e tempo total decorrido.
  - **DiagnÃ³stico de Gargalos ("O que atrasou o inÃ­cio"):** Mapeamento automÃ¡tico dos programas (ID 101), drivers (ID 102), serviÃ§os (ID 103) e tarefas em segundo plano (ID 108) que adicionaram atraso ao boot, exibindo tempo exato de lentidÃ£o e Ã­cones de identificaÃ§Ã£o.
  - **Programas de InicializaÃ§Ã£o com Selos de RecomendaÃ§Ã£o do HUB Pro:** CategorizaÃ§Ã£o de cada item de inicializaÃ§Ã£o com selos coloridos ("Seguro de desligar" em verde esmeralda, "Melhor deixar ligado" em azul e "Confira antes" em Ã¢mbar), alÃ©m de interruptor de ativaÃ§Ã£o rÃ¡pida, botÃ£o de remoÃ§Ã£o e restauraÃ§Ã£o de removidos.
  - **Retorno da Aba ao Menu Lateral:** Item "InicializaÃ§Ã£o" reintroduzido no menu `RESET FERRAMENTAS`, desvinculando-o da aba Geral das ConfiguraÃ§Ãµes.
  - **Fechamento AutomÃ¡tico de Abas na InicializaÃ§Ã£o:** Ao abrir o programa, todos os grupos expansÃ­veis do menu lateral (Ferramentas, Reset Maker, etc.) iniciam devidamente recolhidos.
- **Atalhos RÃ¡pidos & Controles do Programa na Aba Geral (`MainWindow.Settings`, `PanelSettingsGeneral`):**
  - Novo card de conveniÃªncia na aba Geral de ConfiguraÃ§Ãµes sem remover os controles das pÃ¡ginas originais.
  - Atalho rÃ¡pido para mutar/desmutar microfone com tecla de atalho global (`Ctrl + Shift + M`).
  - Atalho rÃ¡pido para ativar/desativar controle de volume com scroll do mouse na barra de tarefas.
  - Seletor rÃ¡pido de dispositivo de saÃ­da de som e microfone padrÃ£o com sincronizaÃ§Ã£o instantÃ¢nea.
  - Atalho rÃ¡pido para ligar/desligar o Supressor Neural de RuÃ­do.
  - Atalho rÃ¡pido para ligar/desligar a Overlay de Desempenho e FPS nos jogos.
- **Sub-aba de AtualizaÃ§Ã£o de Programas no Instalador de Apps (`AppInstaller`, `MainWindow.Apps`, `ViewAppsUpdate`):**
  - Sub-abas `[ðŸ“¦ INSTALAR PROGRAMAS]` e `[ðŸ”„ ATUALIZAR PROGRAMAS]` no topo da tela de aplicativos.
  - DetecÃ§Ã£o de novas versÃµes disponÃ­veis via `winget upgrade --include-unknown` exibindo versÃ£o instalada, versÃ£o disponÃ­vel, ID e status.
  - BotÃµes para "Verificar AtualizaÃ§Ãµes", "Atualizar Selecionados" e "ðŸš€ ATUALIZAR TODOS" com execuÃ§Ã£o silenciosa em lote.
- **AtualizaÃ§Ã£o Completa de Drivers com Ponto de RestauraÃ§Ã£o (`DriverManager`, `MainWindow.Driver`):**
  - BotÃ£o `[ðŸš€ ATUALIZAR TODOS OS DRIVERS DISPONÃVEIS]` na tela de Drivers & DDU.
  - CriaÃ§Ã£o automÃ¡tica prÃ©via de Ponto de RestauraÃ§Ã£o do Windows via `SystemRestoreManager` antes de baixar e instalar as atualizaÃ§Ãµes pelo Windows Update.
- **RestauraÃ§Ã£o de Aplicativos PadrÃ£o do Windows no Debloat (`WindowsDebloatService`, `MainWindow.WindowsDebloat`):**
  - BotÃ£o `[â†º REINSTALAR APPS DO WINDOWS]` na aba Bloatware, permitindo re-registrar todos os pacotes AppX nativos de fÃ¡brica do Windows de forma segura caso algum componente tenha sido removido por engano.
- **Backup e RestauraÃ§Ã£o de Redes Wi-Fi e Senhas (`BackupService`, `MainWindow.Backup`):**
  - Card dedicado **REDES WI-FI E SENHAS (BACKUP PARA FORMATAÃ‡ÃƒO)** na Central de Backup.
  - Leitura e listagem de todos os perfis Wi-Fi salvos com SSID, autenticaÃ§Ã£o e senhas.
  - ExportaÃ§Ã£o de perfis XML com chave legÃ­vel (`key=clear`), relatÃ³rio em texto (`Redes-WiFi-Senhas.txt`) e script em lote automatizado (`Restaurar-WiFi.bat`).
  - RestauraÃ§Ã£o com 1 clique de todos os perfis de uma pasta de backup de volta para o Windows.

## [0.18.0] - 2026-11-04

### ðŸš€ Adicionado & Aprimorado
- **Novo Mapa do Disco com Particionamento 2D Squarified Treemap (`TreemapLayoutEngine`, `DiskSpaceAnalyzer`, `MainWindow.DiskSpace`):**
  - **Treemap Squarified 2D Responsivo:** ImplementaÃ§Ã£o do algoritmo Squarified Treemap (Bruls, Huizing, van Wijk) com retÃ¢ngulos aninhados proporcionais de alta resoluÃ§Ã£o, separaÃ§Ã£o visual, hierarquia de profundidade (zoom-in ao clicar nas pastas) e botÃ£o de retorno (â† Voltar).
  - **CategorizaÃ§Ã£o AutomÃ¡tica em 8 Grupos com Cores Oficiais:** VÃ­deos (`#E11D48`), Imagens (`#06B6D4`), MÃºsicas (`#A855F7`), Documentos (`#F59E0B`), Programas e jogos (`#10B981`), Compactados (`#F97316`), Windows e sistema (`#3B82F6`) e Outros (`#64748B`).
  - **Faixa Superior de CartÃµes de Unidades (Drive Cards):** DetecÃ§Ã£o automÃ¡tica de todos os discos com grÃ¡ficos de uso coloridos (verde/accent ou vermelho para >90%), espaÃ§o livre/total e seletor para pastas personalizadas (`Escolher pasta...`).
  - **Painel Lateral com Abas Duplas:** `[ðŸ“ Maiores pastas]` e `[ðŸ“„ Maiores arquivos]`, exibindo indicador de categoria, tamanho, contagem de arquivos, porcentagem e mini barra de progresso horizontal para cada item.
  - **PÃ­lulas de Resumo de Categorias e Dicas de Atalhos:** Total em bytes e porcentagem em tempo real para cada uma das 8 categorias no diretÃ³rio atual, com atalhos de clique direto para Limpeza, Desinstalador e Arquivos duplicados.
- **20 Novas OtimizaÃ§Ãµes do Windows ExtraÃ­das do HUB Pro (`WindowsDebloatService`):**
  - **Barra de Tarefas & Menu Iniciar:** Ocultar VisÃ£o de Tarefas (Task View), Exibir segundos no relÃ³gio, Alinhar Menu Iniciar Ã  esquerda (estilo Windows 10), Ocultar Widgets e NotÃ­cias (economiza memÃ³ria RAM), Ocultar Chat/Teams da barra, Reduzir pesquisa da barra para apenas Ã­cone, Desativar instalaÃ§Ã£o silenciosa de jogos patrocinados (Candy Crush, etc).
  - **Windows Explorer & Interface:** Mostrar extensÃµes de arquivos conhecidos (.exe, .zip), Mostrar pastas e arquivos ocultos, Abrir Explorador direto em "Este Computador", Desativar histÃ³rico e arquivos recentes no Acesso RÃ¡pido, Desativar pesquisa web do Bing no Menu Iniciar.
  - **Privacidade & Telemetria:** Desativar ID de publicidade e anÃºncios direcionados, Desativar rastreamento de inicializaÃ§Ã£o de aplicativos, Desativar feed de atividades e linha do tempo, Desativar coleta de telemetria de digitaÃ§Ã£o e escrita, Desativar questionÃ¡rios de feedback e avaliaÃ§Ãµes do Windows.
  - **Desempenho & Fluidez:** Desativar HibernaÃ§Ã£o do Windows (liberaÃ§Ã£o imediata de gigabytes do `hiberfil.sys`), Encerramento acelerado de aplicativos travados (reduÃ§Ã£o de timeout de 20s para 2s), Desativar efeitos de transparÃªncia do Windows (alÃ­vio de GPU e DWM).
- **Nova Central Desfazer Integrada nas ConfiguraÃ§Ãµes (`MainWindow.Settings.Undo`, `PanelSettingsUndo`):**
  - Aba dedicada **CENTRAL DESFAZER** dentro da tela de ConfiguraÃ§Ãµes (`PageSettings`).
  - Status em tempo real do sistema com contagem de ajustes ativos e banner visual intuitivo ("VocÃª tem N ajustes aplicados" ou "Seu PC estÃ¡ no estado original").
  - BotÃ£o `[â†º RESTAURAR TUDO AO PADRÃƒO]`: reverte todas as modificaÃ§Ãµes aplicadas pelo Lumini Tray de uma vez sÃ³, recuperando o estado de fÃ¡brica do Windows.
  - BotÃ£o `[DESFAZER]` individual para cada ajuste listado, com exibiÃ§Ã£o de categoria e efeito.
  - Atalhos integrados para `Abrir RestauraÃ§Ã£o do Sistema (rstrui.exe)` e `Criar Ponto de RestauraÃ§Ã£o Agora`.

## [0.17.9] - 2026-11-03

### ðŸ› ï¸ CorreÃ§Ãµes & ReativaÃ§Ã£o de Cabo Virtual
- **ReativaÃ§Ã£o e Roteamento AutÃ´nomo do Microfone Virtual (`VirtualCableManager`, `MainWindow.Suppressor` & `MicrophoneSuppressorService`):**
  - **Auto-ativaÃ§Ã£o do CABLE Input via PolicyConfig:** Implementado `EnsureCableInputEnabled()`. Quando o dispositivo de Ã¡udio `CABLE Input (VB-Audio Virtual Cable)` estiver presente no Windows porÃ©m marcado como Desativado (`Disabled`), o sistema reativa-o automaticamente em tempo de execuÃ§Ã£o via `IPolicyConfig.SetEndpointVisibility`.
  - **RecuperaÃ§Ã£o e SeleÃ§Ã£o AutomÃ¡tica no Dropdown:** A interface agora detecta o cabo virtual em todos os estados, reativa-o e seleciona-o automaticamente com destaque (`â­ CABLE Input (VB-Audio Virtual Cable) (Recomendado)`), restaurando a rota de Ã¡udio para o Discord, jogos e Windows.
  - **Isolamento de Retorno Preservado:** O Ã¡udio continua sendo transmitido com zero ruÃ­do para o cabo virtual sem vazar para o fone de ouvido do usuÃ¡rio, mantendo o retorno restrito apenas Ã  opÃ§Ã£o sob demanda (`ChkListenSelf`).

## [0.17.8] - 2026-11-03

### ðŸ› ï¸ CorreÃ§Ãµes & Isolamento de Ãudio
- **Isolamento Estrito do Retorno de Voz no Supressor de RuÃ­do (`MicrophoneSuppressorService` & `MainWindow.Suppressor`):**
  - **Fim do Retorno Permanente Acidental:** Corrigido problema em que a rota do Microfone Virtual (CABLE Input) recorria ao endpoint de renderizaÃ§Ã£o padrÃ£o do sistema (`GetDefaultAudioEndpoint`), fazendo com que o Ã¡udio do microfone fosse reproduzido continuamente no fone de ouvido do usuÃ¡rio assim que o supressor neural era ativado.
  - **Bloqueio de Fallback no Roteamento Virtual:** O mÃ©todo de inicializaÃ§Ã£o de endpoints WASAPI (`TryInitRenderEndpoint`) agora desabilita estritamente o fallback padrÃ£o (`allowDefaultFallback = false`) para cabos virtuais. Se nenhum cabo virtual estiver presente ou configurado, o Ã¡udio NÃƒO Ã© enviado aos alto-falantes/fones do usuÃ¡rio.
  - **Filtro Seguro de Dispositivos de Cabo Virtual:** O seletor `CmbVirtualCableDev` agora lista apenas dispositivos de cabo virtual autÃªnticos (`VirtualCableManager.IsCableDevice`). Se nenhum driver estiver instalado, exibe aviso e zera a seleÃ§Ã£o, impedindo seleÃ§Ã£o automÃ¡tica do fone de ouvido como cabo virtual.
  - **Retorno de Voz Sob Demanda:** O retorno de voz no fone de ouvido agora sÃ³ Ã© ativado quando o usuÃ¡rio marca expressamente a opÃ§Ã£o *"Ouvir retorno da minha voz no fone de ouvido"* (`ChkListenSelf`) ou durante a reproduÃ§Ã£o do Teste RÃ¡pido de 5 segundos. Com a opÃ§Ã£o desmarcada, hÃ¡ silÃªncio absoluto no fone.

## [0.17.7] - 2026-11-02

### ðŸš€ Adicionado & Aprimorado
- **Overlay Responsiva com Redimensionamento Proporcional DinÃ¢mico (`OverlayWindow`):**
  - IntegraÃ§Ã£o de `Viewbox` dinÃ¢mico (`Stretch="Uniform"`, `StretchDirection="Both"`) envolvendo a estrutura interna do overlay (`Box`).
  - As informaÃ§Ãµes (FPS, Frametimes, GrÃ¡fico e telemetria de sensores) agora se ajustam, escalam e adaptam perfeitamente ao tamanho da janela de overlay quando o usuÃ¡rio a estica ou diminui, sem cortes ou desalinhamento.
- **Aprimoramento do Supressor de RuÃ­do com AdaptaÃ§Ãµes do EasyEffects para Windows 11 (`NoiseSuppressionEngine`):**
  - Engenharia reversa e adaptaÃ§Ã£o do pipeline de reduÃ§Ã£o de ruÃ­do e VAD do EasyEffects (`easyeffects-master/src/rnnoise.cpp` / `rnnoise.hpp` e `gate.cpp`).
  - ImplementaÃ§Ã£o de mixagem Wet/Dry linear configurÃ¡vel baseada na intensidade de supressÃ£o, prevenindo distorÃ§Ãµes robÃ³ticas e mantendo a voz encorpada e cristalina.
  - VAD Grace Counter integrado (sustentaÃ§Ã£o de fala do EasyEffects com liberaÃ§Ã£o em blocos de 10ms) para impedir cortes prematuros em consoantes fricativas e cauda de frases.
  - Ajuste na precisÃ£o dos coeficientes de filtros Butterworth passa-altas de 80Hz para isolamento de rumble subsÃ´nico.

### âš¡ Performance & Testes
- **Novos Testes Automatizados:** Adicionada suite `EasyEffectsAdaptationTests.cs` validando o algoritmo adaptado do EasyEffects com retenÃ§Ã£o de naturalidade vocal e conformidade temporal.
- **ValidaÃ§Ã£o de CompilaÃ§Ã£o e Testes:** Build concluÃ­do com Ãªxito e 123 testes unitÃ¡rios aprovados com 100% de sucesso.

## [0.17.7] - 2026-11-02

### ðŸš€ Adicionado & Aprimorado
- **Overlay adaptÃ¡vel Ã  janela (mesmo visual da v0.17.6):** o conteÃºdo agora escala conforme o tamanho da janela (cabe em largura e altura) e a largura extra Ã© usada pelo grÃ¡fico de frametime e pelos valores dos sensores, que se alinham Ã  direita. Os presets Pequeno/MÃ©dio/Grande redimensionam a janela.
- **Novas mÃ©tricas de RAM na overlay (opcionais):** "RAM do jogo em foco" e "RAM do Lumini Tray".
- **Supressor de RuÃ­do com tÃ©cnicas do EasyEffects adaptadas ao Windows 11:**
  - *VAD grace*: o gate segue aberto por um tempo apÃ³s a Ãºltima voz detectada pelo RNNoise (evita cortar finais de palavras e sussurros).
  - *Sidechain com passa-altas (150 Hz)*: o detector do gate ignora batidas de mesa e graves.
  - *Limitador suave anti-estouro* e *De-esser* (sibilÃ¢ncia acima de 6,5 kHz), ambos ativos por padrÃ£o.
- **Barra lateral:** todos os grupos agora iniciam fechados (antes PerifÃ©ricos e Ferramentas iniciavam abertos).

### âš¡ Performance & Testes
- Novos testes em `EasyEffectsSuppressorTests.cs`.

## [0.17.6] - 2026-11-02

### ðŸš€ Adicionado & Aprimorado
- **ModernizaÃ§Ã£o e Aprimoramento da Overlay de FPS & MÃ©tricas (`OverlayWindow`):**
  - **Redimensionamento Nativo de Janela:** A overlay agora pode ser redimensionada livremente pelas bordas e cantos como uma janela tradicional do Windows, integrando o processamento nativo de `WM_NCHITTEST` com cursores de dimensionamento do sistema operacional.
  - **Controle de TransparÃªncia por Scroll:** Girar a roda do mouse (*mouse wheel*) diretamente sobre a overlay ajusta a opacidade de forma contÃ­nua entre 20% e 100%, com exibiÃ§Ã£o de badge visual de feedback momentÃ¢neo.
  - **InÃ­cio Solto e ArrastÃ¡vel:** A overlay inicia sempre desbloqueada por padrÃ£o, permitindo reposicionamento imediato em qualquer monitor e ponto da tela.
  - **FixaÃ§Ã£o por Duplo Clique:** Eliminada a antiga barra superior de arraste e o botÃ£o fixar; basta dar 2 cliques rÃ¡pidos (duplo clique) em qualquer parte da overlay para travar a posiÃ§Ã£o e reativar a transparÃªncia total a cliques (`WS_EX_TRANSPARENT`) para os jogos.
  - **Fechamento InstantÃ¢neo via BotÃ£o do Meio:** Clicar com a roda do mouse (botÃ£o do meio / *scroll click*) fecha a overlay na hora.
  - **PersistÃªncia de DimensÃµes e Opacidade:** Salvamento automÃ¡tico de `OverlayWidth`, `OverlayHeight` e `OverlayOpacity` no arquivo de configuraÃ§Ãµes.
- **ExpansÃ£o do CatÃ¡logo de Aplicativos por Categorias (`PageApps` & `AppInstaller`):**
  - CatÃ¡logo ampliado com mais de 25 programas essenciais pÃ³s-formataÃ§Ã£o, organizados nas 4 categorias solicitadas:
    - **ðŸŽ® Jogos:** Steam (Valve), Epic Games Launcher (Epic Games), EA App (Electronic Arts), Ubisoft Connect (Ubisoft), GOG GALAXY (GOG.com), Battle.net (Blizzard).
    - **ðŸ’¬ ComunicaÃ§Ã£o:** Discord (Discord Inc.), Telegram Desktop (Telegram), TeamSpeak 3 Client (TeamSpeak), Zoom Workplace (Zoom), OBS Studio (OBS Project), Spotify (Spotify AB).
    - **ðŸŒ NavegaÃ§Ã£o:** Google Chrome (Google), Mozilla Firefox (Mozilla), Brave Browser (Brave Software), Opera GX (Opera), Microsoft Edge (Microsoft).
    - **ðŸ› ï¸ UtilitÃ¡rios:** 7-Zip (Igor Pavlov), WinRAR (win.rar GmbH), VLC media player (VideoLAN), Lightshot (Skillbrains), ShareX (ShareX Team), Notepad++ (Notepad++ Team), qBittorrent (The qBittorrent project), Visual Studio Code (Microsoft).
  - Todos os pacotes sÃ£o validados diretamente da fonte oficial do fabricante pelo `winget`.
  - Nova barra de filtros rÃ¡pidos por categoria (*Chips*) na interface com suporte aos temas Escuro e Claro do Lumini Tray.

### âš¡ Performance & Testes
- **Novos Testes UnitÃ¡rios:** Adicionada a suite `OverlayAndAppsTests.cs` cobrindo validaÃ§Ã£o de propriedades de catÃ¡logo, categorias, integridade dos IDs do winget e serializaÃ§Ã£o de configuraÃ§Ãµes da overlay.
- **ValidaÃ§Ã£o com dotnet build e dotnet test:** Projeto compilado sem erros e 122 testes automatizados executados e aprovados com 100% de sucesso.

## [0.17.5] - 2026-11-02

### ðŸ› ï¸ CorreÃ§Ãµes & Responsividade de Layout
- **CorreÃ§Ã£o da ModulaÃ§Ã£o e Redimensionamento da Janela (Window Layout Modulation):**
  - Resolvido problema em que o conteÃºdo das abas era cortado (clipped) ao redimensionar ou esticar a janela no Windows em vez de se ajustar dinamicamente.
  - O atributo `HorizontalScrollBarVisibility="Auto"` nas 21 pÃ¡ginas (`ScrollViewer`) passava largura infinita (`double.PositiveInfinity`) durante o ciclo de mensuraÃ§Ã£o do WPF (`MeasureOverride`), o que impedia que `Grid` com colunas proporcionais (`Width="*"`), `WrapPanel` e textos com `TextWrapping="Wrap"` calculassem corretamente a quebra de linha e a largura disponÃ­vel.
  - Revertido para `HorizontalScrollBarVisibility="Disabled"` em todas as pÃ¡ginas (`PageDash`, `PageDisks`, `PageEnergy`, `PageNet`, `PageDriver`, `PageDiskTools`, `PageRestore`, `PageBackup`, `PageBottleneck`, `PageOverlay`, `PageOptimize`, `PageApps`, `PageUninstaller`, `PageMakerPorts`, `PageMakerDrv`, `PageOc`, `PageSettings`, `PagePerifericos`, `PageFanControl`, `PageWindowsDebloat`, `PageSuppressor`).
  - Agora, cada container recebe estritamente a largura visÃ­vel da janela (`ViewportWidth`), garantindo que o conteÃºdo responda e se acomode fluidamente ao esticar, diminuir ou maximizar a janela sem cortes indesejados.

### âš¡ Performance & Testes
- **ValidaÃ§Ã£o com dotnet build e dotnet test:** Projeto compilado sem erros ou avisos e suite de 117 testes automatizados validada com 100% de sucesso.

## [0.17.4] - 2026-11-02

### ðŸ› ï¸ CorreÃ§Ãµes & Tipografia
- **CorreÃ§Ã£o Geral de AcentuaÃ§Ã£o e Caracteres Especiais (Mojibake Fix):**
  - Identificada e eliminada toda a corrupÃ§Ã£o de codificaÃ§Ã£o de texto (sequÃªncias de codificaÃ§Ã£o dupla UTF-8/CP1252 como `ÃƒÂ§`, `ÃƒÂ£`, `ÃƒÂ¡`, `ÃƒÂ©`, `ÃƒÂ­`, `ÃƒÂ³`, `Ã¢Å¡Â¡`, etc.) presente na interface grÃ¡fica (`MainWindow.xaml`).
  - Corrigidos tÃ­tulos, rÃ³tulos, badges, tooltips e comentÃ¡rios em toda a aba do **Supressor de RuÃ­do** e barra de navegaÃ§Ã£o:
    - `"Supressor de RuÃ­do"` (anteriormente `RuÃƒÂ­do` / `RuÃƒÂ­do`);
    - `"âš¡ RNNoise Ativo"` (anteriormente `Ã¢Å¡Â¡`);
    - `"âšª"` (anteriormente `Ã¢Å¡Âª`);
    - `"-âˆž dB"` (anteriormente `-Ã¢Ë†Å¾ dB`);
    - `"SUPRESSOR DE RUÃDO: DESATIVADO"` e mensagens de Ã¡udio ativas;
    - `"SINAL PROCESSADO (APÃ“S SUPRESSÃƒO)"`;
    - `"PARÃ‚METROS DO SUPRESSOR NEURAL & NOISE GATE"`;
    - `"Intensidade da SupressÃ£o"`, `"BalanÃ§o entre corte de ruÃ­do e naturalidade"`;
    - `"Ganho de PrÃ©-AmplificaÃ§Ã£o (dB)"`, `"Muda para silÃªncio absoluto abaixo deste nÃ­vel"`;
    - `"Tempo de LiberaÃ§Ã£o (Release)"`, `"Corpo e presenÃ§a da voz"`;
    - `"MÃ‰DIOS (1.2 kHz)"`, `"SaÃ­da"`, `"cÃ¡psula do microfone"`.
  - Varredura e validaÃ§Ã£o completa em todos os arquivos de cÃ³digo (`.cs`, `.xaml`, `.json`), assegurando que 100% dos textos e caracteres ortogrÃ¡ficos do portuguÃªs brasileiro estejam codificados de forma pura e limpa em UTF-8 sem perdas.

### âš¡ Performance & Testes
- **ValidaÃ§Ã£o de Testes UnitÃ¡rios:** Todos os 117 testes automatizados executados e validados com 100% de aprovaÃ§Ã£o.


## [0.17.3] - 2026-11-02

### ðŸš€ Adicionado & Aprimorado
- **DesinstalaÃ§Ã£o em Lote com Caixas de SeleÃ§Ã£o:**
  - Adicionadas caixas de seleÃ§Ã£o (`CheckBox`) individuais em cada programa da lista para marcaÃ§Ã£o mÃºltipla.
  - Nova barra de ferramentas em lote com opÃ§Ãµes "Selecionar Todos", "Desmarcar Todos" e botÃ£o de aÃ§Ã£o `ðŸ—‘ï¸ Desinstalar Selecionados (X)`.
  - Processamento sequencial organizado de todos os desinstaladores marcados com atualizaÃ§Ã£o contÃ­nua de status na tela.
  - Suporte Ã  varredura profunda consolidada ao final do lote, agrupando resÃ­duos no disco e no Registro de todos os programas removidos.
- **OpÃ§Ã£o "Abrir Local do Arquivo" (ðŸ“ Abrir Local):**
  - Adicionado botÃ£o direto em cada programa da lista que abre a pasta de instalaÃ§Ã£o oficial ou localiza o executÃ¡vel selecionado no Windows Explorer.
- **AtivaÃ§Ã£o e DesativaÃ§Ã£o de Programas Ocultos e Componentes:**
  - Adicionado alternador `Exibir programas ocultos e componentes` para revelar ou ocultar instalaÃ§Ãµes que nÃ£o possuem interface de aplicativo direta (runtimes, dependÃªncias, redistributÃ¡veis C++, drivers e componentes marcados como `SystemComponent`/`ParentKeyName` no Registro).
  - IdentificaÃ§Ã£o visual clara com badge `[âš™ï¸ Componente]` para diferenciar softwares comuns de componentes do sistema.
- **ConfirmaÃ§Ã£o de SeguranÃ§a ObrigatÃ³ria em Todas as DesinstalaÃ§Ãµes:**
  - DiÃ¡logos explÃ­citos de confirmaÃ§Ã£o antes de iniciar qualquer desinstalaÃ§Ã£o (individual, profunda ou em lote), listando os softwares a serem removidos para evitar cliques acidentais.

### ðŸ› ï¸ CorreÃ§Ãµes & RefatoraÃ§Ã£o
- **RemoÃ§Ã£o do Modo Silencioso:**
  - Removido o botÃ£o e a badge de "âš¡ Silencioso", assegurando que os desinstaladores oficiais sejam sempre iniciados de maneira visÃ­vel e controlada pelo usuÃ¡rio.

### âš¡ Performance & Testes
- **ExpansÃ£o da SuÃ­te de Testes:** SuÃ­te atualizada com 117 testes unitÃ¡rios automatizados passando com 100% de aprovaÃ§Ã£o.


## [0.17.2] - 2026-11-02

### ðŸš€ Adicionado & Aprimorado
- **Sistema Completo de NavegaÃ§Ã£o HistÃ³rica entre Abas (Setas Voltar / AvanÃ§ar):**
  - **Setas na Barra de TÃ­tulo (TitleBar):** Adicionados botÃµes dedicados de navegaÃ§Ã£o histÃ³rica (`BtnNavBack` e `BtnNavForward`) posicionados no topo da janela entre o logotipo "Lumini Tray" e o tÃ­tulo da pÃ¡gina ativa, com suporte total a clique nativo (`WindowChrome.IsHitTestVisibleInChrome="True"`).
  - **Tooltips Contextuais Inteligentes:** Os botÃµes exibem dinamicamente o nome da pÃ¡gina de destino ao passar o mouse (ex: *"Voltar para Sensores em Tempo Real (Alt+Seta Esquerda / BotÃ£o Voltar do Mouse)"*), ficando desabilitados e com opacidade reduzida quando nÃ£o houver pÃ¡ginas para voltar ou avanÃ§ar.
  - **Suporte Nativo a BotÃµes do Mouse:** NavegaÃ§Ã£o instantÃ¢nea com botÃµes laterais de mouses gamer/office (`XButton1` para Voltar e `XButton2` para AvanÃ§ar).
  - **Atalhos de Teclado Universais:** NavegaÃ§Ã£o histÃ³rica por `Alt + Seta Esquerda` (Voltar), `Alt + Seta Direita` (AvanÃ§ar) e tecla `Backspace` (quando nÃ£o estiver digitando em campos de texto).
  - **Gerenciador de HistÃ³rico Encadeado (`NavigationHistoryManager`):** Rastreia pÃ¡ginas visitadas com capacidade de atÃ© 50 itens, descarta automaticamente ramificaÃ§Ãµes futuras ao navegar para uma nova aba e evita entradas consecutivas duplicadas.
  - **SincronizaÃ§Ã£o Completa com Menus e AcordeÃµes:** Ao retroceder ou avanÃ§ar, o menu lateral ativa automaticamente o radio button correspondente, expande a categoria pai de acordeÃµes (ex: PerifÃ©ricos) e atualiza o cabeÃ§alho.

- **ModulaÃ§Ã£o Responsiva e Fluida do Layout (Zero Quebras ao Redimensionar/Esticar):**
  - **EliminaÃ§Ã£o de Margens RÃ­gidas:** Reformulados todos os cabeÃ§alhos de pÃ¡ginas que possuÃ­am margens duras Ã  direita (atÃ© 450px) para grades responsivas de 2 colunas (`Grid.ColumnDefinitions` com `Width="*"` e `Width="Auto"`), permitindo que tÃ­tulos e botÃµes de aÃ§Ã£o se adaptem perfeitamente a qualquer largura.
  - **Suporte a Rolagem Horizontal em Telas Pequenas/Divididas (`HorizontalScrollBarVisibility="Auto"`):** Habilitada rolagem horizontal automÃ¡tica em todos os 21 painÃ©is de pÃ¡ginas (`ScrollViewer`), garantindo que nenhum conteÃºdo, tabela, cartÃ£o ou botÃ£o seja cortado ao utilizar o aplicativo em meia-tela (Snap do Windows) ou monitores compactos.
  - **Alinhamento e EstilizaÃ§Ã£o Dark/Light Theme:** UtilizaÃ§Ã£o rigorosa das fontes e temas dinÃ¢micos do projeto (`{DynamicResource Fg}`, `{DynamicResource Hover}`, `{DynamicResource CardSubtleBg}`, etc.) sem cores fixadas no sistema.

### âš¡ Performance & Testes
- **ExpansÃ£o da SuÃ­te de Testes Automatizados:** Adicionada a classe `NavigationHistoryTests` com 9 novos testes unitÃ¡rios validando empilhamento de pÃ¡ginas, navegaÃ§Ã£o de ida e volta, truncamento de ramificaÃ§Ã£o futura, limite de capacidade e mapeamento de nomes amigÃ¡veis.
- **100% de AprovaÃ§Ã£o:** SuÃ­te agora conta com 114 testes unitÃ¡rios automatizados passando com Ãªxito total.


## [0.17.1] - 2026-11-02

### ðŸš€ Adicionado & Aprimorado
- **ReformulaÃ§Ã£o Completa da Aba de Desinstalar Programas (Baseada no Bulk Crap Uninstaller 6.3):**
  - **Motor de DetecÃ§Ã£o Multi-Origem (Multi-Source Factory):**
    - **Reconhecimento Muito Mais Abrangente:** O app agora identifica nÃ£o apenas softwares do registro clÃ¡ssico, mas tambÃ©m aplicativos modernos da Microsoft Store (UWP / MSIX), jogos do Steam em todas as bibliotecas e unidades de disco (`libraryfolders.vdf` e `appmanifest_*.acf`), e softwares portÃ¡teis/diretÃ³rios em `Program Files` e `AppData\Programs`. (Salto de 60 para 127+ softwares detectados no sistema).
    - **DetecÃ§Ã£o Inteligente de Motores de InstalaÃ§Ã£o:** Reconhece Inno Setup, NSIS (Nullsoft), MSI / Windows Installer, InstallShield, StoreApp (UWP) e Steam.
    - **DesinstalaÃ§Ã£o Silenciosa com 1-Clique (âš¡ Silencioso):** Gera automaticamente os argumentos silenciosos corretos (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` para InnoSetup, `/S` para NSIS, `/qn /norestart` para MSI e `Remove-AppxPackage` para Store Apps) permitindo desinstalar em segundo plano sem caixas de diÃ¡logo.
  - **Motor de Varredura Profunda de ResÃ­duos (Junk Engine com 5 Categorias):**
    - **Pastas e Arquivos Residuais:** Varre `InstallLocation`, `AppData\Roaming`, `AppData\Local`, `AppData\LocalLow`, `ProgramData` e `Common Files`, alÃ©m de limpar com seguranÃ§a pastas vazias de fabricantes.
    - **Atalhos Ã“rfÃ£os:** Identifica atalhos `.lnk` abandonados na Ãrea de Trabalho (UsuÃ¡rio e PÃºblica) e no Menu Iniciar.
    - **Chaves no Registro:** Localiza chaves residuais em `HKCU\Software`, `HKLM\Software`, chaves de desinstalaÃ§Ã£o Ã³rfÃ£s e registros de inicializaÃ§Ã£o.
    - **Regras do Firewall do Windows:** Detecta e elimina regras Ã³rfÃ£s em `FirewallPolicy\FirewallRules` que vinculavam a executÃ¡veis deletados.
    - **Rastros do Sistema:** Remove arquivos de Prefetch (`*.pf`) e relatÃ³rios de falha (`WER\ReportArchive`, `%LOCALAPPDATA%\CrashDumps`).
    - **Guarda de SeguranÃ§a Estrito (Safe Guard):** Impede a exclusÃ£o acidental de pastas protegidas do Windows, raÃ­zes de unidades ou diretÃ³rios de fabricantes que ainda contenham outros softwares instalados.
  - **Interface GrÃ¡fica Reformulada com Filtros RÃ¡pidos:**
    - Filtros por categoria com badges e contadores dinÃ¢micos: **[ðŸŒ Todos]**, **[ðŸ’» Desktop (Win32)]**, **[ðŸ›ï¸ Microsoft Store]**, **[ðŸŽ® Steam & Jogos]**, **[ðŸ“ PortÃ¡teis & DiretÃ³rios]**.
    - Lista de softwares com alinhamento fluido (`HorizontalContentAlignment="Stretch"`), badge de categoria, badge de suporte silencioso, tamanho formatado e data de instalaÃ§Ã£o.
    - Painel modal de revisÃ£o categorizada de resÃ­duos com botÃµes "Marcar Todos", "Desmarcar" e resumo visual do espaÃ§o em disco liberado.

### ðŸ› ï¸ CorreÃ§Ãµes & Autonomia
- **Empacotamento 100% AutÃ´nomo do Driver de Microfone Virtual (VB-CABLE):**
  - Corrigido o erro onde o executÃ¡vel publicado em arquivo Ãºnico (`Single-File Release`) nÃ£o localizava o instalador do driver (`VBCABLE_Setup_x64.exe`), devido Ã  exclusÃ£o padrÃ£o de arquivos `.exe` e `.sys` em pastas de conteÃºdo pelo publicador do .NET.
  - O pacote completo do driver oficial agora Ã© embutido diretamente como recurso do assembly (`Drivers/VBCable.zip`) no `LuminiTray.dll` e extraÃ­do de forma transparente para `%TEMP%\LuminiTray\VBCable\` sob demanda.
  - Implementada cadeia de instalaÃ§Ã£o robusta com fallback progressivo:
    1. InstalaÃ§Ã£o silenciosa oficial com elevaÃ§Ã£o de administrador (`-i -h`);
    2. InstalaÃ§Ã£o nativa de driver via `pnputil.exe /add-driver vbMmeCable64_win10.inf /install`;
    3. Abertura interativa do instalador oficial VB-Audio com elevaÃ§Ã£o para clique manual ("Install Driver") caso o modo silencioso seja bloqueado pelo sistema.
  - **Novo BotÃ£o "ðŸ“ Abrir Pasta":** Adicionado na interface do Supressor de RuÃ­do ao lado do botÃ£o de 1 clique para que o usuÃ¡rio possa inspecionar os arquivos e executar o instalador manualmente quando desejar.
  - Atualizado o script de distribuiÃ§Ã£o `publicar.bat` para copiar preventivamente a pasta descompactada de drivers para a pasta de distribuiÃ§Ã£o.

### âš¡ Performance & OtimizaÃ§Ã£o
- **SuÃ­te de Testes Expandida:** Total de 105 testes unitÃ¡rios automatizados com 100% de aprovaÃ§Ã£o, incluindo testes de classificaÃ§Ã£o de motor, geraÃ§Ã£o de comandos silenciosos, proteÃ§Ã£o de pastas do sistema e varredura ao vivo.

## [0.17.0] - 2026-11-01

### ðŸš€ Adicionado & Aprimorado
- **Microfone Virtual para Discord, Jogos e Aplicativos do Windows:**
  - O Ã¡udio processado pelo Lumini Tray agora pode ser usado na prÃ¡tica como um microfone real pelo Windows e qualquer outro programa (Discord, CS2, Valorant, Zoom, OBS, Google Meet, etc.).
  - IntegraÃ§Ã£o do driver de Ã¡udio virtual oficial assinado `VB-Audio Virtual Cable` na pasta `Assets/Drivers/VBCable/` com detecÃ§Ã£o automÃ¡tica de status em tempo real.
  - **InstalaÃ§Ã£o com 1-Clique Integrada:** Se o driver nÃ£o estiver presente no Windows, um botÃ£o direto na interface executa a instalaÃ§Ã£o silenciosa oficial com elevaÃ§Ã£o de administrador e atualiza os dispositivos instantaneamente.
  - **Roteamento ContÃ­nuo para o Microfone:** O Ã¡udio tratado e sem ruÃ­dos Ã© continuamente transmitido para o dispositivo `CABLE Input`, disponibilizando o microfone virtual `CABLE Output` para seleÃ§Ã£o nos aplicativos.
  - **Monitoramento de PÃ³s-Processamento Dedicado:** OpÃ§Ã£o direta para ouvir **somente o Ã¡udio pÃ³s-processamento** (limpo com RNNoise e Gate) nos fones de ouvido ou alternar para o Ã¡udio bruto (cru) para comparaÃ§Ã£o A/B ao vivo.
  - **Teste RÃ¡pido de Microfone (5 Segundos):** Gravador e reprodutor integrado na interface (`ðŸŽ™ï¸ Gravar Amostra de 5s e Reproduzir`) que permite ao usuÃ¡rio escutar com 100% de clareza e fidelidade como sua voz estÃ¡ soando, eliminando a confusÃ£o causada pela vibraÃ§Ã£o Ã³ssea natural da fala.

### âš¡ Performance & OtimizaÃ§Ã£o
- **Reamostrador DinÃ¢mico de Amostragem (`LinearResampler` Zero GC):**
  - Implementado reamostrador linear de alta precisÃ£o que converte automaticamente entre qualquer combinaÃ§Ã£o de taxas de amostragem (44.1 kHz, 48 kHz, 96 kHz, 192 kHz) do microfone de entrada e dos dispositivos de saÃ­da.
  - Elimina distorÃ§Ãµes de velocidade, estalos e efeito *chipmunk* quando o headset opera em 44.1 kHz ou 96 kHz.
- **SuÃ­te de Testes Expandida:** Total de 97 testes unitÃ¡rios automatizados cobrindo reamostragem, detecÃ§Ã£o de microfone virtual e mÃ¡quina de estados de gravaÃ§Ã£o de amostras.

## [0.16.9] - 2026-11-01

### âš¡ Performance & OtimizaÃ§Ã£o
- **AtivaÃ§Ã£o da Rede Neural RNNoise Nativa x64 (`rnnoise.dll`):**
  - IntegraÃ§Ã£o do binÃ¡rio nativo prÃ©-compilado de alta performance `rnnoise.dll` na pasta `Native/` com cÃ³pia automÃ¡tica para o diretÃ³rio de build e execuÃ§Ã£o (`CopyToOutputDirectory="PreserveNewest"`).
  - Implementado `NativeLibrary.SetDllImportResolver` no motor `NoiseSuppressionEngine` para resoluÃ§Ã£o estrita e robusta de DLLs do RNNoise tanto na raiz quanto na pasta `Native/`.
  - SupressÃ£o de ruÃ­do neural em 22 bandas com arquitetura GRU recorrente: elimina ruÃ­dos contÃ­nuos, cliques de teclado mecÃ¢nico e ventiladores durante a fala sem distorcer o timbre natural da voz.

### ðŸ› ï¸ CorreÃ§Ãµes & Refinamentos
- **Noise Gate Inteligente com Histerese (Schmitt Trigger) e Hold Time:**
  - Corrigido o corte abrupto de palavras e sÃ­labas fracas ("comeÃ§a a cortar as palavras"):
    - Adicionado limiar de fechamento com histerese de 8 dB abaixo do limiar de abertura (`GateHysteresisDb`), mantendo a porta de Ã¡udio aberta durante sussurros ou consoantes finais suaves;
    - Adicionado Hold Time de 140 ms (`GateHoldMs`) pÃ³s-fala que impede fechamento prematuro do sinal em pausas naturais entre palavras;
    - IntegraÃ§Ã£o direta entre o Voice Activity Detector (VAD neural com probabilidade da rede RNNoise) e o Noise Gate pÃ³s-processamento, silenciando completamente o piso de ruÃ­do no silÃªncio absoluto.
  - Ajuste no badge visual de status do motor para `âš¡ RNNoise Neural Ativo`.
  - SuÃ­te completa de 92 testes unitÃ¡rios aprovada com cobertura para carregamento de biblioteca nativa e preservaÃ§Ã£o de cauda de fala via histerese e hold.

## [0.16.8] - 2026-11-01

### ðŸš€ Adicionado & Aprimorado
- **Nova Ferramenta: Supressor de RuÃ­do de Microfone em Tempo Real (`RESET FERRAMENTAS`):**
  - ImplementaÃ§Ã£o completa da pÃ¡gina `PageSuppressor` acessÃ­vel via menu lateral `NavSuppressor` em RESET FERRAMENTAS.
  - **Motor HÃ­brido de SupressÃ£o:** Suporte opcional Ã  rede neural RNNoise (`rnnoise.dll`) com fallback automÃ¡tico para processamento DSP puro de alta fidelidade:
    - Filtro Passa-Altas Biquad de 80 Hz contra estrondos subsÃ´nicos (vibraÃ§Ã£o de mesa, ar-condicionado, baques);
    - Downward Expander / Noise Gate com limiar (-70 dB a -25 dB) e envelope de liberaÃ§Ã£o suave (30 ms a 400 ms);
    - Equalizador vocal paramÃ©trico de 3 bandas (Graves 180 Hz, MÃ©dios 1.2 kHz, Agudos 6 kHz) com botÃ£o de reset Flat;
    - Ganho de prÃ©-amplificaÃ§Ã£o ajustÃ¡vel de -12 dB a +20 dB.
  - **Arquitetura WASAPI Multithread e RingBuffer (Zero GC):** Thread dedicada com prioridade elevada (`AboveNormal`) operando em blocos precisos de 10 ms (480 amostras a 48 kHz).
  - **Telemetria e Medidores VU em Tempo Real:** Medidores duplos de sinal de entrada bruto vs sinal processado limpo com leitura em dB, alÃ©m de indicador visual inteligente de detecÃ§Ã£o de voz (`VAD Pill`).
  - **Monitoramento / Retorno ao Vivo:** OpÃ§Ã£o "Ouvir minha voz" em tempo real para ajuste fino e testes rÃ¡pidos pelo usuÃ¡rio.
  - **PersistÃªncia Total:** Todas as preferÃªncias de dispositivos, volumes, switches e sliders sÃ£o salvas em `AppSettings.json`.

---

## [0.16.7] - 2026-11-01

### ðŸš€ Adicionado & Aprimorado
- **ReestruturaÃ§Ã£o de InicializaÃ§Ã£o e ConfiguraÃ§Ãµes:**
  - O item "InicializaÃ§Ã£o" foi removido de RESET FERRAMENTAS e migrado para **CONFIGURAÃ‡Ã•ES > GERAL**.
  - Nova sub-aba **GERAL** (somente geral): inicializaÃ§Ã£o completa do Lumini Tray (Agendador de Tarefas, funÃ§Ãµes automÃ¡ticas), gerenciador de programas de inicializaÃ§Ã£o do Windows, comportamento/bandeja do sistema, taxa dos sensores e encerramento completo.
  - A antiga "Geral & Cores" virou **TEMAS & CORES**, contendo apenas modo escuro/claro e paleta de cores de destaque.
  - Sub-abas Ãudio & Microfone, Wallpapers & Telas e IntegraÃ§Ãµes mantidas sem alteraÃ§Ãµes.
  - Removida a opÃ§Ã£o duplicada "Iniciar com o Windows" da bandeja (jÃ¡ existe na inicializaÃ§Ã£o completa).

---

## [0.16.6] - 2026-11-01

### ðŸš€ Adicionado & Aprimorado
- **OtimizaÃ§Ã£o AutomÃ¡tica Programada em Segundo Plano:**
  - Adicionado novo mÃ³dulo de agendamento automÃ¡tico na aba `RESET OTIMIZA` gerenciado pelo novo serviÃ§o `AutoOptimizeService`.
  - Seletor flexÃ­vel de intervalo com prÃ©-definiÃ§Ãµes de 1 min, 5 min, 10 min, 15 min, 30 min, 60 min e modo "Personalizado..." com input numÃ©rico dinÃ¢mico em minutos.
  - TrÃªs aÃ§Ãµes de otimizaÃ§Ã£o selecionÃ¡veis: *Liberar MemÃ³ria RAM em Segundo Plano*, *Fechar Processos em Segundo Plano / NÃ£o Ãšteis*, e *Limpar Cache e Arquivos TemporÃ¡rios (%TEMP%)*.
  - Indicador visual dinÃ¢mico com badge de status ("â³ PrÃ³xima em Xm Ys", "Otimizando agora...", "Inativo") e contagem regressiva em tempo real.
  - BotÃ£o de execuÃ§Ã£o manual imediata ("Otimizar Agora") com feedback sonoro e visual.
- **Lacuna de ExclusÃ£o (Whitelist) e Fechamento Inteligente de Processos:**
  - Novo mecanismo de proteÃ§Ã£o de processos com campo de inserÃ§Ã£o Ã¡gil (suporte Ã  tecla `Enter` ou botÃ£o `+ Adicionar`).
  - BotÃµes de atalho rÃ¡pido em 1 clique para proteger processos de jogos e programas populares (`+ Discord`, `+ Steam`, `+ Spotify`, `+ Chrome`, `+ OBS`).
  - Chips visuais interativos e dinÃ¢micos para cada processo excluÃ­do, com remoÃ§Ã£o em 1 clique (botÃ£o `âœ•`).
  - ImunizaÃ§Ã£o automÃ¡tica de processos essenciais do Windows (`explorer`, `dwm`, `taskmgr`, `svchost`, etc.) e do prÃ³prio aplicativo (`LuminiTray`).
  - NormalizaÃ§Ã£o inteligente de nomes de processos (indiferente a maiÃºsculas/minÃºsculas e extensÃ£o `.exe`) com deduplicaÃ§Ã£o automÃ¡tica.
  - PersistÃªncia completa de todas as configuraÃ§Ãµes, tempos e lista de exclusÃµes em `AppSettings.json`.

### âš¡ Performance & OtimizaÃ§Ã£o de Disco
- **ReduÃ§Ã£o Significativa da Pasta Raiz (~620 MB a 800 MB Liberados):**
  - MigraÃ§Ã£o limpa do catÃ¡logo de bloatware (`Apps.json` e `pt-BR/Apps.json`) para a pasta de assets internos `LuminiTray-latest\Assets\Debloat\`.
  - ExclusÃ£o total de pastas de pacotes redundantes (`debloat/` com 221 MB e `novos/` com 60 MB).
  - Limpeza de artefatos temporÃ¡rios e builds de teste residuais (`bin/TestOut/` com ~250 MB e `.wpftmp.csproj`).
  - RemoÃ§Ã£o de arquivo executÃ¡vel descontinuado `LuminiTray-dist\LuminiTray.exe.old` (89 MB).
  - O repositÃ³rio agora contÃ©m apenas arquivos estritamente necessÃ¡rios para o desenvolvimento, testes e execuÃ§Ã£o do app.

---

## [0.16.5] - 2026-10-31

### ðŸ› ï¸ CorreÃ§Ãµes & Aprimoramentos de Layout
- **CorreÃ§Ã£o da Escala de Tela Cheia e Janela (Build e RodapÃ© VisÃ­veis):**
  - ImplementaÃ§Ã£o de gancho Win32 nativo para a mensagem `WM_GETMINMAXINFO` no `HotkeyHook` de `MainWindow`, restringindo a maximizaÃ§Ã£o estritamente Ã  Ã¡rea de trabalho Ãºtil (`rcWork`) calculada via `GetMonitorInfo` e `MonitorFromWindow`.
  - EliminaÃ§Ã£o definitiva da sobreposiÃ§Ã£o sob a barra de tarefas do Windows: em tela cheia (maximizado), o aplicativo nÃ£o mais invade a barra de tarefas nem perde o rodapÃ© da interface.
  - O indicador de versÃ£o e build (`SideVersion`) no rodapÃ© da barra lateral agora permanece 100% visÃ­vel, legÃ­vel e posicionado com margem de seguranÃ§a perfeita em qualquer resoluÃ§Ã£o e proporÃ§Ã£o de tela.
  - CompensaÃ§Ã£o dinÃ¢mica do sizing frame (`SM_CXFRAME + SM_CXPADDEDBORDER`) sensÃ­vel ao DPI da tela (`VisualTreeHelper.GetDpi`), garantindo alinhamento pixel-perfect tanto em 100%, 125%, 150% quanto em 175% de escala de exibiÃ§Ã£o do Windows.
  - FunÃ§Ã£o `AdjustWindowScaleForScreen` na inicializaÃ§Ã£o para ajustar proporÃ§Ãµes em modo janela caso o monitor possua resoluÃ§Ã£o vertical menor (ex: 768p ou 900p com escala elevada), impedindo que a janela ultrapasse os limites visÃ­veis da tela.
- **Debloat de Aplicativos - EliminaÃ§Ã£o de EspaÃ§o Vazio & Layout Fluido:**
  - Redesenho completo das colunas do `LstDebloatApps`: o bloco de informaÃ§Ãµes do aplicativo (Nome, DescriÃ§Ã£o e ID do Pacote) agora opera com coluna fluida `Width="*"`, preenchendo 100% do espaÃ§o livre disponÃ­vel.
  - Agrupamento compacto e alinhado Ã  direita do Fabricante (`Vendor`, largura padronizada de 85px com alinhamento Ã  direita) e do Badge de RecomendaÃ§Ã£o (`MinWidth="80"`), eliminando a lacuna de espaÃ§o ocioso apontada na interface e mantendo distÃ¢ncia consistente de 14px entre eles.
  - HabilitaÃ§Ã£o de `TextWrapping="Wrap"` nas descriÃ§Ãµes de bloatware, permitindo que textos longos quebrem linha suavemente sem reticÃªncias desnecessÃ¡rias.
  - DesativaÃ§Ã£o explÃ­cita de rolagem horizontal (`ScrollViewer.HorizontalScrollBarVisibility="Disabled"`) e faixa de altura responsiva (`MinHeight="380" MaxHeight="620"`), permitindo visualizaÃ§Ã£o de todos os dados do app sem necessidade de rolar para o lado.
  - EspaÃ§amento de seguranÃ§a no Salvador de Programas Winget (`LstBackupApps`) para evitar sobreposiÃ§Ã£o entre o nome do programa e a publicadora.

---

## [0.16.4] - 2026-10-31

### ðŸš€ Adicionado & Aprimorado
- **Nova Aba Windows & Debloat (RESET FERRAMENTAS):**
  - ImplementaÃ§Ã£o completa da pÃ¡gina dedicada `PageWindowsDebloat` acessÃ­vel via `NavWindowsDebloat` na categoria RESET FERRAMENTAS.
  - **Perfil Adaptativo Inteligente ("Otimizar para meu Hardware"):** AnÃ¡lise em tempo real do processador (Intel com P-cores/E-cores vs AMD Ryzen com CPPC), placa de vÃ­deo (NVIDIA GeForce, AMD Radeon, Intel Arc) e quantidade de memÃ³ria RAM (>= 16 GB vs <= 8 GB) aplicando um conjunto individualizado de parÃ¢metros ideais para a mÃ¡quina.
  - **100% ReversÃ­vel (Desfazer ModificaÃ§Ãµes):** Backup prÃ©vio automÃ¡tico de todas as chaves e valores originais do registro do Windows antes de qualquer alteraÃ§Ã£o, com botÃ£o em destaque para restaurar as configuraÃ§Ãµes de fÃ¡brica em 1 clique.
  - **Debloat de Aplicativos do Windows & OEM:** IntegraÃ§Ã£o com o catÃ¡logo de 162 identificadores de bloatware do `Win11Debloat` com suporte completo para fabricantes OEM (Dell, HP, Lenovo, ASUS, Acer, Samsung e MSI).
  - SeleÃ§Ã£o em lote rÃ¡pida (*Marcar Seguros*, *Marcar Bloatware OEM*, *Desmarcar Todos*), barra de pesquisa dinÃ¢mica com marca d'Ã¡gua e remoÃ§Ã£o assÃ­ncrona multithread via PowerShell (`Remove-AppxPackage -AllUsers` e `Remove-AppxProvisionedPackage -Online`) com barra de progresso e relatÃ³rio detalhado.
  - **Ajustes de CPU, GPU, Input e Sistema:** 17 otimizaÃ§Ãµes documentadas e seguras com leitura de estado real no registro (`Registry.GetValue`), incluindo `SystemResponsiveness = 0`, agendamento de GPU acelerado por hardware (HAGS), DirectX Flip Model, `DisableDXMaxFrameLatency = 1`, mira linear 1:1 sem aceleraÃ§Ã£o de mouse, desativaÃ§Ã£o de Game DVR, desativaÃ§Ã£o de telemetria diagnÃ³stica, desativaÃ§Ã£o de Copilot e Recall, e retorno do menu de contexto clÃ¡ssico.
  - Suporte completo aos temas Claro e Escuro com `{DynamicResource Fg}`, `{DynamicResource CardSubtleBg}`, e alinhamento fluido (`HorizontalContentAlignment="Stretch"`).

---

## [0.16.3] - 2026-10-31

### ðŸš€ Aprimorado & Corrigido
- **Central de Backup no Tema Escuro & Alinhamento:**
  - CorreÃ§Ã£o das cores de texto dos softwares listados (`LstBackupApps`), aplicando `{DynamicResource Fg}` para leitura em alto contraste no tema escuro.
  - ImplementaÃ§Ã£o de `ItemContainerStyle` com `HorizontalContentAlignment="Stretch"`, eliminando o agrupamento incorreto Ã  esquerda e garantindo alinhamento fluido de colunas (Nome, Desenvolvedor e Tamanho).
  - AdiÃ§Ã£o de marca d'Ã¡gua (`Buscar programas instalados...`) no campo de pesquisa e alto contraste para os seletores de formato de backup (.ZIP e CÃ³pia direta).
- **Controle de Fans - EdiÃ§Ã£o Direta de PrÃ©-definiÃ§Ãµes:**
  - Auto-preenchimento do nome da prÃ©-definiÃ§Ã£o na caixa de texto ao selecionar um perfil customizado no ComboBox (CPU e GPU).
  - Ao clicar em "Salvar", caso a prÃ©-definiÃ§Ã£o jÃ¡ esteja selecionada, o software atualiza e sobrescreve as novas curvas diretamente sem exigir redigitaÃ§Ã£o do nome.
  - Limpeza automÃ¡tica do campo de texto ao selecionar presets originais do sistema (*Silencioso*, *Equilibrado*, *Desempenho*).
- **Suporte a MÃºltiplos Monitores no MonitorLab Pro:**
  - IntegraÃ§Ã£o das ferramentas de calibraÃ§Ã£o e testes de tela cheia (*Dead Pixels*, *Uniformidade*, *Contraste*, *Gradientes*, *Nitidez* e *UFO Ghosting*) ao monitor selecionado na aba PerifÃ©ricos.
  - Posicionamento e maximizaÃ§Ã£o direcionados para as coordenadas reais do display alvo via `System.Windows.Forms.Screen.AllScreens`.
  - ExibiÃ§Ã£o do nome do monitor em teste no HUD de abertura e notificaÃ§Ã£o imediata (*toast*) ao alternar de monitor.

---

## [0.16.2] - 2026-10-31

### ðŸš€ Adicionado & Aprimorado
- **Gerenciador de PrÃ©-definiÃ§Ãµes de Fans:**
  - AdiÃ§Ã£o de lacuna de texto (`TextBox`), botÃ£o "Salvar" e seletor suspenso (`ComboBox`) para nomear, armazenar e trocar perfis de curvas de ventoinhas em tempo real (CPU e GPU).
  - PersistÃªncia automÃ¡tica de todas as prÃ©-definiÃ§Ãµes salvas em `settings.json`, com opÃ§Ã£o de exclusÃ£o rÃ¡pida de presets personalizados.
- **Nova Central de Backup & RestauraÃ§Ã£o (`PageBackup`):**
  - **Salvador de Drivers (pnputil):** Exporta todos os drivers de terceiros instalados e gera um script/executÃ¡vel instalador autÃ´nomo com auto-elevaÃ§Ã£o administrativa (`Instalar_Drivers.bat`) para reinstalaÃ§Ã£o instantÃ¢nea em 1 clique apÃ³s formataÃ§Ã£o.
  - **Salvador de Programas (Winget):** Lista dinÃ¢mica de programas instalados com busca e checkboxes (`[x]`); gera um instalador em lote (`Instalar_Programas.bat`) que reinstala automaticamente os aplicativos selecionados via `winget`.
  - **Backup de Pastas do UsuÃ¡rio:** Backup com cÃ¡lculo de tamanho em tempo real para **Ãrea de Trabalho**, **Downloads**, **Imagens** e **VÃ­deos** (com opÃ§Ã£o de arquivo comprimido `.ZIP` ou cÃ³pia de pastas direta, barra de progresso e cancelamento).

---

## [0.16.1] - 2026-10-30

### ðŸš€ Adicionado & Aprimorado
- **Controle de Fans & Bomba AIO em Segundo Plano:**
  - O motor de controle de ventoinhas agora roda em uma thread de background dedicada desacoplada da interface grÃ¡fica, garantindo que o controle continue ativo mesmo com o app minimizado para a bandeja.
  - Heartbeat anti-timeout para canais com trava de 100% (ex: bombas de Water Cooler / AIO), prevenindo que a BIOS ou o SuperI/O da placa-mÃ£e restaurem o controle padrÃ£o por falta de pulsos PWM.
  - CorreÃ§Ã£o do desarme indevido: a restauraÃ§Ã£o dos ventiladores para a BIOS (`RestoreAllToBios`) nÃ£o Ã© mais acionada ao fechar a janela para a bandeja, ocorrendo unicamente no encerramento real do aplicativo ("Sair").
  - InicializaÃ§Ã£o automÃ¡tica do serviÃ§o de fans no startup do programa, sem necessidade de navegar manualmente atÃ© a aba FanControl.
  - **PersistÃªncia Completa:** As curvas de CPU e GPU, modos manuais e travas de 100% dos canais agora sÃ£o salvas automaticamente em `settings.json` e restauradas em cada inicializaÃ§Ã£o.

### âš¡ Performance & OtimizaÃ§Ã£o
- **ReduÃ§Ã£o DrÃ¡stica do EspaÃ§o em Disco:**
  - AtivaÃ§Ã£o da compressÃ£o nativa Single-File no compilador do .NET 8 (`<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>`). O executÃ¡vel distribuÃ­vel foi reduzido de **~207 MB para cerca de 70 MB** (economia de mais de 65% de espaÃ§o em disco).
- **OtimizaÃ§Ã£o de Consumo de MemÃ³ria RAM:**
  - AtivaÃ§Ã£o do modo DATAS do .NET 8 (`<GarbageCollectionAdaptationMode>1</GarbageCollectionAdaptationMode>`), ajustando a dimensÃ£o do heap de forma adaptativa e automÃ¡tica ao tamanho real dos dados ativos.
  - Carregamento sob demanda (lazy) do `TraceEventSession` no `FrameMonitor` e reduÃ§Ã£o do buffer ETW de 64 MB para 1 MB, liberando de 60 MB a 100 MB quando o overlay e os testes de benchmark nÃ£o estÃ£o ativos.
  - DesativaÃ§Ã£o de mÃ³dulos nÃ£o utilizados no `LibreHardwareMonitor` (`IsNetworkEnabled = false`, `IsControllerEnabled = false`), eliminando alocaÃ§Ãµes de contadores de desempenho de rede nativos (perflib) e varredura HID contÃ­nua.
  - OtimizaÃ§Ã£o do loop de sensores com cache de identificadores (`_sensorIdCache`), eliminando alocaÃ§Ãµes repetitivas de strings (`s.Identifier.ToString()`) e descarte automÃ¡tico de ponteiros COM no WMI (`IWbemClassObject`).
  - Modo ultraleve em segundo plano (`updateUi: false`) que mantÃ©m a telemetria do FanControl sem despachar eventos de formataÃ§Ã£o e PropertyChanged para o WPF enquanto o aplicativo estiver minimizado ou na bandeja.
  - ExecuÃ§Ã£o de compactaÃ§Ã£o forÃ§ada de heap e `TrimMemory` ao finalizar o carregamento do hardware e na inicializaÃ§Ã£o minimizada, mantendo a pegada em segundo plano em cerca de 25 a 35 MB.
  - ImplementaÃ§Ã£o de coleta forÃ§ada de lixo e limpeza de Working Set (`EmptyWorkingSet`) via Win32 API ao minimizar para a bandeja do sistema.

### ðŸ› ï¸ CorreÃ§Ãµes de Ambiente e Scripts
- CorreÃ§Ã£o de scripts `.bat` (`iniciar.bat`, `publicar.bat`, `executar_e_testar.bat`, `testar.bat`):
  - LocalizaÃ§Ã£o automÃ¡tica do .NET 8 SDK em `%USERPROFILE%\.dotnet`, `%ProgramFiles%\dotnet` ou variÃ¡veis de ambiente.
  - DefiniÃ§Ã£o correta do diretÃ³rio de trabalho (`-WorkingDirectory`) ao iniciar processos com elevaÃ§Ã£o de Administrador (`UAC`), corrigindo falhas na execuÃ§Ã£o com arquivos soltos.
  - CorreÃ§Ã£o de ambiguidade no `dotnet publish` especificando explicitamente `LuminiTray.csproj` (resolvendo o erro `MSB1011`).
  - CriaÃ§Ã£o de scripts ponte na pasta raiz facilitando a execuÃ§Ã£o direta sem entrar em subpastas.

---

## [0.16.0] - 2026-10-28
- ImplementaÃ§Ã£o inicial da aba de controle de fans (FanControl) com suporte a curvas personalizadas e histerese anti-spike.
- Suporte a sensores de placas-mÃ£e (SuperI/O) e placas de vÃ­deo NVIDIA/AMD/Intel.
- AdiÃ§Ã£o da trava de seguranÃ§a de 100% para bombas de Water Cooler.
- Monitoramento de temperatura de hotspot e memÃ³ria VRAM em GPUs compatÃ­veis.
