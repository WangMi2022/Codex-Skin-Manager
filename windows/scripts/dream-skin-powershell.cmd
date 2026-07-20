@echo off
setlocal
set "DREAM_SKIN_POWERSHELL="

for /f "delims=" %%P in ('where pwsh.exe 2^>nul') do if not defined DREAM_SKIN_POWERSHELL set "DREAM_SKIN_POWERSHELL=%%P"
if not defined DREAM_SKIN_POWERSHELL (
  for /f "delims=" %%P in ('where powershell.exe 2^>nul') do if not defined DREAM_SKIN_POWERSHELL set "DREAM_SKIN_POWERSHELL=%%P"
)

if not defined DREAM_SKIN_POWERSHELL (
  echo PowerShell was not found. Install PowerShell 7 or repair Windows PowerShell.
  exit /b 9009
)

"%DREAM_SKIN_POWERSHELL%" -NoProfile -ExecutionPolicy Bypass %*
set "DREAM_SKIN_EXIT=%ERRORLEVEL%"
endlocal & exit /b %DREAM_SKIN_EXIT%
