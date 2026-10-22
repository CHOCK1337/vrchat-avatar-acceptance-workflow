@echo off
setlocal
"%~dp0runtime\node\node.exe" "%~dp0tools\doctor.mjs" %*
set "result=%errorlevel%"
if "%~1"=="" pause
exit /b %result%
