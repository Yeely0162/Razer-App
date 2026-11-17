@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "VER=0.1.0"
set "LOG=%~dp0build.log"
set "DIST=%~dp0..\dist"
set "RELDIR=%~dp0..\release"
set "ZIPNAME=Razer-Chroma-WLED-Studio-v%VER%-win-x64.zip"
set "ZIP=%RELDIR%\%ZIPNAME%"
set "OUT=%~dp0RazerChromaWLEDConnect\bin\Release\net8.0-windows"

echo === Razer Chroma WLED Studio v%VER% === > "%LOG%"
echo date: %DATE% %TIME% >> "%LOG%"
echo folder: %CD% >> "%LOG%"
echo. >> "%LOG%"

set "DOTNET=dotnet"
where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
)
"%DOTNET%" --version >nul 2>&1
if errorlevel 1 (
  >>"%LOG%" echo [x] dotnet was not found. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
  echo.
  echo Install the .NET 8 SDK, then run this file again.
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

echo === dotnet build === >> "%LOG%"
"%DOTNET%" build "Razer Chroma WLED Connect App.sln" -c Release --nologo -v:m >> "%LOG%" 2>&1
set "CODE=%ERRORLEVEL%"
echo. >> "%LOG%"
echo === exit code: %CODE% === >> "%LOG%"

if not "%CODE%"=="0" (
  echo.
  type "%LOG%"
  echo.
  echo Build failed, exit code %CODE%. Full log: %LOG%
  pause
  exit /b %CODE%
)

rem ---- copy the fresh build somewhere a human can find it ----
if not exist "%DIST%" mkdir "%DIST%"
xcopy "%OUT%\*" "%DIST%\" /E /Y /I >> "%LOG%" 2>&1

rem keep the existing device list, but seed a new one from the old app if there is none
if not exist "%DIST%\settings.xml" (
  if exist "%~dp0..\settings.xml" copy /Y "%~dp0..\settings.xml" "%DIST%\settings.xml" >nul
)

rem ---- release zip: program files only, never the local settings ----
if not exist "%RELDIR%" mkdir "%RELDIR%"
if exist "%ZIP%" del /q "%ZIP%"

echo === packaging %ZIPNAME% === >> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d='%DIST%'; $z='%ZIP%'; $items = Get-ChildItem -Path (Join-Path $d '*') | Where-Object { $_.Name -ne 'settings.xml' -and $_.Name -ne 'crash.log' }; if ($items) { Compress-Archive -LiteralPath $items.FullName -DestinationPath $z -CompressionLevel Optimal -Force }" >> "%LOG%" 2>&1

echo. >> "%LOG%"
echo === output === >> "%LOG%"
dir /b "%DIST%" >> "%LOG%" 2>&1
echo. >> "%LOG%"
dir /b "%RELDIR%" >> "%LOG%" 2>&1

echo.
type "%LOG%"
echo.
echo Build OK.
echo Run:  %DIST%\RazerChromaWLEDConnect.exe
echo Zip:  %ZIP%
echo.
start "" "%DIST%"
pause
exit /b 0
