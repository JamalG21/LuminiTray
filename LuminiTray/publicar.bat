@echo off
setlocal enabledelayedexpansion
title Publicar LUMINI TRAY (Gerar .EXE)

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
echo                 PUBLICANDO LUMINI TRAY (GERAR .EXE)
echo ========================================================================
echo.
echo Fechando instancias antigas se estiverem abertas...
taskkill /f /im LuminiTray.exe >nul 2>&1
taskkill /f /im LuminiTray.exe >nul 2>&1
taskkill /f /im MicMute.exe >nul 2>&1
if exist "..\LuminiTray-dist\LuminiTray.exe" (
    del /f /q "..\LuminiTray-dist\LuminiTray.exe.old" >nul 2>&1
    ren "..\LuminiTray-dist\LuminiTray.exe" "LuminiTray.exe.old" >nul 2>&1
)

echo Limpando compilacoes anteriores...
if exist "obj\Release" rmdir /s /q obj\Release >nul 2>&1

echo Gerando executavel unico (Single-File Release)...
echo Aguarde, isso pode levar alguns instantes...
echo.

"%DOTNET_EXE%" publish LuminiTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "..\LuminiTray-dist" -nologo -v m
if errorlevel 1 (
    echo.
    echo ========================================================================
    echo [ERRO] Falha ao publicar o projeto. Veja os erros detalhados acima.
    echo ========================================================================
    echo.
    if not "%NON_INTERACTIVE%"=="1" pause
    exit /b 1
)

if exist "LEIA-ME.txt" (
    copy /y "LEIA-ME.txt" "..\LuminiTray-dist\LEIA-ME.txt" >nul
)
if exist "Assets\Drivers\VBCable" (
    xcopy /y /e /i /q "Assets\Drivers\VBCable" "..\LuminiTray-dist\Assets\Drivers\VBCable" >nul
)
if exist "Assets\Plugins\mpv" (
    xcopy /y /e /i /q "Assets\Plugins\mpv" "..\LuminiTray-dist\Assets\Plugins\mpv" >nul
)

echo.
echo ========================================================================
echo [SUCESSO] LuminiTray.exe gerado com sucesso!
echo Pasta de saida: %~dp0..\LuminiTray-dist
echo.
for %%F in ("..\LuminiTray-dist\LuminiTray.exe") do (
    echo Arquivo: %%~nxF
    echo Tamanho: %%~zF bytes
    echo Data/Hora: %%~tF
)
echo ========================================================================
echo.
if not "%NON_INTERACTIVE%"=="1" pause
