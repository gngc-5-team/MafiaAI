@echo off
chcp 65001 >nul
title AI 마피아 - AI 리소스 설치
echo ==============================================
echo   AI 마피아 - AI 리소스 설치
echo   (Ollama + gemma4:12b, 약 7.6GB)
echo ==============================================
echo.

rem ── 1) Ollama 설치 확인 ──
set "OLLAMA=ollama"
where ollama >nul 2>&1
if %errorlevel%==0 goto pull

if exist "%LOCALAPPDATA%\Programs\Ollama\ollama.exe" (
  set "OLLAMA=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
  goto pull
)

echo [1/2] Ollama 설치 파일을 내려받습니다...
curl -L -o "%TEMP%\OllamaSetup.exe" https://ollama.com/download/OllamaSetup.exe
if not exist "%TEMP%\OllamaSetup.exe" (
  echo.
  echo  ! 다운로드 실패. 인터넷 연결을 확인하고 다시 실행해 주세요.
  pause & exit /b 1
)
echo     설치 창이 뜨면 Install 을 눌러 주세요. 설치가 끝날 때까지 기다립니다...
start /wait "" "%TEMP%\OllamaSetup.exe"

if exist "%LOCALAPPDATA%\Programs\Ollama\ollama.exe" (
  set "OLLAMA=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
) else (
  where ollama >nul 2>&1 || (
    echo  ! Ollama 설치를 확인하지 못했습니다. 설치 후 이 파일을 다시 실행해 주세요.
    pause & exit /b 1
  )
)

:pull
rem ── 2) 모델 다운로드 (이미 있으면 즉시 통과) ──
echo.
echo [2/2] AI 모델을 내려받습니다 (약 7.6GB, 네트워크에 따라 수 분~수십 분)...
"%OLLAMA%" pull gemma4:12b
if %errorlevel% neq 0 (
  echo.
  echo  ! 모델 다운로드 실패. 인터넷 연결 확인 후 다시 실행해 주세요.
  pause & exit /b 1
)

echo.
echo ==============================================
echo   완료! 게임으로 돌아가 [다시 확인]을 누르세요.
echo ==============================================
pause
