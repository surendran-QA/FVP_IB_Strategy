@echo off
title 0_STOP_ALL_SERVERS
echo ==========================================
echo Shutting down all AI Engine Servers...
echo ==========================================

:: Kills any window with these specific titles
taskkill /F /FI "WINDOWTITLE eq LiteLLM Proxy*" /T >nul 2>&1
taskkill /F /FI "WINDOWTITLE eq Cognee Engine*" /T >nul 2>&1

:: Also explicitly kill the python processes launched by these bats if they detached
taskkill /F /IM litellm.exe >nul 2>&1

echo All servers successfully shut down!
timeout /t 3 /nobreak >nul
