@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-adb.ps1" %*
