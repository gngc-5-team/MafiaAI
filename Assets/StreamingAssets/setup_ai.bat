@echo off
title AI Mafia - AI Resource Setup

echo ==============================================
echo   AI 마피아 - AI 리소스 설치
echo   (Ollama + gemma4:12b, 약 7.6GB)
echo ==============================================
echo.

set "OLLAMA=ollama"
where ollama >nul 2>&1
if %errorlevel%==0 goto pull

if exist "%LOCALAPPDATA%\Programs\Ollama\ollama.exe" (
  set "OLLAMA=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"
  goto pull
)

echo [1/2] Ollama 설치 파일을 내려받는 중... (약 1GB)
curl -L -o "%TEMP%\OllamaSetup.exe" https://ollama.com/download/OllamaSetup.exe
if not exist "%TEMP%\OllamaSetup.exe" (
  echo.
  echo  !! 다운로드 실패 - 인터넷 연결을 확인하고 다시 실행해 주세요.
  goto done
)
echo     설치 창이 뜨면 Install 을 눌러 주세요. 끝날 때까지 기다립니다...
start /wait "" "%TEMP%\OllamaSetup.exe"

if exist "%LOCALAPPDATA%\Programs\Ollama\ollama.exe" set "OLLAMA=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"

:pull
echo.
echo [2/2] AI 모델 다운로드 중 (7.6GB, 수 분~수십 분)...
echo       아래에 진행률이 표시됩니다. 창을 닫지 마세요.
echo.
"%OLLAMA%" pull gemma4:12b
if %errorlevel% neq 0 (
  echo.
  echo  !! 모델 다운로드 실패 - 인터넷 확인 후 이 창에서 아무 키나 누르고 다시 실행해 주세요.
  goto done
)

echo.
echo ==============================================
echo   완료! 게임으로 돌아가 [다시 확인]을 누르세요.
echo   이 창은 닫아도 됩니다.
echo ==============================================

:done
