@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" -Apply %*
set "awExit=%ERRORLEVEL%"
echo.
pause
exit /b %awExit%
