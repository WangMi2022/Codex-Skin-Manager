@echo off
setlocal
cd /d "%~dp0\..\.."

node "%~dp0generate-image2.mjs" ^
  --prompt-file "%~dp0\..\image2-prompts\romantic-rose-wallpaper-relay.txt" ^
  --output "output\imagegen\romantic-rose-image2.png" ^
  --size 2560x1440 ^
  --request-size 1536x1024 ^
  --quality low ^
  --stream ^
  --focus-x 0.68 ^
  --focus-y 0.45 ^
  %*

if errorlevel 1 (
  echo.
  echo Image generation failed. Review the message above.
  pause
  exit /b 1
)

echo.
echo Image generated successfully.
pause
