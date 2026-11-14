@echo off
setlocal
cd /d "%~dp0"

echo.
echo  Render one frame with the current settings - no window, nothing on screen.
echo.
if not exist "%~dp0BubbleGlass.exe" goto noexe

"%~dp0BubbleGlass.exe" /x

echo.
echo  Done. Output:
echo    %%TEMP%%\BubbleGlass-frame.png
echo    %%TEMP%%\BubbleGlass.log
echo.
pause
exit /b 0

:noexe
echo.
echo  [X] BubbleGlass.exe not found - run build.bat first.
echo.
pause
exit /b 1
