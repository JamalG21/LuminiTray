# Diretrizes e Regras do Projeto Lumini Tray

Estas regras são permanentes e devem ser rigorosamente seguidas em todas as interações e atualizações do projeto.

## 1. Controle de Versão Semântico (SemVer 2.0.0) e Changelog
- **Adesão Estrita ao SemVer (https://semver.org/lang/pt-BR/):** O projeto adota oficialmente a especificação do Versionamento Semântico 2.0.0 no formato estrito `MAJOR.MINOR.PATCH` (X.Y.Z):
  - **`PATCH` (Correção / Terceiro Número - X.Y.Z):** Deve ser incrementado obrigatoriamente ao aplicar correções de falhas (*bug fixes*), pequenas refatorações internas ou ajustes que não adicionem novas funcionalidades nem quebrem a compatibilidade (ex: de `0.24.1` para `0.24.2`).
  - **`MINOR` (Menor / Número do Meio - X.Y.0):** Deve ser incrementado obrigatoriamente quando uma nova funcionalidade for adicionada ao software de forma retrocompatível. Ao incrementar o número `MINOR`, o `PATCH` DEVE ser reiniciado em zero `0` (ex: de `0.24.x` para `0.25.0`).
  - **`MAJOR` (Maior / Primeiro Número - X.0.0):** Deve ser incrementado ao introduzir modificações que quebrem compatibilidade (*breaking changes*), reestruturações de grande impacto ou ao atingir a maturidade para a versão pública estável `1.0.0`. Ao incrementar `MAJOR`, tanto `MINOR` quanto `PATCH` DEVEM ser reiniciados em zero `0`.
  - **Imutabilidade de Versões Publicadas:** Uma versão lançada jamais deve ter seu conteúdo alterado; qualquer modificação futura exige um novo incremento de versão.
  - **Sincronização nos Arquivos de Projeto:** Sempre atualizar as tags `<Version>`, `<AssemblyVersion>` e `<FileVersion>` no arquivo de projeto `LuminiTray.csproj` em conjunto com a nova versão.
- **Registro no Changelog:** Toda alteração DEVE ser documentada e registrada no arquivo `CHANGELOG.md`, sob o novo número de versão e a data correspondente, categorizando em seções claras como `### 🚀 Adicionado & Aprimorado`, `### ⚡ Performance & Otimização` ou `### 🛠️ Correções`.

## 2. Política de Publicação do Executável (.exe)
- **Não publicar automaticamente:** NUNCA execute scripts de publicação como `publicar.bat` ou comandos de publicação `dotnet publish` para gerar executáveis únicos distribuíveis (`Single-File Release`) automaticamente, exceto quando o usuário solicitar expressamente (ex: *"gere o executável"*, *"publique o .exe"*, *"faça a release"*).
- **Validação de Código:** Para validação e verificação de alterações de código, utilize estritamente `dotnet build` e `dotnet test`.

## 3. Padrões de Design e Temas do Lumini Tray
- **Suporte a Tema Escuro e Claro:** Todo novo controle, texto (`TextBlock`) ou lista (`ListView`, `ItemsControl`) deve utilizar os recursos dinâmicos da aplicação (`{DynamicResource Fg}`, `{DynamicResource Muted}`, `{DynamicResource CardSubtleBg}`, `{DynamicResource Line}`, `{DynamicResource Accent}`). Nunca deixe texto sem cor explícita para evitar o uso da cor padrão do sistema (preto).
- **Layout de Listas WPF:** Em `ListView` com `DataTemplate`, sempre configure `HorizontalContentAlignment="Stretch"` no `ItemContainerStyle` para que colunas fluídas expandam por completo e elementos com alinhamento à direita fiquem perfeitamente posicionados.

## 4. Política de Publicação e Sincronização com o GitHub (Ciclo SemVer)
- **Publicação Conforme Versionamento Semântico:** As versões são publicadas no repositório GitHub (`https://github.com/JamalG21/LuminiTray.git`) acompanhando o ciclo natural do SemVer 2.0.0 (seja PATCH, MINOR ou MAJOR) a cada versão lançada e validada, criando sua respectiva tag de versão (ex: `v0.24.2`).
- **Registro Rigoroso no Changelog:** Toda publicação deve vir acompanhada da atualização correspondente no `CHANGELOG.md`.
- **Estrutura de Pastas (Desenvolvimento vs Release):**
  - **`desenvolvimento/`:** Diretório de trabalho onde ficam os arquivos de teste, depuração e os scripts `.bat` (`iniciar.bat`, `publicar.bat`, `executar_e_testar.bat`, `testar.bat`). Nenhum arquivo `.bat` deve ir para o GitHub.
  - **`release/`:** Diretório que contém estritamente os arquivos limpos e finais que compõem o repositório do GitHub. É proibido conter scripts `.bat` nesta pasta.

## 5. Padrão de Fábrica e Higienização Pré-Publicação (Clean Factory State)
- **Zeramento Obrigatório de Dados e Preferências:** Antes de qualquer publicação, release ou sincronização para o repositório público (`release/` e GitHub), é terminantemente OBRIGATÓRIO zerar todas as configurações, preferências, histórico e dados locais da máquina do desenvolvedor:
  - **Estado Padrão de Fábrica (Factory Defaults):** Nenhuma configuração pessoal (ex: volume preferido, curvas de fans calibradas para o hardware local, atalhos modificados, monitores específicos, dispositivos de áudio selecionados ou preferências locais) deve ser incluída nos arquivos versionados ou pacotes de release. O software deve chegar ao repositório sempre no seu estado virgem/padrão de fábrica.
  - **Limpeza de Histórico e Caches:** Devem ser eliminados quaisquer arquivos residuais de histórico de navegação, relatórios de benchmark locais, arquivos temporários de teste, dumps de memória, logs de depuração ou arquivos de estado de execução (ex: `post_format_state.json`, `*.log`, caches em `%AppData%` ou `%LocalAppData%`).
  - **Sanitização de Caminhos Absolutos:** Nenhum caminho absoluto contendo diretórios do computador local (ex: `C:\Users\...`, unidades ou nomes de usuário) deve estar gravado no código ou em arquivos de configuração do repositório.
