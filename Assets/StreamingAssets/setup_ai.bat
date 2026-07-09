@echo off
chcp 65001 >nul
title AI Mafia - AI Setup

set "LOG=%~dp0setup_debug.log"
echo ===== %date% %time% ===== > "%LOG%"

echo ==============================================
echo   AI Mafia - AI Resource Setup
echo   Installing Ollama + gemma4:e4b-it-qat (about 6.1GB)
echo ==============================================
echo.

set "OLLAMA_PATH=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"

if exist "%OLLAMA_PATH%" (
  set "OLLAMA=%OLLAMA_PATH%"
  echo [DEBUG] found local install: %OLLAMA% >> "%LOG%"
  goto pull
)

where ollama >nul 2>&1
if %errorlevel%==0 (
  set "OLLAMA=ollama"
  echo [DEBUG] using ollama from PATH >> "%LOG%"
  goto pull
)

echo [1/2] Downloading Ollama installer...
curl -L -o "%TEMP%\OllamaSetup.exe" https://ollama.com/download/OllamaSetup.exe
if not exist "%TEMP%\OllamaSetup.exe" (
  echo.
  echo  ! Download failed. Please check your internet connection and try again.
  pause
  exit /b 1
)

echo     When the installer window opens, click Install and wait for it to finish...
start /wait "" "%TEMP%\OllamaSetup.exe"

if exist "%OLLAMA_PATH%" (
  set "OLLAMA=%OLLAMA_PATH%"
) else (
  where ollama >nul 2>&1
  if %errorlevel% neq 0 (
    echo  ! Could not confirm Ollama installation. Please install it and run this file again.
    pause
    exit /b 1
  )
  set "OLLAMA=ollama"
)

:pull
echo [DEBUG] OLLAMA=%OLLAMA% >> "%LOG%"
echo [DEBUG] running: %OLLAMA% pull gemma4:e4b-it-qat >> "%LOG%"

echo.
echo [2/2] Downloading AI model (about 6.1GB, may take several minutes)...
call "%OLLAMA%" pull gemma4:e4b-it-qat
set "PULLRESULT=%errorlevel%"
echo [DEBUG] pull exit code = %PULLRESULT% >> "%LOG%"

if %PULLRESULT% neq 0 (
  echo.
  echo  ! Model download failed. Please check your internet connection and try again.
  pause
  exit /b 1
)

echo.
echo ==============================================
echo   Done! Go back to the game and click Check Again.
echo ==============================================
pause