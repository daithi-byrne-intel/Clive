@echo off
rem Double-click to remove Clive for the current user.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
echo.
pause
