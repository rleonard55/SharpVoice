@echo off
setlocal
cd /d "%~dp0"
if exist "artifacts\aot-update\update.ready" (
  "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "if (Get-Process -Name SharpVoice -ErrorAction SilentlyContinue) { exit 1 } else { exit 0 }"
  if errorlevel 1 (
    echo Exit SharpVoice from its tray menu, then run this launcher again to install the update.
    pause
    exit /b 1
  )
  robocopy "artifacts\aot-update" "artifacts\aot" /E /XF update.ready /NFL /NDL /NJH /NJS >nul
  if errorlevel 8 (
    echo Could not install the update. Close SharpVoice and try again.
    pause
    exit /b 1
  )
  del "artifacts\aot-update\update.ready"
)
if exist "artifacts\aot\SharpVoice.exe" (
  start "" "artifacts\aot\SharpVoice.exe"
  exit /b 0
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 10 SDK, then run this launcher again.
  pause
  exit /b 1
)
dotnet run --project src\SharpVoice.App\SharpVoice.App.csproj -c Release -p:PublishAot=false
if errorlevel 1 pause
