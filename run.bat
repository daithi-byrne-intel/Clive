@echo off
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
    echo Creating virtual environment...
    py -3 -m venv .venv
    if errorlevel 1 goto :end
)

if exist requirements.txt (
    ".venv\Scripts\python.exe" -m pip install -r requirements.txt
    if errorlevel 1 goto :end
) else if exist pyproject.toml (
    ".venv\Scripts\python.exe" -m pip install .
    if errorlevel 1 goto :end
)

".venv\Scripts\python.exe" main.py %*

:end
pause
