@echo off
setlocal EnableExtensions DisableDelayedExpansion

cd /d "%~dp0"
if errorlevel 1 (
    echo [ERROR] Could not open the repository folder.
    goto :fail
)

if not exist "%~dp0pull_build_run.bat" (
    echo [ERROR] pull_build_run.bat was not found.
    goto :fail
)

call "%~dp0pull_build_run.bat"
if errorlevel 1 goto :fail

echo [PASS] Git_Pull completed.
exit /b 0

:fail
echo [FAIL] Git_Pull stopped. Review the error shown above.
pause
exit /b 1
