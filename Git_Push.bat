@echo off
setlocal EnableExtensions DisableDelayedExpansion

cd /d "%~dp0"
if errorlevel 1 (
    echo [ERROR] Could not open the repository folder.
    goto :fail
)

if not exist "%~dp0push_current_branch.bat" (
    echo [ERROR] push_current_branch.bat was not found.
    goto :fail
)

call "%~dp0push_current_branch.bat" %*
if errorlevel 1 goto :fail

echo [PASS] Git_Push completed.
exit /b 0

:fail
echo [FAIL] Git_Push stopped. Review the error shown above.
pause
exit /b 1
