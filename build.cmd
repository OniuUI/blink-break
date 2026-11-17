@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (echo Could not find csc.exe from .NET Framework 4.x & exit /b 1)
if not exist bin mkdir bin
set "OPTS=/nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll"

rem Pass 1: build once so the program can draw its own .ico file
"%CSC%" %OPTS% /out:bin\BlinkBreak.exe src\BlinkBreak.cs || exit /b 1
start "" /wait bin\BlinkBreak.exe --export-icon bin\BlinkBreak.ico || exit /b 1

rem Pass 2: rebuild with the icon embedded
"%CSC%" %OPTS% /win32icon:bin\BlinkBreak.ico /out:bin\BlinkBreak.exe src\BlinkBreak.cs || exit /b 1
echo Built bin\BlinkBreak.exe
