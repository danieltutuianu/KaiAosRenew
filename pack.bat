@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
set "OUTDIR=%ROOT%bin\Release"
set "CLIENT_ROOT=%ROOT%KaiAosNotifyClient"
set "CLIENT_OUT=%CLIENT_ROOT%\bin\Release"
set "DIST=%ROOT%dist"

taskkill /F /IM "KAI - AOS Renew.exe" >nul 2>&1
taskkill /F /IM "KAI - AOS Renew - Client.exe" >nul 2>&1
taskkill /F /IM KaiAosRenew.exe >nul 2>&1
taskkill /F /IM KaiAosNotifyClient.exe >nul 2>&1

call "%ROOT%build.bat"
if errorlevel 1 exit /b 1

call "%CLIENT_ROOT%\build.bat"
if errorlevel 1 exit /b 1

if not exist "%DIST%" mkdir "%DIST%"
set "STAGE=%DIST%\_stage"
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
mkdir "%STAGE%"
mkdir "%STAGE%\KaiAosNotifyClient"

copy /Y "%OUTDIR%\KAI - AOS Renew.exe" "%STAGE%\" >nul
copy /Y "%OUTDIR%\BMW.ISPI.KAI.Decentral.KaiAdminLibData.dll" "%STAGE%\" >nul
copy /Y "%OUTDIR%\BMW.ISPI.KAI.Decentral.KaiBasicData.dll" "%STAGE%\" >nul
if exist "%OUTDIR%\log4net.dll" copy /Y "%OUTDIR%\log4net.dll" "%STAGE%\" >nul
if exist "%OUTDIR%\System.Data.SQLite.dll" copy /Y "%OUTDIR%\System.Data.SQLite.dll" "%STAGE%\" >nul
if exist "%OUTDIR%\SQLite.Interop.dll" copy /Y "%OUTDIR%\SQLite.Interop.dll" "%STAGE%\" >nul
if exist "%OUTDIR%\kai-key.png" copy /Y "%OUTDIR%\kai-key.png" "%STAGE%\" >nul
if exist "%ROOT%CHANGELOG.md" copy /Y "%ROOT%CHANGELOG.md" "%STAGE%\" >nul

copy /Y "%CLIENT_OUT%\KAI - AOS Renew - Client.exe" "%STAGE%\KaiAosNotifyClient\" >nul

set "ZIP=%DIST%\KaiAosRenew.zip"
if exist "%ZIP%" del /F /Q "%ZIP%"

powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
  echo ZIP failed
  exit /b 1
)

rmdir /S /Q "%STAGE%"
echo Packed: %ZIP% (server + KaiAosNotifyClient folder)

REM Also a standalone client zip next to the server zip
set "CLIENT_STAGE=%DIST%\_stage_notify"
if exist "%CLIENT_STAGE%" rmdir /S /Q "%CLIENT_STAGE%"
mkdir "%CLIENT_STAGE%"
copy /Y "%CLIENT_OUT%\KAI - AOS Renew - Client.exe" "%CLIENT_STAGE%\" >nul

set "CLIENT_ZIP=%DIST%\KaiAosNotifyClient.zip"
if exist "%CLIENT_ZIP%" del /F /Q "%CLIENT_ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%CLIENT_STAGE%\*' -DestinationPath '%CLIENT_ZIP%' -Force"
if errorlevel 1 (
  echo Client ZIP failed
  exit /b 1
)
rmdir /S /Q "%CLIENT_STAGE%"
echo Packed: %CLIENT_ZIP%
endlocal
