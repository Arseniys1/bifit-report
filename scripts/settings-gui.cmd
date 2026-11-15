@echo off
rem BifitReport settings GUI. The PowerShell script requests elevation (UAC).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0settings-gui.ps1"
