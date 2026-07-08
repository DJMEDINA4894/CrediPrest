@echo off
setlocal
set "PATH=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin;%PATH%"
cd /d "%~dp0..\web"
"%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\bin\pnpm.cmd" run dev
