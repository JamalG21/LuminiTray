# Lumini Tray

<p align="center">
  <strong>Utilitário gamer de alto desempenho e diagnóstico para a bandeja do Windows</strong><br>
  Monitor de hardware, controle inteligente de ventoinhas, motor de wallpapers animados e tweaks do sistema com consumo ultrabaixo de recursos.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Versão-0.24.1-blue.svg?style=flat-square" alt="Versão">
  <img src="https://img.shields.io/badge/.NET-8.0_WPF-purple.svg?style=flat-square" alt=".NET 8">
  <img src="https://img.shields.io/badge/Plataforma-Windows_10_%2F_11_x64-0078D6.svg?style=flat-square" alt="Windows">
  <img src="https://img.shields.io/badge/Licença-Open_Source-green.svg?style=flat-square" alt="Licença">
</p>

---

## 🚀 Principais Funcionalidades

- **❄️ Controle Inteligente de Fans:**
  - Curvas de ventoinha totalmente customizáveis para CPU e GPU com pontos de controle manuais e fail-safe na BIOS.
  - Inicialização sob demanda de drivers NVIDIA (NvAPI) e AMD (ADL) para consumo mínimo de memória.

- **📊 Monitor de Métricas e Sensores em Tempo Real:**
  - Leitura precisa de temperatura, clocks, voltagens e consumo (W) via LibreHardwareMonitor.
  - Subtarefas identificadas individualmente no Gerenciador de Tarefas do Windows.
  - Repouso automático do timer (`0.0% CPU`) e compactação de memória RAM quando minimizado na bandeja.

- **🎬 Motor Nativo de Wallpapers (Animados e Estáticos):**
  - Renderização de vídeos (.mp4/.webm) acoplada diretamente à Área de Trabalho do Windows (WorkerW/Progman) com decodificação por hardware (Direct3D 11 / Vulkan via MPV).
  - Suporte independente a múltiplos monitores (papel de parede estático em uma tela e animado na outra).
  - **Modo Gamer Automático:** Pausa instantânea da reprodução em jogos ou aplicativos em tela cheia (0% CPU/GPU).
  - Slideshow automático nativo no Windows para wallpapers estáticos sem uso de memória adicional.

- **🔋 Modo Baixo Consumo de Recursos:**
  - Alternador de um clique para desligar utilitários secundários e manter o aplicativo em consumo mínimo.
  - **Restauração Inteligente:** Lembra e restaura o estado exato de cada recurso ativo ao desativar o modo econômico.

- **🎧 Tweaks de Áudio & Supressor de Ruído:**
  - Supressão de ruído em tempo real para microfone via rede neural RNNoise.
  - Ajuste de volume girando a roda do mouse sobre a barra de tarefas do Windows.
  - Atalhos globais para mutar saída de áudio, microfone e alternar dispositivos de reprodução/gravação.

- **🎮 Overlay para Jogos e Diagnóstico:**
  - Overlay minimalista para visualização de métricas durante jogos (`Ctrl + Shift + O`).
  - Benchmarks integrados (CPU, GPU, RAM, Discos), stress tests e limpador de memória RAM.

---

## 📦 Como Baixar e Executar

1. Acesse a aba **[Releases](https://github.com/JamalG21/LuminiTray/releases)** do repositório.
2. Baixe o pacote executável `LuminiTray-v0.24.1-Windows-x64.zip`.
3. Extraia o conteúdo em uma pasta de sua escolha e execute o `LuminiTray.exe`.
   - *Nota:* O aplicativo é portátil e autônomo (não requer instalação prévia do .NET Runtime).
   - Execute como Administrador para habilitar a leitura de sensores de baixo nível da placa-mãe e controle de fans.

---

## 🛠️ Como Compilar a Partir do Código-Fonte

### Requisitos:
- **Windows 10 ou 11 (64 bits)**
- **.NET 8.0 SDK** (com suporte a Desktop WPF)

### Compilando o Projeto:
```powershell
# Restaurar dependências e compilar
dotnet build LuminiTray/LuminiTray.csproj -c Release

# Publicar executável único distribuível (Single-File)
dotnet publish LuminiTray/LuminiTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./dist
```

---

## 📄 Licença

Distribuído sob a licença de código aberto. Consulte o arquivo [CHANGELOG.md](CHANGELOG.md) para o histórico completo de versões e melhorias.
