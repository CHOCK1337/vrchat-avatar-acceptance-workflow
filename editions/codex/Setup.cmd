@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" %*
set "result=%errorlevel%"
if "%~1"=="" pause
exit /b %result%
