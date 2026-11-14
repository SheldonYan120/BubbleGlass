@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

rem Name/scope sanity check. Optional: skipped when python is not installed.
set "PY="
where python >nul 2>nul && set "PY=python"
if not defined PY goto compile
echo Preflight ...
"%PY%" "tools\preflight.py" "BubbleGlass.cs"
if errorlevel 1 goto preflightfail

:compile
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
echo  Want a still frame for inspection? Run dump-frame.bat
echo.
pause
exit /b 0

:nocsc
echo.
echo  .NET Framework 4 compiler not found.
echo.
pause
exit /b 1

:preflightfail
echo.
echo  Preflight found problems - see the lines above.
echo  They are the same errors the compiler would raise, just reported earlier.
echo.
pause
exit /b 1

:failed
echo.
echo  Build FAILED - see errors above.
pause
exit /b 1
