# Changelog - Lumini Tray

Todas as alterações notáveis deste projeto serão documentadas neste arquivo.
O formato é baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [0.34.0] - 2026-11-20

### 🚀 Adicionado & Aprimorado (Expansão Otimização & Debloat via Win11Debloat 2026.10.30)
- **Novos Ajustes de Registro Reversíveis (`WindowsDebloatService`):** adicionados novos tweaks organizados em subgrupos filtráveis na aba **Otimização & Debloat**, preservando o backup e a reversão completa (`AppSettings.TweakBackup`) sem alterar o perfil automático recomendado:
  - **IA & Navegadores:** bloqueio do serviço `WSAIFabricSvc`, *Click to Do*, Copilot/IA no Microsoft Edge, Gemini/IA no Google Chrome, IA no Bloco de Notas e Paint, além de remoção de anúncios/sugestões no Edge e bloatware no Brave.
  - **Telemetria, Privacidade & Iniciar:** localização, *Localizar Meu Dispositivo*, anúncios Microsoft 365 e página inicial nas Configurações, dicas na tela de bloqueio, *Windows Spotlight*, painel *Vincular ao Celular*, seções do Menu Iniciar e notificações toast.
  - **Explorador & Menu de Contexto:** ocultar *Início*, *Galeria*, *OneDrive* e unidades removíveis duplicadas; remover *Conceder acesso a*, *Incluir na biblioteca* e *Compartilhar*; exibir letras das unidades primeiro.
  - **Barra de Tarefas & Multitarefa:** ativar *Finalizar Tarefa* no clique direito da barra de tarefas, clique na última janela ativa, desativação de *Snap Assist* / *Snap Layouts* / *Aero Snap*, ocultar abas do Edge no `Alt+Tab`, desativar animações e integração `ms-gamebar`.
  - **Sistema & Windows Update:** desativar *Fast Startup*, rede em *Modern Standby*, criptografia automática BitLocker, *Storage Sense*, atalho de Teclas de Aderência (`Shift 5x`), Otimização de Entrega P2P, download automático de apps/ícones de dispositivos, atualizações de drivers via Windows Update, reinicialização automática e atualizações antecipadas.
- **Filtros na Interface (`MainWindow.xaml` & `MainWindow.WindowsDebloat.cs`):** adicionados chips de filtro por subgrupo acima da lista de Telemetria, Privacidade & Sistema.
- **Catálogo de Apps Validado (`Apps.json`):** confirmada a sincronia completa dos 162 aplicativos catalogados no `Win11Debloat-2026.10.30` (incluindo `Microsoft PC Manager` e `Microsoft Edge`).

## [0.33.4] - 2026-11-20

### ⚡ Performance & Otimização (Redução de RAM com Janela Aberta)
- **Trim de Memória Pós-Inicialização e Periódico (`MainWindow`):** restaurado `TrimMemory(force: true)` após a carga inicial de sensores com a janela aberta (+ compactação agendada 1,2s após renderização/restauração) e ciclo periódico a cada 15s fora de benchmarks/stress, liberando páginas transitórias do XAML/JIT/driver que ficavam retidas no Working Set.
- **Lazy Loading Real na `Visão geral` (`InitFastCpuAndGpuStubs`):** removidas as coletas pesadas de WMI e DXGI (`EnsureSystemInfoLoadedAsync` e `EnsureGpuInfoLoadedAsync`) da abertura da tela inicial; CPU, GPU principal e NVML agora são identificados em `< 1 ms` direto pelo `LibreHardwareMonitor` / Registro / NVML, carregando o WMI completo apenas ao abrir `Sistema`, `Placa de vídeo`, `Estresse` ou ao exportar relatório.
- **Inicialização Sob Demanda de Buffers e Janelas Ocultas:** `BenchRenderer`, `WriteableBitmap` do Benchmark, `InitGames`, `InitOc`, `MiniTaskManagerWindow`, `TaskbarVolumeOsdWindow` e `MicOverlayWindow` passam a ser instanciados apenas quando suas abas/recursos são efetivamente utilizados.
- **Redução de Churn de UI em Sensores Ocultos (`SensorMonitor` / `SensorRow`):** na `Visão geral`, a leitura dos sensores atualiza os valores em memória (`updateUi: false`) sem disparar centenas de notificações `PropertyChanged` por segundo na grade oculta `SensorGrid`; os bindings são sincronizados (`SyncRowsToUi()`) ao abrir `Sensores`, `Monitoramento` ou `Placa de vídeo`.
- **Descarte COM no Coletor de GPU (`GpuInfoCollector`):** adicionados blocos `using` explícitos em `ManagementObjectCollection` e `ManagementObject` para evitar retenção de handles nativos de WMI.

