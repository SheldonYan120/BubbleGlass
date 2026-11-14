@echo off
setlocal
cd /d "%~dp0"

echo.
echo  用当前设置渲染一帧，不开窗口、不打扰桌面。
echo.
if not exist "%~dp0BubbleGlass.exe" goto noexe

"%~dp0BubbleGlass.exe" /x

echo.
echo  完成。图片和日志在：
echo    %%TEMP%%\BubbleGlass-frame.png
echo    %%TEMP%%\BubbleGlass.log
echo.
pause
exit /b 0

:noexe
echo.
echo  [X] 没找到 BubbleGlass.exe，请先运行 build.bat 编译。
echo.
pause
exit /b 1
