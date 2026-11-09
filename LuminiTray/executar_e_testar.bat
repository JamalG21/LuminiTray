@echo off
setlocal enabledelayedexpansion
title LUMINI TRAY - Executar e Testar

cd /d "%~dp0"

:: -------------------------------------------------------------
:: Localizar o .NET SDK
:: -------------------------------------------------------------
set "DOTNET_EXE="
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
    set "DOTNET_EXE=%USERPROFILE%\.dotnet\dotnet.exe"
) else if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%ProgramFiles%\dotnet"
    set "PATH=%ProgramFiles%\dotnet;%PATH%"
    set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
) else if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%ProgramFiles(x86)%\dotnet"
    set "PATH=%ProgramFiles(x86)%\dotnet;%PATH%"
    set "DOTNET_EXE=%ProgramFiles(x86)%\dotnet\dotnet.exe"
) else if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
    set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
    set "DOTNET_EXE=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
) else (
    where dotnet >nul 2>&1
    if !errorlevel! equ 0 (
        set "DOTNET_EXE=dotnet"
    )
)

if "%DOTNET_EXE%"=="" (
    echo.
    echo ========================================================================
    echo [ERRO] .NET SDK nao foi encontrado no sistema!
    echo Instale o .NET 8 SDK em: https://dotnet.microsoft.com/download/dotnet/8.0
    echo ========================================================================
    echo.
    pause
    exit /b 1
)

:: -------------------------------------------------------------
:: Verifica argumentos passados por linha de comando
:: -------------------------------------------------------------
if /i "%~1"=="test" goto RUN_TESTS
if /i "%~1"=="--test" goto RUN_TESTS
if /i "%~1"=="-t" goto RUN_TESTS

if /i "%~1"=="run" goto RUN_APP
if /i "%~1"=="--run" goto RUN_APP
if /i "%~1"=="-r" goto RUN_APP

if /i "%~1"=="all" goto RUN_ALL
if /i "%~1"=="--all" goto RUN_ALL

if /i "%~1"=="auto" goto RUN_AUTO
if /i "%~1"=="--auto" goto RUN_AUTO
if /i "%~1"=="--autostart" goto RUN_AUTO

if /i "%~1"=="build" goto RUN_BUILD
if /i "%~1"=="--build" goto RUN_BUILD

:: -------------------------------------------------------------
:: Menu Interativo
:: -------------------------------------------------------------
:MENU
cls
echo ========================================================================
echo                 LUMINI TRAY - EXECUTAR E TESTAR
echo ========================================================================
echo.
echo  Selecione uma opcao:
echo.
echo   [1] Iniciar o LUMINI TRAY (Padrao - pressione ENTER)
echo   [2] Executar Testes Automatizados (validar funcionamento)
echo   [3] Executar Testes e Iniciar o Projeto
echo   [4] Testar Modo Inicializacao e Funcoes Automaticas (--autostart)
echo   [5] Publicar Executavel para Distribuicao (Release Single-File)
echo   [0] Sair
echo.
echo ========================================================================

set "choice=1"
set /p "choice=Digite a opcao desejada [Padrao: 1]: "

if "%choice%"=="1" goto RUN_APP
if "%choice%"=="2" goto RUN_TESTS
if "%choice%"=="3" goto RUN_ALL
if "%choice%"=="4" goto RUN_AUTO
if "%choice%"=="5" goto RUN_PUBLISH
if "%choice%"=="0" goto EXIT_SCRIPT

echo Opcao invalida.
pause
goto MENU

:: -------------------------------------------------------------
:: Opcao 1: Executar o Projeto (Compila e Inicia o Executavel)
:: -------------------------------------------------------------
:RUN_APP
echo.
echo ========================================================================
echo                  INICIANDO O LUMINI TRAY...
echo ========================================================================
echo.
echo [1/2] Compilando projeto...
taskkill /f /im LuminiTray.exe >nul 2>&1
taskkill /f /im LuminiTray.exe >nul 2>&1
taskkill /f /im LuminiTray.Wallpaper.exe >nul 2>&1
taskkill /f /im mpv.exe >nul 2>&1
taskkill /f /im mpv-core.exe >nul 2>&1
taskkill /f /im MicMute.exe >nul 2>&1
"%DOTNET_EXE%" build LuminiTray.csproj -c Debug --nologo -v m
if errorlevel 1 (
    echo.
    echo [ERRO] Falha na compilacao. Veja os erros acima.
    echo.
    pause
    if "%~1"=="" goto MENU
    exit /b 1
)

echo [2/2] Abrindo aplicativo com privilegios de administrador...
set "EXE_PATH=%~dp0bin\Debug\net8.0-windows\LuminiTray.exe"
set "EXE_DIR=%~dp0bin\Debug\net8.0-windows"
if not exist "%EXE_PATH%" (
    if exist "%~dp0bin\Debug\net8.0-windows\LuminiTray.exe" (
        set "EXE_PATH=%~dp0bin\Debug\net8.0-windows\LuminiTray.exe"
        set "EXE_DIR=%~dp0bin\Debug\net8.0-windows"
    ) else if exist "%~dp0..\LuminiTray-dist\LuminiTray.exe" (
        set "EXE_PATH=%~dp0..\LuminiTray-dist\LuminiTray.exe"
        set "EXE_DIR=%~dp0..\LuminiTray-dist"
    ) else if exist "%~dp0..\LuminiTray-dist\LuminiTray.exe" (
        set "EXE_PATH=%~dp0..\LuminiTray-dist\LuminiTray.exe"
        set "EXE_DIR=%~dp0..\LuminiTray-dist"
    ) else (
        echo [ERRO] LuminiTray.exe nao encontrado.
        pause
        if "%~1"=="" goto MENU
        exit /b 1
    )
)

