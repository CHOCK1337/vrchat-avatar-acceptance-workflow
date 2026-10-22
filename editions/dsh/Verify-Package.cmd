@echo off
setlocal
"%~dp0runtime\python\python.exe" "%~dp0tools\verify_package.py" %*
set "awExit=%ERRORLEVEL%"
echo.
pause
exit /b %awExit%
