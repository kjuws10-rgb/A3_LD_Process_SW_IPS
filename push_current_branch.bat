@echo off
setlocal EnableExtensions DisableDelayedExpansion

cd /d "%~dp0"
if errorlevel 1 goto :fail

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] git.exe was not found in PATH.
    goto :fail
)

git rev-parse --is-inside-work-tree >nul 2>nul
if errorlevel 1 (
    echo [ERROR] This BAT file is not inside a Git working tree.
    goto :fail
)

git remote get-url origin >nul 2>nul
if errorlevel 1 (
    echo [ERROR] The origin remote is not configured.
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

set "REMOTE_BRANCH_EXISTS=0"
git ls-remote --exit-code --heads origin "refs/heads/%CURRENT_BRANCH%" >nul 2>nul
if not errorlevel 1 set "REMOTE_BRANCH_EXISTS=1"

if "%REMOTE_BRANCH_EXISTS%"=="0" (
    echo [1/2] Remote branch does not exist. An upstream will be created.
    goto :push
)

echo [1/2] Fetch origin/%CURRENT_BRANCH%
git fetch --no-tags origin "%CURRENT_BRANCH%"
if errorlevel 1 goto :fail

git merge-base --is-ancestor "origin/%CURRENT_BRANCH%" HEAD
if errorlevel 1 (
    echo [ERROR] Local history is not a fast-forward of origin/%CURRENT_BRANCH%.
    echo         Review and integrate the remote commits before pushing.
    goto :fail
)

set "AHEAD_COUNT="
for /f "delims=" %%C in ('git rev-list --count "origin/%CURRENT_BRANCH%..HEAD"') do set "AHEAD_COUNT=%%C"
if not defined AHEAD_COUNT (
    echo [ERROR] Could not count commits to push.
    goto :fail
)
if "%AHEAD_COUNT%"=="0" (
    echo [INFO] There are no new commits to push to origin/%CURRENT_BRANCH%.
    echo [PASS] Nothing was changed on the remote.
    exit /b 0
)
echo Commits to push: %AHEAD_COUNT%

:push
git rev-parse --abbrev-ref --symbolic-full-name "@{upstream}" >nul 2>nul
if errorlevel 1 (
    echo [2/2] Push and set upstream origin/%CURRENT_BRANCH%
    git push --set-upstream origin "HEAD:%CURRENT_BRANCH%"
) else (
    echo [2/2] Push HEAD to origin/%CURRENT_BRANCH%
    git push origin "HEAD:%CURRENT_BRANCH%"
)
if errorlevel 1 goto :fail

echo [PASS] Push completed: origin/%CURRENT_BRANCH%
exit /b 0

:fail
echo [FAIL] Operation stopped.
pause
exit /b 1
