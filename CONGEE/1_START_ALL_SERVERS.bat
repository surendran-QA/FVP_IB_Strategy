@echo off
cd /d "%~dp0"
title 1_START_ALL_SERVERS
echo ==========================================
echo Starting LiteLLM Proxy AND Cognee Engine...
echo ==========================================

:: Start the LiteLLM Proxy in a new window
start "LiteLLM Proxy" cmd /c "2_START_LITELLM_PROXY.bat"

:: Wait 3 seconds for proxy to spin up
timeout /t 3 /nobreak >nul

:: Start the Cognee Engine in a new window
start "Cognee Engine" cmd /c "3_START_COGNEE_ENGINE.bat"

echo Both servers have been launched in new windows!
pause
