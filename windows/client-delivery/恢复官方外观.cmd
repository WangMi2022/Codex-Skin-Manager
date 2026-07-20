@echo off
setlocal
set "ENGINE=%~dp0.codex-dream-skin"
call "%ENGINE%\scripts\dream-skin-powershell.cmd" -File "%ENGINE%\scripts\restore-dream-skin.ps1" -RestoreBaseTheme -Uninstall -PromptRestart
if errorlevel 1 (
  echo.
  echo Restore failed. Review the message above, then press any key.
  pause > nul
  exit /b 1
)
exit /b 0
