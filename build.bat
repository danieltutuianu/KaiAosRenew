@echo off
setlocal EnableExtensions
set "MSBUILD=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
set "OUTDIR=%~dp0bin\Release"
set "ASSETDIR=%~dp0assets"
set "KAIGUI=C:\Program Files (x86)\BMW\ISPI\KAI\KAIGui\KaiGui.ico"
set "BMWDIR=C:\Program Files (x86)\BMW\ISPI\KAI\KAI"
set "BMWLIB=%BMWDIR%\BMW.ISPI.KAI.Decentral.KaiAdminLibData.dll"
set "BMWBASE=%BMWDIR%\BMW.ISPI.KAI.Decentral.KaiBasicData.dll"

if not exist "%MSBUILD%" (
  echo MSBuild.exe not found
  exit /b 1
)
if not exist "%BMWLIB%" (
  echo BMW KaiAdminLibData.dll not found
  exit /b 1
)
if not exist "%KAIGUI%" (
  echo KaiGui.ico not found
  exit /b 1
)

if not exist "%OUTDIR%" mkdir "%OUTDIR%"
if not exist "%ASSETDIR%" mkdir "%ASSETDIR%"
copy /Y "%KAIGUI%" "%ASSETDIR%\app.ico" >nul

set "NUGET=%~dp0tools\nuget.exe"
if not exist "%NUGET%" (
  if not exist "%~dp0tools" mkdir "%~dp0tools"
  powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile '%NUGET%'"
)
"%NUGET%" restore "%~dp0packages.config" -PackagesDirectory "%~dp0packages" -NonInteractive
if errorlevel 1 exit /b 1

"%MSBUILD%" "%~dp0KaiAosRenew.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=x86 /v:m /nologo
if errorlevel 1 exit /b 1

copy /Y "%BMWLIB%" "%OUTDIR%\" >nul
copy /Y "%BMWBASE%" "%OUTDIR%\" >nul
if exist "%ASSETDIR%\kai-key.png" copy /Y "%ASSETDIR%\kai-key.png" "%OUTDIR%\" >nul
if exist "%~dp0CHANGELOG.md" copy /Y "%~dp0CHANGELOG.md" "%OUTDIR%\" >nul
echo Built: %OUTDIR%\KAI - AOS Renew.exe
endlocal
