@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

rem ---------------------------------------------------------------------------
rem Optional name/scope check. A python.exe that merely EXISTS is not enough:
rem the Microsoft Store ships a stub that exits 49 with "Python was not found".
rem So each candidate must actually run before it is trusted, and anything that
rem is not a real finding (missing python, script error) must never stop a build.
rem ---------------------------------------------------------------------------
set "PY="
py -c "import sys" >nul 2>nul
if not errorlevel 1 set "PY=py"
if defined PY goto precheck

python -c "import sys" >nul 2>nul
if not errorlevel 1 set "PY=python"
if defined PY goto precheck

python3 -c "import sys" >nul 2>nul
if not errorlevel 1 set "PY=python3"
if defined PY goto precheck

echo [preflight] skipped - no working python found
goto compile

:precheck
echo Preflight ...
"%PY%" "tools\preflight.py" "BubbleGlass.cs"
if errorlevel 2 goto preskip
if errorlevel 1 goto preflightfail
goto compile

:preskip
echo [preflight] could not run - continuing, the compiler is the final judge

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
echo  These are the same errors the compiler would raise, just reported earlier.
echo.
pause
exit /b 1

:failed
echo.
echo  Build FAILED - see errors above.
pause
exit /b 1
