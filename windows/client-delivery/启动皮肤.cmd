@echo off
setlocal
set "ENGINE=%~dp0.codex-dream-skin"
call "%ENGINE%\scripts\dream-skin-powershell.cmd" -File "%ENGINE%\scripts\start-dream-skin.ps1" -PromptRestart
if errorlevel 1 (
  echo.
  echo Launch failed. Review the message above, then press any key.
  pause > nul
  exit /b 1
)
exit /b 0