net session >nul 2>&1
if %errorlevel% equ 0 (
    start "" /D "%EXE_DIR%" "%EXE_PATH%"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%EXE_PATH%' -WorkingDirectory '%EXE_DIR%' -Verb RunAs" 2>nul || start "" /D "%EXE_DIR%" "%EXE_PATH%"
)

echo.
echo [SUCESSO] LUMINI TRAY iniciado!
echo.
if "%~1"=="" (
    timeout /t 3 >nul
    exit /b 0
)
exit /b 0

:: -------------------------------------------------------------
:: Opcao 2: Executar Testes Automatizados
:: -------------------------------------------------------------
:RUN_TESTS
echo.
echo ========================================================================
echo               EXECUTANDO TESTES AUTOMATIZADOS (dotnet test)...
echo ========================================================================
echo.
"%DOTNET_EXE%" test LuminiTray.Tests\LuminiTray.Tests.csproj --verbosity normal
set TEST_RESULT=%errorlevel%
echo.
if %TEST_RESULT% equ 0 (
    echo ========================================================================
    echo [SUCESSO] Todos os testes automatizados passaram com exito!
    echo ========================================================================
) else (
    echo ========================================================================
    echo [FALHA] Alguns testes falharam com codigo de saida: %TEST_RESULT%
    echo ========================================================================
)
echo.
if "%~1"=="" (
    pause
    goto MENU
)
exit /b %TEST_RESULT%

:: -------------------------------------------------------------
:: Opcao 3: Executar Testes e depois Abrir o Projeto
:: -------------------------------------------------------------
:RUN_ALL
echo.
echo ========================================================================
echo [PASSO 1/2] Executando testes automatizados...
echo ========================================================================
"%DOTNET_EXE%" test LuminiTray.Tests\LuminiTray.Tests.csproj --verbosity normal
if errorlevel 1 (
    echo.
    echo [ERRO] Os testes falharam! O aplicativo nao sera iniciado.
    echo.
    if "%~1"=="" (
        pause
        goto MENU
    )
    exit /b 1
)

echo.
echo ========================================================================
echo [PASSO 2/2] Testes aprovados! Iniciando o LUMINI TRAY...
echo ========================================================================
echo.
set "EXE_PATH=%~dp0bin\Debug\net8.0-windows\LuminiTray.exe"
set "EXE_DIR=%~dp0bin\Debug\net8.0-windows"
net session >nul 2>&1
if %errorlevel% equ 0 (
    start "" /D "%EXE_DIR%" "%EXE_PATH%"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%EXE_PATH%' -WorkingDirectory '%EXE_DIR%' -Verb RunAs" 2>nul || start "" /D "%EXE_DIR%" "%EXE_PATH%"
)

if "%~1"=="" (
    timeout /t 3 >nul
    exit /b 0
)
exit /b 0

:: -------------------------------------------------------------
:: Opcao 4: Testar Inicializacao Automatica (--autostart)
:: -------------------------------------------------------------
:RUN_AUTO
echo.
echo ========================================================================
echo        TESTANDO INICIALIZACAO E FUNCOES AUTOMATICAS (--autostart)
echo ========================================================================
echo Compilando e iniciando em modo de inicializacao automatica...
"%DOTNET_EXE%" build LuminiTray.csproj -c Debug --nologo -v m
if errorlevel 1 (
    echo [ERRO] Falha na compilacao.
    pause
    if "%~1"=="" goto MENU
    exit /b 1
)

set "EXE_PATH=%~dp0bin\Debug\net8.0-windows\LuminiTray.exe"
set "EXE_DIR=%~dp0bin\Debug\net8.0-windows"
net session >nul 2>&1
if %errorlevel% equ 0 (
    start "" /D "%EXE_DIR%" "%EXE_PATH%" --autostart
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%EXE_PATH%' -WorkingDirectory '%EXE_DIR%' -ArgumentList '--autostart' -Verb RunAs" 2>nul || start "" /D "%EXE_DIR%" "%EXE_PATH%" --autostart
)

echo.
echo [INFO] O LUMINI TRAY foi iniciado em segundo plano com --autostart.
echo Verifique a notificacao no canto inferior direito da tela.
echo.
if "%~1"=="" (
    pause
    goto MENU
)
exit /b 0

:: -------------------------------------------------------------
:: Opcao 5: Apenas Compilar
:: -------------------------------------------------------------
:RUN_BUILD
echo.
echo ========================================================================
echo                  COMPILANDO O PROJETO (dotnet build)...
echo ========================================================================
echo.
"%DOTNET_EXE%" build LuminiTray.csproj
if errorlevel 1 (
    echo [ERRO] Compilacao falhou.
) else (
    echo [SUCESSO] Projeto compilado com exito!
)
echo.
if "%~1"=="" (
    pause
    goto MENU
)
exit /b %errorlevel%

:: -------------------------------------------------------------
:: Opcao 6: Publicar para Distribuicao
:: -------------------------------------------------------------
:RUN_PUBLISH
call publicar.bat
if "%~1"=="" goto MENU
exit /b %errorlevel%

:EXIT_SCRIPT
exit /b 0
