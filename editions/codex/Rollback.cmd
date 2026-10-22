@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Rollback.ps1" %*
set "result=%errorlevel%"
if "%~1"=="" pause
exit /b %result%
