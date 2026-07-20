@echo off
setlocal
set "ENGINE=%~dp0.codex-dream-skin"
set "SCREENSHOT=%USERPROFILE%\Desktop\Coral-Haze-Dream-Skin-Verification.png"
call "%ENGINE%\scripts\dream-skin-powershell.cmd" -File "%ENGINE%\scripts\verify-dream-skin.ps1" -ScreenshotPath "%SCREENSHOT%"
if errorlevel 1 (
  echo.
  echo Verification failed. Review the message above, then press any key.
  pause > nul
  exit /b 1
)
echo Verification screenshot: %SCREENSHOT%
pause
exit /b 0
