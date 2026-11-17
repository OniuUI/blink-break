@echo off
taskkill /im BlinkBreak.exe /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v BlinkBreak /f >nul 2>&1
rmdir /s /q "%LOCALAPPDATA%\Programs\BlinkBreak" 2>nul
rmdir /s /q "%APPDATA%\BlinkBreak" 2>nul
echo Blink Break removed.
