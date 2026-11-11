@echo off
rem Unity UO setup for the UO Smallz shard. Double-click this file.
rem It downloads the launcher from the latest release and lets it install the game, the desktop shortcut and,
rem if this PC does not have it yet, the game data.
title Unity UO setup
set "UNITYUO_LAUNCHER=%TEMP%\unityuo-launcher.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = 'Tls12'; try { (New-Object Net.WebClient).DownloadFile('https://github.com/wesleysnoe-dev/UnityUO/releases/latest/download/unityuo-launcher.ps1', $env:UNITYUO_LAUNCHER) } catch { Write-Host ('Could not download the installer: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }"
if errorlevel 1 (
  echo.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%UNITYUO_LAUNCHER%" -Setup
