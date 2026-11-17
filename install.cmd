@echo off
setlocal
cd /d "%~dp0"
call "%~dp0build.cmd" || exit /b 1
set "DEST=%LOCALAPPDATA%\Programs\BlinkBreak"
taskkill /im BlinkBreak.exe /f >nul 2>&1
if not exist "%DEST%" mkdir "%DEST%"
copy /y bin\BlinkBreak.exe "%DEST%\BlinkBreak.exe" >nul || exit /b 1
start "" "%DEST%\BlinkBreak.exe"
echo Installed to %DEST% and started. It will also start with Windows.
