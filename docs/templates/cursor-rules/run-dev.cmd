@echo off
REM AgentFlow code generation: implements from the approved PRD.
REM AgentFlow sets: WORK_ITEM_JSON_PATH, REPO_PATH, GUIDANCE_DIR, PRD_PATH, GIT_ALLOWLIST, GIT_ALLOWLIST_DESCRIPTION.
setlocal
if "%WORK_ITEM_JSON_PATH%"=="" exit /b 1
if not exist "%WORK_ITEM_JSON_PATH%" exit /b 1
if "%REPO_PATH%"=="" exit /b 1
if not exist "%REPO_PATH%" exit /b 1
if "%PRD_PATH%"=="" exit /b 1
if not exist "%PRD_PATH%" exit /b 1
cd /d "%REPO_PATH%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-dev.ps1"
exit /b %ERRORLEVEL%
