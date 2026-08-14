@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0export-openapi.ps1" %*
exit /b %ERRORLEVEL%
