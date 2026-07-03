#!/usr/bin/env bash
# AI 마피아 — 로컬 AI(Ollama + gemma3:4b) 자동 세팅 (macOS / Linux)
# 사용법:  bash setup.sh   또는   ./setup.sh
set -e

MODEL="gemma3:4b"
echo "=============================================="
echo "  AI 마피아 - 로컬 AI 환경 세팅"
echo "=============================================="

# 1) Ollama 설치 확인
if ! command -v ollama >/dev/null 2>&1; then
  echo "[1/3] Ollama가 없습니다. 설치를 시도합니다..."
  if [[ "$OSTYPE" == "darwin"* ]]; then
    if command -v brew >/dev/null 2>&1; then
      brew install ollama
    else
      echo "  ! Homebrew가 없습니다. https://ollama.com/download 에서 직접 설치 후 다시 실행하세요."
      exit 1
    fi
  else
    curl -fsSL https://ollama.com/install.sh | sh
  fi
else
  echo "[1/3] Ollama 설치 확인됨: $(ollama --version 2>/dev/null | head -1)"
fi

# 2) Ollama 서버 실행 확인 (백그라운드)
if ! curl -s --max-time 2 http://localhost:11434/api/tags >/dev/null 2>&1; then
  echo "[2/3] Ollama 서버를 시작합니다..."
  (ollama serve >/dev/null 2>&1 &) || true
  # 최대 15초 대기
  for i in $(seq 1 15); do
    if curl -s --max-time 2 http://localhost:11434/api/tags >/dev/null 2>&1; then break; fi
    sleep 1
  done
else
  echo "[2/3] Ollama 서버 실행 중"
fi

# 3) 모델 다운로드 (약 3.3GB) + 워밍업
if ollama list 2>/dev/null | grep -q "$MODEL"; then
  echo "[3/3] 모델 $MODEL 이미 있음"
else
  echo "[3/3] 모델 $MODEL 다운로드 (약 3.3GB, 네트워크에 따라 수 분 소요)..."
  ollama pull "$MODEL"
fi

echo "  워밍업 중..."
ollama run "$MODEL" "준비됐나? 한 단어로만." >/dev/null 2>&1 || true

echo ""
echo "완료! 이제 Unity에서 SampleScene을 열고 Play 하세요."
echo "필요 Unity 버전: 6000.3.19f1 (Unity Hub에서 동일 버전 설치 권장)"
