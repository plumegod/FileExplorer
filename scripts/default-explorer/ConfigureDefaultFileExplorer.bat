@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%ConfigureDefaultFileExplorer.ps1"

if not exist "%PS_SCRIPT%" (
    echo ConfigureDefaultFileExplorer.ps1 was not found next to this batch file.
    echo Re-extract the complete FileExplorer package and try again.
    echo.
    pause
    exit /b 2
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo The configuration tool exited with code %EXIT_CODE%.
    pause
)

exit /b %EXIT_CODE%
