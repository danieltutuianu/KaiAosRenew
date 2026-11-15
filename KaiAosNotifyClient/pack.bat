@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "OUTDIR=%ROOT%bin\Release"
set "DIST=%ROOT%..\dist"
call "%ROOT%build.bat"
if errorlevel 1 exit /b 1

if not exist "%DIST%" mkdir "%DIST%"
set "STAGE=%DIST%\_stage_notify"
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
mkdir "%STAGE%"

copy /Y "%OUTDIR%\KAI - AOS Renew - Client.exe" "%STAGE%\" >nul

set "ZIP=%DIST%\KaiAosNotifyClient.zip"
if exist "%ZIP%" del /F /Q "%ZIP%"

powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
  echo ZIP failed
  exit /b 1
)

rmdir /S /Q "%STAGE%"
echo Packed: %ZIP%
endlocal
