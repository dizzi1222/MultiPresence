@echo off
REM Lanzador para set-appid.ps1. La politica de ejecucion de PowerShell de Windows bloquea
REM los .ps1 por defecto, asi que este .cmd los ejecuta con Bypass.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0set-appid.ps1" %*
pause