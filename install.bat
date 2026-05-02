@echo off
chcp 65001 >nul 2>&1
echo ============================================
echo   YUNA-S 설치
echo ============================================
echo.

:: 관리자 권한 확인
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] 관리자 권한이 필요합니다. 관리자 권한으로 다시 실행합니다...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: 현재 폴더 경로
set "APP_DIR=%~dp0"
set "APP_EXE=%APP_DIR%YUNA-S-new.exe"

echo [1/3] Windows Defender 예외 등록 중...
powershell -Command "Add-MpPreference -ExclusionPath '%APP_DIR%'" >nul 2>&1
if %errorlevel% equ 0 (
    echo       폴더 예외 등록 완료: %APP_DIR%
) else (
    echo       [!] 예외 등록 실패. 수동으로 등록해주세요.
)

echo.
echo [2/3] 시작 프로그램 등록 중...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "YUNA-S" /t REG_SZ /d "\"%APP_EXE%\"" /f >nul 2>&1
if %errorlevel% equ 0 (
    echo       시작 프로그램 등록 완료
) else (
    echo       [!] 시작 프로그램 등록 실패
)

echo.
echo [3/3] 설치 완료!
echo.
echo   실행 파일: %APP_EXE%
echo   Defender 예외: %APP_DIR%
echo.
echo   프로그램을 실행하려면 YUNA-S-new.exe를 실행하세요.
echo ============================================
pause
