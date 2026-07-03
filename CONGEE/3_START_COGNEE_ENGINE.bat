@echo off
cd /d "%~dp0"
title 3_START_COGNEE_ENGINE
echo ==========================================
echo Starting Ninaivaatral Quant AI Engine...
echo ==========================================
.\.venv\Scripts\python.exe server.py
pause
