# Diretrizes e Regras do Projeto Lumini Tray

Estas regras são permanentes e devem ser rigorosamente seguidas em todas as interações e atualizações do projeto.

## 1. Controle de Versão e Documentação de Mudanças (Changelog)
- **Incremento de Build Obrigatório:** A cada nova funcionalidade, alteração relevante, refatoração ou correção de bugs, a versão da build DEVE ser incrementada no arquivo de projeto `LuminiTray/LuminiTray.csproj` nas tags `<Version>`, `<AssemblyVersion>` e `<FileVersion>` seguindo o versionamento semântico (SemVer: `MAJOR.MINOR.PATCH`).
- **Registro no Changelog:** Toda alteração DEVE ser documentada e registrada no arquivo `CHANGELOG.md` na raiz do projeto, sob o novo número de versão e a data correspondente, categorizando em seções claras como `### 🚀 Adicionado & Aprimorado`, `### ⚡ Performance & Otimização` ou `### 🛠️ Correções`.

## 2. Política de Publicação do Executável (.exe)
- **Não publicar automaticamente:** NUNCA execute scripts de publicação como `publicar.bat` ou comandos de publicação `dotnet publish` para gerar executáveis únicos distribuíveis (`Single-File Release`) automaticamente, exceto quando o usuário solicitar expressamente (ex: *"gere o executável"*, *"publique o .exe"*, *"faça a release"*).
- **Validação de Código:** Para validação e verificação de alterações de código, utilize estritamente `dotnet build` e `dotnet test`.

## 3. Padrões de Design e Temas do Lumini Tray
- **Suporte a Tema Escuro e Claro:** Todo novo controle, texto (`TextBlock`) ou lista (`ListView`, `ItemsControl`) deve utilizar os recursos dinâmicos da aplicação (`{DynamicResource Fg}`, `{DynamicResource Muted}`, `{DynamicResource CardSubtleBg}`, `{DynamicResource Line}`, `{DynamicResource Accent}`). Nunca deixe texto sem cor explícita para evitar o uso da cor padrão do sistema (preto).
- **Layout de Listas WPF:** Em `ListView` com `DataTemplate`, sempre configure `HorizontalContentAlignment="Stretch"` no `ItemContainerStyle` para que colunas fluídas expandam por completo e elementos com alinhamento à direita fiquem perfeitamente posicionados.
