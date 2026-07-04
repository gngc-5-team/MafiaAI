# AI 마피아 🕵️

6인 마피아 게임을 5명의 로컬 AI(Gemma 4 8B Q4)와 함께 플레이하는 텍스트 심리전 게임.
당신은 저택의 방을 돌아다니며 AI들과 대화해 그 속에 숨은 마피아 AI를 색출한다.

기획 상세: [`planning/AI마피아_기획서_v2.docx`](planning/AI마피아_기획서_v2.docx)

---

## ⚡ 빠른 시작 (Clone 후 3단계)

### 1. 저장소 받기
```bash
git clone <이-저장소-URL>
cd MafiaAI
```

### 2. 로컬 AI 자동 세팅 (Ollama + gemma4:latest)

> ⚠️ AI 모델 가중치(약 9.6GB)는 저장소에 포함되지 않습니다(용량 문제).
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
3. `gemma4:latest` 모델 다운로드(약 9.6GB) + 워밍업

### 3. Unity에서 실행
1. **Unity Hub**에서 **Unity 6000.3.19f1** 설치 (동일 버전 권장)
2. 이 프로젝트 폴더를 Unity로 열기 (첫 실행 시 패키지 임포트로 수 분 소요)
3. `Assets/Scenes/SampleScene.unity` 열기 → **▶ Play**

끝. 워밍업된 로컬 AI가 대화를 시작합니다.

---

## 🎮 조작

| 입력 | 동작 |
|---|---|
| `WASD` / 방향키 | 저택 안 이동 (같은 방 사람의 말만 들림) |
| `Enter` | 채팅 입력창 포커스 |
| 채팅 입력 후 Enter | 발언. 메시지에 특정 인물 이름을 넣으면 그 AI가 우선 반응 |
| `추궁` 버튼 | 같은 방의 특정 인물을 지목해 즉시 반박 유도 |
| `Tab` | 대화 로그 패널 토글 |
| 밤/투표 오버레이 | 능력자면 대상 클릭(밤 10초 제한), 투표 대상 클릭 |

낮 토론 60초 → 투표 → 밤 10초 사이클. 상단 중앙에 낮/밤 카운트다운 타이머 표시.

---

## ✅ 사전 요구사항 요약

| 항목 | 버전/비고 |
|---|---|
| Unity | 6000.3.19f1 (URP · 2D · Input System) |
| Ollama | 최신 (setup 스크립트가 설치) |
| 모델 | `gemma4:latest` (현재 설치 태그 기준 8B Q4, 약 9.6GB, setup 스크립트가 pull) |
| 디스크 | 모델용 약 12GB 여유 |
| RAM | 16GB+ 권장 (8B Q4 모델 구동) |

---

## 🔧 문제 해결

- **"Ollama 연결 실패"** → 터미널에서 `ollama serve` 가 떠 있는지, `curl http://localhost:11434/api/tags` 응답 확인.
- **모델이 없다고 나옴** → `ollama pull gemma4:latest` 수동 실행.
- **첫 발언이 느림** → 첫 호출은 모델 로딩으로 2~3초. setup 스크립트의 워밍업으로 완화됨.
- **Unity 버전 경고** → Unity Hub에서 6000.3.19f1 설치 후 그 버전으로 열기.

---

## 📁 구조

```
Assets/MafiaAI/Scripts/
  Core/   순수 C# 규칙 엔진(역할·상태머신·승패) — UnityEngine 비의존
  LLM/    Ollama 호출 · 프롬프트 조립 · AI/인간 좌석 · 게임 진행
  UI/     다크 누아르 취조 UI · 저택 스프라이트 이동
planning/ 기획서
setup.sh / setup.ps1  로컬 AI 자동 세팅
```
