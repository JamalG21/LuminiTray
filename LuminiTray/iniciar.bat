@echo off
setlocal enabledelayedexpansion
title Iniciar Lumini Tray

cd /d "%~dp0"

:: -------------------------------------------------------------
:: Localizar .NET SDK
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

echo ========================================================================
echo                     INICIANDO LUMINI TRAY
echo ========================================================================
echo.

:: 1. Compila o projeto
echo [1/2] Verificando compilacao do projeto...
taskkill /f /im LuminiTray.exe >nul 2>&1
taskkill /f /im LuminiTray.exe >nul 2>&1
taskkill /f /im MicMute.exe >nul 2>&1
"%DOTNET_EXE%" build LuminiTray.csproj -c Debug --nologo -v m
if errorlevel 1 (
    echo.
    echo [ERRO] Falha ao compilar o projeto.
    echo.
    pause
    exit /b 1
)

:: 2. Inicia o executavel com elevacao de Administrador
echo [2/2] Abrindo Lumini Tray...
set "EXE_PATH=%~dp0bin\Debug\net8.0-windows\LuminiTray.exe"
set "EXE_DIR=%~dp0bin\Debug\net8.0-windows"
if not exist "%EXE_PATH%" (
    if exist "%~dp0..\LuminiTray-dist\LuminiTray.exe" (
        set "EXE_PATH=%~dp0..\LuminiTray-dist\LuminiTray.exe"
        set "EXE_DIR=%~dp0..\LuminiTray-dist"
    ) else if exist "%~dp0bin\Debug\net8.0-windows\LuminiTray.exe" (
        set "EXE_PATH=%~dp0bin\Debug\net8.0-windows\LuminiTray.exe"
        set "EXE_DIR=%~dp0bin\Debug\net8.0-windows"
    ) else (
        echo [ERRO] Executavel LuminiTray.exe nao encontrado.
        pause
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
echo [SUCESSO] Lumini Tray iniciado!
timeout /t 2 >nul
exit /b 0
