@echo off
cd /d "%~dp0"
"app\.venv\Scripts\python.exe" start_studio.py
if errorlevel 1 pause
