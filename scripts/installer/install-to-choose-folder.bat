@echo off
REM VigilWatt installer wizard: pick an install folder, then run the official Velopack setup with --installto.
REM NOTE: keep this file pure ASCII (cmd on Chinese Windows runs it with code page 936).
setlocal
set "HERE=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%choose-install-dir.ps1" -Setup "%HERE%VigilWattApp-win-Setup.exe"
if errorlevel 1 (
  echo.
  echo Install wizard failed. You can still install manually:
  echo   "%HERE%VigilWattApp-win-Setup.exe" --installto "D:\VigilWatt"
  echo.
)
endlocal
