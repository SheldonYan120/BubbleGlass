@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

echo Compiling ...
"%CSC%" /nologo /target:winexe /optimize+ /out:"BubbleGlass.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "BubbleGlass.cs"

if errorlevel 1 goto failed

echo.
echo  Build OK  -  BubbleGlass.exe
echo.
echo  Double-click it: a bubble icon appears in the tray.
echo  Double-click that icon and iridescent bubbles float over your real
echo  desktop - everything behind them stays visible. Move the mouse to dismiss.
echo.
pause
exit /b 0

:nocsc
echo  .NET Framework 4 compiler not found.
pause
exit /b 1

:failed
echo.
echo  Build FAILED - see errors above.
pause
exit /b 1
