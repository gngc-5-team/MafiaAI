# AI 마피아 🕵️

6인 마피아 게임을 5명의 로컬 AI(**Gemma 4 12B**)와 함께 플레이하는 심리전 게임.
당신은 저택의 방을 돌아다니며 AI들과 대화해 그 속에 숨은 마피아 AI를 색출한다.

기획 상세: [`planning/AI마피아_기획서_v2.docx`](planning/AI마피아_기획서_v2.docx)

---

## ⚡ 빠른 시작 (Clone 후 3단계)

### 1. 저장소 받기
```bash
git clone <이-저장소-URL>
cd MafiaAI
```

### 2. 로컬 AI 자동 세팅 (Ollama + gemma4:12b)

> ⚠️ AI 모델 가중치(약 7.6GB)는 저장소에 포함되지 않습니다(용량 문제).
> 아래 스크립트가 Ollama 설치와 모델 다운로드를 **자동으로** 처리합니다.

**macOS / Linux**
```bash
bash setup.sh
```

**Windows (PowerShell)**
```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force ; ./setup.ps1
```

스크립트가 하는 일:
1. Ollama 설치 여부 확인 → 없으면 설치(brew / winget / 공식 스크립트)
2. Ollama 서버 실행
3. `gemma4:12b` 모델 다운로드(약 7.6GB) + 워밍업

### 3. Unity에서 실행
1. **Unity Hub**에서 **Unity 6000.3.19f1** 설치 (동일 버전 권장)
2. 이 프로젝트 폴더를 Unity로 열기 (첫 실행 시 패키지 임포트로 수 분 소요)
3. `Assets/Scenes/YuminScene.unity` 열기 → **▶ Play**

끝. 워밍업된 로컬 AI가 대화를 시작합니다.

---

## 🎮 조작

| 입력 | 동작 |
|---|---|
| `WASD` / 방향키 | 저택 안 이동 (같은 방 사람의 말만 들림) |
| `Enter` | 채팅 입력창 포커스 → 입력 후 Enter로 발언. 이름을 넣으면 그 AI가 우선 반응 |
| `추궁` 버튼 | 같은 방의 특정 인물을 지목해 즉시 반박 유도 |
| `F` | (낮) 같은 방 인물에게 접근해 **1:1 심문** — 단둘의 비공개 대화, 압박하면 빈틈이 샌다 |
| `Space` | (밤·능력자) 대상에게 접근해 능력 사용 — 마피아 살해 / 경찰 조사(즉시 결과) / 의사 보호 |
| 투표 오버레이 | 처형 대상 클릭 |
| `Tab` | 대화 로그 패널 토글 |

낮 토론 60초 → 투표 → 밤 15초 사이클. 밤에는 시야가 좁아지고(역할별 차등), 대상 머리 위 화살표가 능력 사거리를 알려준다.
죽은 사람의 역할은 공개되지 않는다 — 마피아가 몇 명 줄었는지는 스스로 추리해야 한다.

---

## ✅ 사전 요구사항 요약

| 항목 | 버전/비고 |
|---|---|
| Unity | 6000.3.19f1 (URP · 2D · Input System) |
| Ollama | 최신 (setup 스크립트가 설치) |
| 모델 | `gemma4:12b` (Q4, 약 7.6GB, setup 스크립트가 pull) |
| 디스크 | 모델용 약 10GB 여유 |
| RAM | 16GB+ 권장 (12B Q4 구동, 32GB면 쾌적) |

> 저사양 머신은 `Assets`의 GameController 인스펙터에서 `Config > Model`을 `gemma4:latest`(8B)나 `gemma3:4b`로 바꿔도 동작합니다(추리 품질은 낮아짐).

---

## 🔧 문제 해결

- **"Ollama 연결 실패"** → 터미널에서 `ollama serve` 가 떠 있는지, `curl http://localhost:11434/api/tags` 응답 확인.
- **모델이 없다고 나옴** → `ollama pull gemma4:12b` 수동 실행.
- **AI 발언이 전부 빈 말/얼버무림** → gemma4는 thinking 모델이라 `think:false` 없이 호출하면 빈 응답이 옵니다. 이 프로젝트의 `OllamaClient`가 자동 처리하므로, 커스텀 호출을 붙일 때만 주의.
- **첫 발언이 느림** → 첫 호출은 모델 로딩으로 2~3초. setup 스크립트의 워밍업으로 완화됨.
- **판 중간에 갑자기 수십 초 멈춤** → 모델이 메모리에서 내려간 경우. 게임은 `keep_alive 30m`을 보내므로 정상 플레이에선 드묾.
- **Unity 버전 경고** → Unity Hub에서 6000.3.19f1 설치 후 그 버전으로 열기.

---

## 📦 빌드 / 배포 (심사위원 제출용 — 사전 설치 0)

빌드에는 Ollama와 모델이 **통째로 동봉**되어, 받는 사람은 압축 풀고 실행만 하면 된다.
게임이 시작될 때 `OllamaBootstrap`이 동봉 AI 엔진을 자동 기동한다(개발 머신에선 기존 Ollama를 재사용).

1. **동봉물 패키징** (빌드 전 1회, 로컬에 `gemma4:12b`가 pull된 상태에서):
   ```bash
   bash tools/package_llm.sh
   ```
   → `Assets/StreamingAssets/ollama/{mac,win,models}` 생성 (약 8GB, git 제외됨)
2. **Unity 빌드**: File→Build Settings에서 macOS / Windows 각각 빌드. StreamingAssets가 자동 포함된다.
3. **배포**:
   - **Windows**: 빌드 폴더째 zip → 받는 쪽은 풀고 `MafiaAI.exe` 더블클릭. 끝.
   - **macOS**: `ditto -c -k --keepParent MafiaAI.app MafiaAI-mac.zip` 으로 압축(실행 권한 보존).
     서명이 없으므로 첫 실행만 **앱 우클릭 → 열기** 안내 한 줄 필요(맥의 Gatekeeper, 유일한 예외).
4. 첫 실행 시 AI 엔진 기동+모델 로딩으로 10~30초 걸릴 수 있다(이후 즉시).

> 요구 사양(받는 쪽): RAM 16GB+ (12B 모델), 디스크 10GB. 인터넷 불필요(완전 오프라인 동작).

## 📁 구조

```
Assets/MafiaAI/Scripts/
  Core/   순수 C# 규칙 엔진(역할·상태머신·승패) — UnityEngine 비의존
  LLM/    Ollama 호출 · 프롬프트 조립 · AI/인간 좌석 · 게임 진행
  UI/     취조 UI · 저택 타일맵/조명 · 밤 공간 능력 · 1:1 심문
planning/ 기획서 · 작업 핸드오프(CLAUDE_CODE_HANDOFF.md)
setup.sh / setup.ps1  로컬 AI 자동 세팅
```
