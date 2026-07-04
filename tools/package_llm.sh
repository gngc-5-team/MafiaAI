#!/usr/bin/env bash
# 빌드 동봉용 LLM 패키징 — Ollama 바이너리(맥/윈) + gemma4:12b 모델을 StreamingAssets에 채운다.
# 사용법:  bash tools/package_llm.sh [ollama버전]   (기본 v0.20.0 — gemma4 최소 요구 버전)
# 결과:   Assets/StreamingAssets/ollama/{mac,win,models}  (총 약 8GB)
# 주의:   이 폴더는 .gitignore 대상. 빌드 직전 각자 로컬에서 실행한다. 모델은 로컬 ~/.ollama 에서 복사하므로
#         먼저 `ollama pull gemma4:12b` 가 되어 있어야 한다(setup.sh 가 이미 해줌).
set -e

VER="${1:-v0.20.0}"
MODEL_NAME="gemma4"
MODEL_TAG="12b"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="$ROOT/Assets/StreamingAssets/ollama"
SRC_MODELS="$HOME/.ollama/models"
MANIFEST="$SRC_MODELS/manifests/registry.ollama.ai/library/$MODEL_NAME/$MODEL_TAG"

echo "=============================================="
echo "  LLM 패키징 → $DEST"
echo "  Ollama $VER + $MODEL_NAME:$MODEL_TAG"
echo "=============================================="

[ -f "$MANIFEST" ] || { echo "! 모델이 없습니다. 먼저: ollama pull $MODEL_NAME:$MODEL_TAG"; exit 1; }

mkdir -p "$DEST/mac" "$DEST/win" "$DEST/models/manifests/registry.ollama.ai/library/$MODEL_NAME" "$DEST/models/blobs"

# 1) 맥 바이너리 (tgz 안에 단일 'ollama' 실행파일)
if [ ! -f "$DEST/mac/ollama" ]; then
  echo "[1/3] macOS 바이너리 다운로드..."
  curl -fL "https://github.com/ollama/ollama/releases/download/$VER/ollama-darwin.tgz" -o /tmp/ollama-darwin.tgz
  tar -xzf /tmp/ollama-darwin.tgz -C "$DEST/mac"
  chmod +x "$DEST/mac/ollama"
else
  echo "[1/3] macOS 바이너리 있음 (스킵)"
fi

# 2) 윈도우 바이너리 (zip 안에 ollama.exe + lib/ 러너들)
if [ ! -f "$DEST/win/ollama.exe" ]; then
  echo "[2/3] Windows 바이너리 다운로드..."
  curl -fL "https://github.com/ollama/ollama/releases/download/$VER/ollama-windows-amd64.zip" -o /tmp/ollama-win.zip
  unzip -oq /tmp/ollama-win.zip -d "$DEST/win"
else
  echo "[2/3] Windows 바이너리 있음 (스킵)"
fi

# 3) 모델: 매니페스트 + 그 매니페스트가 가리키는 블롭만 복사
echo "[3/3] 모델 복사 ($MODEL_NAME:$MODEL_TAG)..."
cp "$MANIFEST" "$DEST/models/manifests/registry.ollama.ai/library/$MODEL_NAME/$MODEL_TAG"
python3 - "$MANIFEST" "$SRC_MODELS/blobs" "$DEST/models/blobs" << 'PY'
import json, shutil, sys, os
manifest, src, dst = sys.argv[1], sys.argv[2], sys.argv[3]
m = json.load(open(manifest))
digests = [m["config"]["digest"]] + [l["digest"] for l in m["layers"]]
total = 0
for d in digests:
    name = d.replace(":", "-")          # sha256:xxx -> sha256-xxx (블롭 파일명 규칙)
    s, t = os.path.join(src, name), os.path.join(dst, name)
    if not os.path.exists(s):
        sys.exit(f"! 블롭 없음: {s}")
    if not (os.path.exists(t) and os.path.getsize(t) == os.path.getsize(s)):
        print(f"  복사: {name[:22]}…  {os.path.getsize(s)/1e9:.2f} GB")
        shutil.copy2(s, t)
    total += os.path.getsize(s)
print(f"  모델 총 {total/1e9:.2f} GB")
PY

echo ""
echo "완료! 이제 Unity에서 macOS/Windows 빌드를 만들면 StreamingAssets/ollama 가 함께 들어갑니다."
du -sh "$DEST"/* 2>/dev/null || true
