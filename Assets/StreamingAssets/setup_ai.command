#!/bin/bash
# AI 마피아 - AI 리소스 설치 (macOS)
echo "=============================================="
echo "  AI 마피아 - AI 리소스 설치"
echo "  (Ollama + gemma4:e4b-it-qat, 약 6.1GB)"
echo "=============================================="

# 1) Ollama 설치 확인
if ! command -v ollama >/dev/null 2>&1; then
  if command -v brew >/dev/null 2>&1; then
    echo "[1/2] Homebrew로 Ollama 설치 중..."
    brew install ollama
  else
    echo "[1/2] Ollama 앱을 내려받습니다..."
    curl -L -o /tmp/Ollama-darwin.zip https://ollama.com/download/Ollama-darwin.zip
    unzip -oq /tmp/Ollama-darwin.zip -d /Applications
    open -a Ollama
    echo "    메뉴바에 Ollama 아이콘이 뜰 때까지 잠시 기다립니다..."
    sleep 8
  fi
fi

# 서버 기동 보장
if ! curl -s --max-time 2 http://localhost:11434/api/tags >/dev/null 2>&1; then
  (ollama serve >/dev/null 2>&1 &) || open -a Ollama || true
  for i in $(seq 1 15); do
    curl -s --max-time 2 http://localhost:11434/api/tags >/dev/null 2>&1 && break
    sleep 1
  done
fi

# 2) 모델 다운로드
echo "[2/2] AI 모델을 내려받습니다 (약 6.1GB)..."
ollama pull gemma4:e4b-it-qat || { echo "! 모델 다운로드 실패 — 인터넷 확인 후 재실행"; read -p "Enter를 누르면 닫힙니다"; exit 1; }

echo ""
echo "완료! 게임으로 돌아가 [다시 확인]을 누르세요."
read -p "Enter를 누르면 닫힙니다"
