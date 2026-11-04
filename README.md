# 🎓 교육장 PC 원클릭 & 선택 설치 올인원 솔루션 (EduPCSetup v3.0)

본 솔루션은 교육장, 실습실, 부트캠프 PC(Windows 10/11 64-bit)를 위해 제작된 **단일 실행 파일(`.exe`) 기반의 올인원 개발 환경 구축 도구**입니다.
복잡한 의존성 없이 **단 1개의 파일(`EduPCSetup.exe`)**만 복사하여 실행하면, 대화형 메뉴를 통해 **Windows 4대 개발 도구(Python, VS Code, Docker, WSL)**뿐만 아니라 **WSL 리눅스 내부 핵심 개발 환경(build-essential, git, venv, ruff, ~/projects)과 Docker 권한까지 한 번에 자동 세팅**합니다.

---

## 🌟 핵심 특징 (v3.0 업그레이드)

1. **단 1개의 단일 실행 파일 (Single Executable, 28KB)**:
   * 별도 Python, Rust, 추가 런타임 없이 Windows 10/11 내장 엔진으로 즉시 구동.
2. **하이브리드 패키지 매니저 엔진 (Winget + Chocolatey + Direct)**:
   * **Winget** 및 **Chocolatey** 공식 패키지 ID로 안정적인 무인 설치.
   * 패키지 매니저가 없거나 오프라인 환경인 경우 공식 인스톨러로 자동 폴백.
3. **WSL 내부 리눅스 개발 환경 원클릭 프로비저닝 (Part A)**:
   * Windows 앱만 깔고 끝나는 것이 아니라, Ubuntu 내부에 `build-essential`, `curl`, `wget`, `git`, `python3-pip`, `python3-venv`, `gh`, `ruff`를 자동 설치.
   * 리눅스 홈 경로에 표준 실습 폴더인 **`~/projects`** 자동 생성 및 권한 설정.
4. **Docker `docker.sock` 권한 에러 원천 차단 (Part B)**:
   * WSL 내부에서 `sudo usermod -aG docker $USER`를 자동 적용하여, 학생들이 `permission denied … docker.sock` 에러 없이 `docker run`을 즉시 실행 가능.
5. **VS Code 필수 개발 확장팩 일괄 설치 (Part B)**:
   * `ms-vscode-remote.remote-wsl` (WSL 원격 연동 필수 다리)
   * `ms-python.python` (Python 인터프리터 & 가상환경 연동)
   * `eamodio.gitlens` (Git 라인별 커밋 히스토리)
   * `github.vscode-pull-request-github` (GitHub PR 연동)
   * `MS-CEINTL.vscode-language-pack-ko` (한국어 언어팩 + 한글 로케일)
6. **WSL 바탕화면 꺼짐 방지 대화형 셸 (Keep-Alive Shell)**:
   * 바탕화면 바로가기 클릭 시 즉시 꺼지지 않고 리눅스 홈(`~`)에서 인터랙티브 Bash가 기동되는 `cmd.exe /k` 래퍼 적용.
7. **터미널 & TUI 파워 도구 팩 지원 (Part C - 선택)**:
   * `btop` (화려한 실시간 리소스 모니터), `tmux` (터미널 분할), `ripgrep(rg)`, `fzf`, `zoxide(z)`, `tealdeer(tldr)` 원클릭 설치 지원.

---

## 📋 메뉴 구성 안내 (`EduPCSetup.exe`)

```text
================================================================================
   [교육장 PC 환경 구축 올인원 솔루션 - EduPCSetup v3.0 (Winget/Choco/WSL)]     
================================================================================
 * 패키지 엔진 : Winget [감지] | Chocolatey [감지] | 공식 직다운로드 [준비됨]
 * 랩 PC 주의사항: 실습실 PC는 공용이므로 퇴실 전 반드시 GitHub에 'git push' 하세요!
--------------------------------------------------------------------------------
 [1] 전체 완전 자동 설치 (Windows 4대 앱 + WSL 리눅스 프로비저닝 + 확장팩) [추천]
 [2] Python (최신 3.12 64-bit, 전역 PATH 최상단 등록, Store stub 차단, IDLE)
 [3] Visual Studio Code (WSL 원격확장 + Python + GitLens + 한국어팩 일괄)
 [4] Docker Desktop (최신 버전 + WSL docker 권한 자동 부여: usermod -aG)
 [5] WSL2 & Ubuntu 24.04 (가상화 기능 + 배포판 등록 + 꺼짐방지 대화형 셸)
 [6] WSL 내부 리눅스 개발 도구 프로비저닝 (build-essential, git, venv, ruff)
 [7] 터미널 파워 도구 팩 설치 (btop, tmux, ripgrep, fzf, zoxide, tldr)
 [8] 다중 선택 설치 (예: 2,3,6 또는 4,5,6 처럼 원하는 항목만 콤마로 선택)
 [9] 환경변수(PATH) 및 바탕화면 바로가기만 복구/점검 (설치 없이 설정만)
 [10] 현재 시스템 설치 상태 진단 및 검증 (Verify - 정밀 감지)
 [11] Chocolatey(choco) 패키지 매니저 자동 설치 (PC에 없을 때)
 [0] 프로그램 종료
================================================================================
```

---

## 🚀 빠른 시작 방법

1. **[`EduPCSetup.exe`](file:///C:/Users/developer3025/.gemini/antigravity/scratch/edu-pc-setup/EduPCSetup.exe)** 파일 1개만 대상 PC나 USB에 복사합니다.
2. 더블 클릭하여 실행합니다. (관리자 권한 승격 UAC 승인)
3. **`1`**을 누르고 엔터를 치면 전체 환경(Windows 도구 + WSL 내부 패키지 + VS Code 확장팩 + Docker 권한 + 바로가기)이 한 번에 완벽하게 구축됩니다!

---

## 📂 파일 구성 안내

| 파일 / 폴더 | 형식 | 설명 |
| :--- | :--- | :--- |
| **[`EduPCSetup.exe`](file:///C:/Users/developer3025/.gemini/antigravity/scratch/edu-pc-setup/EduPCSetup.exe)** | **단일 실행 파일 (PE)** | **v3.0 핵심 산출물 (배포용 독립 실행 파일)** |
| [`EduPCSetup.cs`](file:///C:/Users/developer3025/.gemini/antigravity/scratch/edu-pc-setup/EduPCSetup.cs) | C# 소스 코드 | `EduPCSetup.exe`의 전체 원본 소스 코드 |
| [`rust-src/`](file:///C:/Users/developer3025/.gemini/antigravity/scratch/edu-pc-setup/rust-src) | Rust 프로젝트 | `cargo build --release`로 빌드 가능한 Rust 원본 프로젝트 |
| [`setup.ps1`](file:///C:/Users/developer3025/.gemini/antigravity/scratch/edu-pc-setup/setup.ps1) | PowerShell 스크립트 | 스크립트 기반 자동화 엔진 |
| [`verify_env.bat`](file:///C:/Users/developer3025/.gemini/antigravity/scratch/edu-pc-setup/verify_env.bat) | 검증 도구 | 새 CMD 세션에서 명령어 동작 여부 원클릭 테스트 |
