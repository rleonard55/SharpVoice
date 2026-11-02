@echo off
setlocal
cd /d "%~dp0"
if exist "artifacts\diagnostics\SharpVoice.Diagnostics.exe" (
  start "" "artifacts\diagnostics\SharpVoice.Diagnostics.exe"
  exit /b 0
)
echo Build Wake Diagnostics first using scripts\Build-WakeDiagnostics.ps1.
pause
exit /b 1
