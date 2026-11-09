@echo off
setlocal enabledelayedexpansion
title Lumini Tray - Testes Automatizados

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
echo              LUMINI TRAY - TESTES AUTOMATIZADOS
echo ========================================================================
echo.
echo Executando testes unitarios e de integracao...
echo.

"%DOTNET_EXE%" test LuminiTray.Tests\LuminiTray.Tests.csproj --verbosity normal
set TEST_RESULT=%errorlevel%
echo.
if %TEST_RESULT% equ 0 (
    echo ========================================================================
    echo [SUCESSO] Todos os testes automatizados foram aprovados com exito!
    echo ========================================================================
) else (
    echo ========================================================================
    echo [FALHA] Houve falha nos testes - codigo de saida: %TEST_RESULT%
    echo ========================================================================
)
echo.
if not "%NON_INTERACTIVE%"=="1" pause
exit /b %TEST_RESULT%
