@echo off
setlocal
"%~dp0runtime\node\node.exe" "%~dp0tools\doctor.mjs" %*
set "awExit=%ERRORLEVEL%"
echo.
pause
exit /b %awExit%