## [0.33.3] - 2026-11-20

### ⚡ Performance & Otimização (Fase 3 — Módulos Híbridos em Rust)
- **Varredura de Disco Nativa (`disk.rs` ↔ `DiskSpaceAnalyzer`):** implementado scanner recursivo Win32 `FindFirstFileExW` (`FindExInfoBasic` + `FIND_FIRST_EX_LARGE_FETCH`) em Rust (`ABI_VERSION = 2`) ignorando *reparse points* (`FILE_ATTRIBUTE_REPARSE_POINT`) e serializando a árvore + extensões em formato binário `LTDS` para o C#, mantendo fallback automático via `ScanManagedAsync`.
- **Store e Métricas de Frame Time Nativo (`frames.rs` ↔ `FrameMonitor`):** transferida a fila temporal por PID (`FrameStore` com janela de 10s / corte de inatividade de 2s) e cálculo de FPS médio / Frametime / 1% Low (`nc_frames_compute_metrics`) para memória não-gerenciada fora do GC do .NET, mantendo `ComputeFrameMetricsManaged` como fallback.
- **Pipeline DSP de Microfone em Rust (`dsp.rs` ↔ `NoiseSuppressionEngine`):** portados filtro passa-altas 80 Hz, EQ paramétrico de 3 bandas, *Noise Gate* inteligente com *sidechain HPF*, histerese (*Schmitt Trigger*), *Hold Time*, *VAD grace*, de-esser de 6,5 kHz e limitador suave (`SoftLimit`) para execução nativa em memória contígua de 480 amostras (48 kHz), preservando a chamada neural ao `rnnoise.dll` e o fallback gerenciado `ProcessManaged`.
- **Testes de Equivalência (`OverlayAndAppsTests` + `cargo test`):** validada paridade funcional e numérica entre implementações nativas em Rust e gerenciadas em C# nos três módulos.

## [0.33.2] - 2026-11-20

