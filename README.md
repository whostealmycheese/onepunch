# 🥊 원펀치 (OnePunch) v3.0 - 코딩 교육생을 위한 원클릭 개발 환경 세팅기

본 프로젝트는 부트캠프, 아카데미, 컴퓨터공학 실습실의 **교육생과 강사진을 위해 직접 개발한 초경량 단일 실행기**입니다.

수업 따라가기도 바쁜 첫날부터 Git 설치, 파이썬 환경변수 충돌, WSL 즉시 꺼짐, 도커 소켓 권한 에러(`permission denied`), Active Directory 도메인 계정 권한 문제 등으로 고통받지 않도록, **단 한 번의 실행으로 6대 필수 개발 환경(Git for Windows, Python 3.12, VS Code 한국어팩, Docker Desktop, WSL2 Ubuntu 24.04, Discord)**과 리눅스 내부 실습 환경을 완벽하게 무인 설치 및 연동해 줍니다.

---

## 💻 공식 권장 및 검증 환경

원펀치 v3.0은 최신 고성능 부트캠프 및 실습실 PC 환경에서 100% 사전 검증 및 최적화되었습니다.

* **CPU**: Intel® Core™ Ultra 9 285 / 285K (Arrow Lake, 24 코어 / 24 스레드)
* **GPU**: NVIDIA® GeForce RTX™ 5060 (Blackwell 아키텍처, WSL2 CUDA 가속 및 AI/PyTorch 연동)
* **RAM**: 32GB High-Speed RAM (WSL2 가상머신에 24GB 동적 할당 및 Auto Memory Reclaim 튜닝)
* **OS**: Windows 11 / 10 Enterprise, Pro 64-bit
* **인증 및 계정**: **Windows Active Directory (AD 도메인 연동 환경)**

---

## 🌟 핵심 특징

### 1. 🏢 Active Directory (AD 도메인 계정) 완벽 지원
* **Docker Desktop `docker-users` 자동 매핑**:
  * Windows AD 도메인 계정(`DOMAIN\student`)이 일반 사용자로 로그인해도 Docker Desktop 권한 오류가 발생하지 않도록, `docker-users` 로컬 그룹에 `Authenticated Users(인증된 사용자)` 및 현재 도메인 계정을 자동 등록합니다.
* **새로운 도메인 사용자 자동 한국어화 (`C:\Users\Default`)**:
  * 실습실 PC에 새로운 도메인 학생이 처음 로그인하더라도 `C:\Users\Default` 템플릿에 사전 구성된 VS Code 한국어 설정(`argv.json`)이 자동 복제되어 첫 실행부터 한글로 열립니다.
* **머신 전역 Git 시스템 설정 (`safe.directory *`)**:
  * 공용 실습실에서 여러 학생이 번갈아 프로젝트를 열 때 발생하는 `fatal: detected dubious ownership in repository` 오류를 머신 전역 `safe.directory "*"` 정책으로 원천 차단합니다.
* **공용 바탕화면 (`CommonDesktopDirectory`) 바로가기**:
  * 모든 도메인 계정이 바탕화면에서 Git, VS Code, Docker, Ubuntu 리눅스를 즉시 실행할 수 있도록 공용 바탕화면에 아이콘을 자동 생성합니다.

### 2. ⚡ 인텔 울트라 9 + RTX 5060 + 32GB RAM 하드웨어 극대화
* **WSL2 리소스 최적화 (`.wslconfig`)**:
  * 32GB RAM 중 24GB를 WSL2 및 Docker 컨테이너에 동적 할당하고, 24개 논리 코어를 전면 개방합니다.
  * Windows 11의 `autoMemoryReclaim=gradual` 및 `sparseVhd=true`를 활성화하여 메모리 누수를 방지하고 가상 디스크 용량을 자동 압축합니다.
* **WSL2 NVIDIA CUDA 하드웨어 가속**:
  * RTX 5060 전용 WSL2 Direct3D 및 CUDA 드라이버(`/usr/lib/wsl/lib/libcuda.so.1`)를 점검하고 `ldconfig`를 동기화하여 AI 실습 및 컨테이너 가속을 즉시 활성화합니다.

### 3. 👁️ 눈이 편안한 한글 진행 안내 및 최적 템포 (Human-Friendly Pacing & Korean Progress)
* 설치 중 콘솔 화면이 어지럽게 번쩍이며 빠르게 지나가는 피로감을 없애고, 친절하고 직관적인 한국어 단계별 안내(`[1/7 단계] Git for Windows 설치 중...`)와 편안한 템포로 설치가 진행됩니다.
* 하드웨어(CPU, RAM, GPU)와 윈도우 Active Directory(AD) 도메인 계정 환경을 순수 Win32 API로 100% 실시간 자동 감지합니다.

### 4. 🐧 WSL2 리눅스 기본 계정 자동 프로비저닝 (`developer / 1234`)
* 리눅스 설치 직후 검은 콘솔창에 화면에 글자나 별표(*)가 보이지 않아 학생들이 패닉에 빠지는 UNIX 비밀번호 입력 단계를 자동화하였습니다.
* 기본 사용자 계정(`developer / 1234`) 및 패스워드 없는 `sudo` 권한을 무인 구성하여 즉시 개발에 집중할 수 있습니다.

### 5. 🇰🇷 VS Code 켜자마자 100% 한국어 보장 (불필요한 플러그인 제외)
* 불필요한 외부 플러그인을 배제하고 **공식 한국어 언어팩(`MS-CEINTL.vscode-language-pack-ko`)만 단독 구성**합니다.
* VS Code 구문 오류의 주원인이었던 UTF-8 BOM 문제를 완벽 해결(No-BOM UTF-8 적용)하고, `.vscode/argv.json` 로케일 및 단축아이콘 인자(`--locale=ko`)로 첫 실행부터 오류 창 없이 100% 한국어로 열립니다.

---

## 📋 메뉴 구성 안내 (`OnePunch.exe`)

```text
================================================================================
                            [원펀치 (OnePunch) v3.0]                            
================================================================================
 * 감지 사양: Intel(R) Core(TM) Ultra 9 285K (24 코어) | 32GB RAM | NVIDIA GeForce RTX 5060 / Intel(R) Graphics
 * 계정 환경: GNIA\developer3025 [Active Directory 도메인 연동 환경 - 다중 사용자 모드]
 * 실습실 PC는 공용이므로 퇴실 전 반드시 GitHub에 'git push' 하세요.
--------------------------------------------------------------------------------
 [1] 전체 설치 (차례대로 자동 설치 및 가상화 재부팅 안내) [추천]
 [2] Git for Windows
 [3] Python 3.12
 [4] Visual Studio Code (한국어 언어팩 단독 구성)
 [5] Docker Desktop
 [6] WSL2 & Ubuntu 24.04 LTS
 [7] Discord
 [8] WSL 리눅스 개발 환경 설정
 [9] Chocolatey 패키지 매니저 안전 설치
 [10] 환경변수 및 바로가기 복구
 [11] 설치 상태 검증
 [0] 종료
================================================================================
```

---

## 🚀 빠른 시작 방법

1. **`OnePunch.exe` 다운로드**:
   * 웹 랜딩 페이지나 Releases에서 `OnePunch.exe`를 다운로드합니다.
2. **관리자 권한으로 실행**:
   * 다운로드한 `OnePunch.exe`를 실행합니다 (UAC 승인 창에서 **'예'** 클릭).
3. **`1` 입력 후 엔터**:
   * 메뉴에서 `1`을 입력하고 엔터를 누르면 1단계 설치가 완료되고, PC 재부팅 후 2단계가 자동으로 이어집니다.
