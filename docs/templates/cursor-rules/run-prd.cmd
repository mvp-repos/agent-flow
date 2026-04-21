@echo off
REM AgentFlow PRD generation: runs Cursor CLI with /bmad-bmm-quick-spec.
REM AgentFlow sets: WORK_ITEM_JSON_PATH, REPO_PATH, GUIDANCE_DIR.
REM Optional: add prompt-prefix.md in this directory for user instructions (e.g. read these docs, links).
setlocal
if "%WORK_ITEM_JSON_PATH%"=="" exit /b 1
if not exist "%WORK_ITEM_JSON_PATH%" exit /b 1
if "%REPO_PATH%"=="" exit /b 1
if not exist "%REPO_PATH%" exit /b 1
cd /d "%REPO_PATH%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-prd.ps1"
exit /b %ERRORLEVEL%
