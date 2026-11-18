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
if exist "%OUTDIR%\Velopack.dll" copy /Y "%OUTDIR%\Velopack.dll" "%STAGE%\" >nul
if exist "%OUTDIR%\Newtonsoft.Json.dll" copy /Y "%OUTDIR%\Newtonsoft.Json.dll" "%STAGE%\" >nul
if exist "%OUTDIR%\System.Data.SQLite.dll" copy /Y "%OUTDIR%\System.Data.SQLite.dll" "%STAGE%\" >nul
if exist "%OUTDIR%\SQLite.Interop.dll" copy /Y "%OUTDIR%\SQLite.Interop.dll" "%STAGE%\" >nul
if exist "%ROOT%..\CHANGELOG.md" copy /Y "%ROOT%..\CHANGELOG.md" "%STAGE%\" >nul
if exist "%ROOT%CHANGELOG.md" copy /Y "%ROOT%CHANGELOG.md" "%STAGE%\" >nul

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
