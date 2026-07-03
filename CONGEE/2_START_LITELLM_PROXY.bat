@echo off
cd /d "%~dp0"
title 2_START_LITELLM_PROXY
echo ==========================================
echo Starting LiteLLM Proxy Router...
echo ==========================================
.\.venv\Scripts\litellm.exe --config router_config.yaml
pause
