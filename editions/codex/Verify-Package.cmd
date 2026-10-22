@echo off
setlocal
"%~dp0runtime\python\python.exe" "%~dp0tools\verify_package.py" --root "%~dp0." %*
set "result=%errorlevel%"
if "%~1"=="" pause
exit /b %result%
