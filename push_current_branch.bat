@echo off
setlocal EnableExtensions DisableDelayedExpansion

cd /d "%~dp0"
if errorlevel 1 goto :fail

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] git.exe was not found in PATH.
    goto :fail
)

for /f "delims=" %%B in ('git branch --show-current') do set "CURRENT_BRANCH=%%B"
if not defined CURRENT_BRANCH (
    echo [ERROR] Push is not allowed from a detached HEAD.
    goto :fail
)

git rev-parse --verify HEAD >nul 2>nul
if errorlevel 1 (
    echo [ERROR] There is no commit to push.
    goto :fail
)

echo Current branch: %CURRENT_BRANCH%
git status --short
echo.
echo [1/1] Push HEAD to origin/%CURRENT_BRANCH%
git push origin "HEAD:%CURRENT_BRANCH%"
if errorlevel 1 goto :fail

echo [PASS] Push completed: origin/%CURRENT_BRANCH%
exit /b 0

:fail
echo [FAIL] Operation stopped.
pause
exit /b 1
