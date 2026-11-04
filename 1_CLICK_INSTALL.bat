@echo off
:: ==============================================================================
:: 교육장 PC 원클릭 자동 설치 런처 (WSL, Docker Desktop, Python, VS Code)
:: ==============================================================================
title 교육장 PC 자동 환경 구축 솔루션

:: UTF-8 코드페이지 설정
chcp 65001 >nul

:: 관리자 권한 확인
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [안내] 관리자 권한이 필요합니다. 관리자 권한 승격 창(UAC)을 호출합니다...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

:: 스크립트 실행 경로로 이동
cd /d "%~dp0"

echo ==============================================================================
echo                [교육장 PC 원클릭 환경 구축 프로그램]
echo ==============================================================================
echo.
echo 설치 항목:
echo   1. WSL2 및 Ubuntu 24.04 LTS
echo   2. Python 3.12 (전체 사용자, 전역 PATH 자동 등록, Store Alias 충돌 방지)
echo   3. Visual Studio Code (한국어 언어팩 기본 적용)
echo   4. Docker Desktop (WSL2 백엔드 연동)
echo   5. 바탕화면 바로가기 아이콘 생성 및 시스템 전역 PATH 등록
echo.
echo ==============================================================================
echo.

:: PowerShell 자동화 엔진 호출
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"

echo.
echo ==============================================================================
echo 작업이 완료되었습니다. 창을 닫으려면 아무 키나 누르세요.
echo ==============================================================================
pause >nul
