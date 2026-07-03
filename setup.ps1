# AI 마피아 - 로컬 AI(Ollama + gemma3:4b) 자동 세팅 (Windows PowerShell)
# 사용법(PowerShell):
#   Set-ExecutionPolicy -Scope Process Bypass -Force ; ./setup.ps1

$ErrorActionPreference = "Stop"
$MODEL = "gemma3:4b"

Write-Host "=============================================="
Write-Host "  AI 마피아 - 로컬 AI 환경 세팅"
Write-Host "=============================================="

# 1) Ollama 설치 확인
if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
    Write-Host "[1/3] Ollama가 없습니다. 설치를 시도합니다..."
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        winget install -e --id Ollama.Ollama --accept-source-agreements --accept-package-agreements
        Write-Host "  ! 설치 후 새 PowerShell 창에서 setup.ps1 을 다시 실행하세요."
        exit 0
    } else {
        Write-Host "  ! winget이 없습니다. https://ollama.com/download 에서 직접 설치 후 다시 실행하세요."
        exit 1
    }
} else {
    Write-Host "[1/3] Ollama 설치 확인됨"
}

# 2) Ollama 서버 실행 확인
$serverUp = $false
try {
    Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -TimeoutSec 2 -UseBasicParsing | Out-Null
    $serverUp = $true
} catch { $serverUp = $false }

if (-not $serverUp) {
    Write-Host "[2/3] Ollama 서버를 시작합니다..."
    Start-Process -WindowStyle Hidden -FilePath "ollama" -ArgumentList "serve"
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 1
        try {
            Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -TimeoutSec 2 -UseBasicParsing | Out-Null
            break
        } catch {}
    }
} else {
    Write-Host "[2/3] Ollama 서버 실행 중"
}

# 3) 모델 다운로드 + 워밍업
$have = (ollama list | Select-String $MODEL)
if ($have) {
    Write-Host "[3/3] 모델 $MODEL 이미 있음"
} else {
    Write-Host "[3/3] 모델 $MODEL 다운로드 (약 3.3GB, 수 분 소요)..."
    ollama pull $MODEL
}

Write-Host "  워밍업 중..."
try { ollama run $MODEL "준비됐나? 한 단어로만." | Out-Null } catch {}

Write-Host ""
Write-Host "완료! 이제 Unity에서 SampleScene을 열고 Play 하세요."
Write-Host "필요 Unity 버전: 6000.3.19f1 (Unity Hub에서 동일 버전 설치 권장)"