### 🛠️ Correções & Melhorias (Fase 2 — Infraestrutura Híbrida Rust)
- **Crate Nativa Rust (`LuminiTray.NativeCore`):** adicionada biblioteca `cdylib` x64 (`x86_64-pc-windows-msvc`, ~121 KB) com toolchain pinada (`rust-toolchain.toml`), contrato ABI versionado (`ABI_VERSION = 1`), guarda `catch_unwind` anti-panic na fronteira FFI, alocador/liberador seguro (`nc_alloc_buffer` / `nc_free_buffer`) e canal de erro UTF-16 (`nc_last_error`).
- **Ponte Segura C# ↔ Rust (`NativeCoreBridge`):** validação de versão ABI e integridade **SHA-256** da DLL, carregamento local ou via recurso embutido, fallback automático e silencioso para C# em caso de ausência ou incompatibilidade, interfaces de estratégia para a Fase 3 (`IDiskScannerBackend`, `IFrameMonitorBackend`, `IVoiceDspBackend`) e chave `AppSettings.NativeCoreEnabled` / `LUMINI_FORCE_MANAGED=1`.
- **Integração Incremental no Build (`LuminiTray.csproj`):** target MSBuild `BuildRustNativeCore` executa `cargo build --release` quando `cargo` está disponível e utiliza a DLL pré-compilada em `Native\` caso contrário.

## [0.33.1] - 2026-11-20

### ⚡ Performance & Otimização (Fase 1 C# / .NET 8)
- **Inicialização Preguiçosa (*Lazy Loading*) de WMI e GPU:** a coleta pesada de informações estáticas do sistema e de GPUs (`LoadInfo` / `LoadGpu`) agora carrega sob demanda ao abrir a aba correspondente, ao exportar relatório ou em baixa prioridade ociosa — evitando desperdício de RAM e CPU quando o app inicia minimizado na bandeja.
- **Cache Centralizado de Topologia de Discos (`DiskTopologyService`):** unificadas as consultas WMI `Win32_DiskDrive` / `Partition` / `LogicalDisk` com cache de 30s, invalidação automática por `WM_DEVICECHANGE` e descarte explícito de objetos COM (`ManagementBaseObject.Dispose()`).
- **Eliminação de Churn GDI+ / HICON na Bandeja (`SystemTrayManager` & `GuidNotifyIcon`):** ícones de microfone, áudio, mini-tarefas e bateria de periféricos agora verificam chave de estado em cache (`LastIconKey`) e descartam ícones antigos antes de recriar handles Win32.
- **Hot-Paths com Zero Alocação Extra:**
  - `DiskSpaceItem.CategoryBrush` agora reutiliza instâncias congeladas (`Frozen`) em cache por categoria em vez de alocar um `SolidColorBrush` por leitura.
  - `FrameMonitor` (`ComputeFrameMetrics` + `PidNameCache`) utiliza `ArrayPool<double>` + `Array.Sort` in-place e cache de nomes por PID (15s) para reduzir alocações no overlay em jogos.
  - `TrimMemory()` recebeu debounce de 8s e `<RetainVMGarbageCollection>false</RetainVMGarbageCollection>` no projeto.
- **Economia Visual no Modo Baixo Consumo:** estende o `Modo Baixo Consumo` para desacelerar polling de sensores na UI (2s), desativar animação de popups e pausar timers puramente visuais ao ativar.
- **Script de Medição (`Tools/Measure-LuminiResources.ps1`):** adicionado utilitário em `desenvolvimento/` para aferir RAM privada, Working Set e CPU% antes/depois.

## [0.33.0] - 2026-11-18

### 🚀 Adicionado & Aprimorado
- **Nova Sub-aba Monitoramento (`LUMINI INFORMAÇÕES` → `Monitoramento`):**
  - Tabela estilo HWMonitor mais detalhada exibindo **Valor Atual**, **Mínimo + Horário exato (`HH:mm:ss`)** e **Máximo + Horário exato**, busca por hardware/sensor, filtro por tipo e botão **Zerar Mín/Máx**.
  - **Monitoramento Registrado Programável:** permite selecionar qualquer hardware ou sensor individualmente (botões *Principais*, *Todos*, *Limpar* e toggle por hardware), configurar livremente o **intervalo entre leituras** (segundos/minutos) e a **duração total da sessão** (minutos/horas).
  - **Gravação Temporizada e Limpeza Automática:** registra em disco apenas durante a duração programada (inclusive com o programa minimizado na bandeja); ao finalizar, consolida em um **resumo breve** (Mín + horário, Média, Máx + horário, total de leituras) com **gráfico interativo por sensor** (trocável via seletor com tooltip) e apaga automaticamente o arquivo bruto temporário do disco.
  - **Inclusão no Relatório do PC:** a sessão de monitoramento (tabela resumida + gráficos SVG embutidos no HTML e séries no JSON) agora faz parte do botão **EXPORTAR RELATÓRIO**.
- **Modo Privacidade Global (Ícone de Olho na Barra Superior):**
  - Botão de olho ao lado do ícone de atualização na barra superior que oculta instantaneamente dados sensíveis em todas as abas (`Sistema`, `Rede & DNS`, `Saúde dos Discos` e `Periféricos/Bluetooth`), mascarando endereços IPv4/IPv6, MAC, Gateway, DNS, SSID Wi-Fi, números de série, UUID, nome do computador e usuário por `••••`.
  - Quando ativado, a mesma proteção é aplicada automaticamente ao **Relatório do PC exportado** (HTML e JSON).

## [0.32.1] - 2026-11-18

### 🛠️ Correções & Melhorias
- **Exibição Estrita de Periféricos Atualmente Conectados:**
  - Dispositivos Bluetooth pareados mas desconectados (com cache antigo de bateria no Windows), periféricos em modo de espera e receptores 2.4 GHz vazios sem periférico ativo agora são ocultados automaticamente da lista e da bandeja.
  - Ícones individuais fixados com **"+ Bandeja"** ocultam-se automaticamente quando o periférico é desligado e reaparecem na mesma posição assim que ele reconecta.
- **Ícones da Bandeja 100% Independentes com `GUID` Fixo (`GuidNotifyIcon`):**
  - Substituídos os ícones de bateria por um wrapper nativo `Shell_NotifyIconW` com `NIF_GUID` e `GUID` determinístico por dispositivo. O ícone principal (checkbox *"Exibir ícone de bateria na bandeja"*, troca por duplo clique) e cada ícone individual (**"+ Bandeja"**) agora são totalmente independentes e não se movem juntos na barra de tarefas do Windows 11.
- **Identificação Multi-Marca e Correção do XInput em Dongles 2.4 GHz (`PeripheralDeviceCatalog`):**
  - Correlacionamento de slots XInput aos dispositivos USB/HID/PnP reais (lendo `DEVPKEY_Device_BusReportedDeviceDesc`, `HidD_GetManufacturerString` e `HidD_GetProductString`), corrigindo controles sem fio via dongle 2.4 GHz como o **Machenike G5 Pro V2** (`1A34:F517` / `2345:E02F`), **GameSir**, **8BitDo**, **Flydigi**, **EasySMX** e **BigBig Won** que antes apareciam incorretamente como `"Controle Xbox / XInput #1 · Cabo USB · 100% (Cabeado)"`. Agora exibem o modelo real, o selo **`📡 Dongle 2.4 GHz`** e status **`Conectado`** sem inventar porcentagem falsa.
  - Adicionada leitura do nome real via **Logitech HID++ Feature `0x0005` (GetDeviceName)** para dispositivos em receptores Unifying/Lightspeed/Bolt e catálogo expandido com 45+ fabricantes de mouses, teclados, headsets e controles.

## [0.32.0] - 2026-11-18

### 🚀 Adicionado & Aprimorado
- **Aprimoramento da Bateria de Periféricos & Identificação de Dongle 2.4 GHz (baseado no Wireless-Device-Battery-Monitor):**
  - **Identificação de Conexão / Dongle:** Cada periférico agora exibe um selo visual indicando se está conectado via **`📡 Dongle 2.4 GHz`**, **`ᛒ Bluetooth`**, **`🔌 Cabo USB`** ou **`🎮 XInput`**, tanto na aba **Periféricos → Dispositivos** quanto no menu da bandeja. Mesmo receptores/dongles 2.4 GHz que não expõem leitura de bateria via HID passam a ser identificados como ativos via dongle.
  - **Leitura Genérica HID Padrão:** Adicionado fallback nativo via `HidP_GetValueCaps` / `HidP_GetUsageValue` para ler bateria padrão HID (`UsagePage 0x06 / Usage 0x20` e `0x85 / 0x65`) com deduplicação por `ContainerId` contra Bluetooth (ignorando Digitizers e UPS).
  - **Novos Protocolos Nativos Suportados:** **ASUS ROG** (incluindo Omni Receiver `0x1ACE` e detecção de modo de espera), **Kysona / ATK / VXE / Pulsar / Hitscan / HECATE / Redragon CompX** (com leitura de tensão em mV), **Rapoo VT** (Nordic 54L e VT3 Pro legado), **Redragon Areson 2.4 GHz** (`0x25A7`), **AULA / MCHOSE / Redragon Horus Pro** e **Nintendo Switch Pro / GameSir (modo Switch)**.

## [0.31.2] - 2026-11-17

### 🛠️ Correções
- **Ícone do Microfone do Windows Voltando a Aparecer:**
  - Adicionado observador em `UIElement::VisibilityProperty` no hook `LuminiTray.TaskbarHook.v4.dll` para interceptar e re-ocultar imediatamente o ícone de microfone (e demais ícones de `MainStack`) sempre que o Windows 11 tentar restaurar sua visibilidade ao iniciar uma captura de áudio, além de monitorar elementos `SystemTray.TextIconContent` adicionados dinamicamente.

## [0.31.1] - 2026-11-17

### 🛠️ Correções
- **Ícones Individuais & Troca por Duplo Clique na Bandeja de Bateria:**
  - Adicionado botão **"+ Bandeja" / "✓ Na Bandeja"** em cada dispositivo conectado na sub-aba **Dispositivos** para fixar ou remover ícones individuais na bandeja do sistema.
  - Quando houver apenas **1 ícone de bateria** na bandeja, clicar **2x** sobre ele alterna diretamente para o próximo periférico conectado (atualizando anel, porcentagem e tooltip).

## [0.31.0] - 2026-11-17

### 🚀 Adicionado & Aprimorado
- **Módulo de Bateria de Periféricos Sem Fio (Aba Dispositivos):**
  - Novo card **BATERIA DE PERIFÉRICOS SEM FIO** no topo da sub-aba **Periféricos → Dispositivos** (baseado no HaloBattery, 100% nativo em C# sem Python ou dependências externas).
  - Suporte nativo a **Bluetooth / BLE** (`DEVPKEY_Bluetooth_Battery` via PnP), **Controles Xbox / XInput** (`xinput1_4.dll`), e protocolos diretos **HID 2.4 GHz / USB** (**Logitech HID++ 2.0/1.0**, **Razer**, **PlayStation DualShock 4 / DualSense**, **Corsair Slipstream/Void Elite**, **SteelSeries Arctis/Nova**, **HyperX Cloud**, **Keychron**, **JBL Quantum**, **Glorious / Pulsar / Lamzu / WLmouse / ATK / Mchose / Endgame**).
  - Atualização automática a cada **30s** somente enquanto a sub-aba **Dispositivos** estiver aberta (**0% CPU** em segundo plano), alerta Toast opcional para bateria baixa (`< 20%`) e opção de **ícone circular na bandeja** (estilo HaloBattery, desativado por padrão).

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