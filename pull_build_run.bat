@echo off
setlocal EnableExtensions DisableDelayedExpansion

cd /d "%~dp0"
if errorlevel 1 goto :fail

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] git.exe was not found in PATH.
    goto :fail
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet.exe was not found in PATH.
    goto :fail
)

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
    echo [ERROR] This BAT file is not inside a Git working tree.
    goto :fail
)

set "STATUS_FILE=%TEMP%\A3_LD_Process_SW_git_status_%RANDOM%_%RANDOM%.txt"
git status --porcelain > "%STATUS_FILE%"
if errorlevel 1 goto :status_fail

for %%A in ("%STATUS_FILE%") do set "STATUS_SIZE=%%~zA"
del /q "%STATUS_FILE%" >nul 2>nul
if not "%STATUS_SIZE%"=="0" (
    echo [ERROR] The working tree contains uncommitted changes.
    echo         Pull was stopped to protect the current work.
    git status --short
    goto :fail
)

for /f "delims=" %%B in ('git branch --show-current') do set "CURRENT_BRANCH=%%B"
if not defined CURRENT_BRANCH (
    echo [ERROR] Pull is not allowed from a detached HEAD.
    goto :fail
)

echo Current branch: %CURRENT_BRANCH%
echo [1/4] Pull origin/%CURRENT_BRANCH% --ff-only
git pull --ff-only origin "%CURRENT_BRANCH%"
if errorlevel 1 goto :fail

echo [2/4] Restore Drilling.sln
dotnet restore Drilling.sln
if errorlevel 1 goto :fail

echo [3/4] Build Drilling.sln Release
dotnet build Drilling.sln -c Release --no-restore
if errorlevel 1 goto :fail

set "APP_PATH=%~dp0Drilling.UI\bin\Release\net8.0-windows\Drilling.UI.exe"
if not exist "%APP_PATH%" (
    echo [ERROR] Application was not produced: %APP_PATH%
    goto :fail
)

echo [4/4] Run Drilling.UI
start "A3 LD Process SW" /d "%~dp0" "%APP_PATH%"
if errorlevel 1 goto :fail

echo [PASS] Pull, restore, build, and run completed.
exit /b 0

:status_fail
del /q "%STATUS_FILE%" >nul 2>nul
echo [ERROR] git status failed.

:fail
echo [FAIL] Operation stopped.
pause
exit /b 1
