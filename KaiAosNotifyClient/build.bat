@echo off
setlocal EnableExtensions
set "MSBUILD=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
set "ROOT=%~dp0"
set "OUTDIR=%ROOT%bin\Release"
set "ASSETDIR=%ROOT%assets"
set "KAIGUI=C:\Program Files (x86)\BMW\ISPI\KAI\KAIGui\KaiGui.ico"
set "SERVER_ICO=%ROOT%..\assets\app.ico"

if not exist "%MSBUILD%" (
  echo MSBuild.exe not found
  exit /b 1
)

if not exist "%ASSETDIR%" mkdir "%ASSETDIR%"
if exist "%KAIGUI%" (
  copy /Y "%KAIGUI%" "%ASSETDIR%\app.ico" >nul
  copy /Y "%KAIGUI%" "%ROOT%app.ico" >nul
) else if exist "%SERVER_ICO%" (
  copy /Y "%SERVER_ICO%" "%ASSETDIR%\app.ico" >nul
  copy /Y "%SERVER_ICO%" "%ROOT%app.ico" >nul
)

set "NUGET=%ROOT%..\tools\nuget.exe"
if not exist "%NUGET%" (
  if not exist "%ROOT%..\tools" mkdir "%ROOT%..\tools"
  powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile '%NUGET%'"
)
"%NUGET%" restore "%ROOT%packages.config" -PackagesDirectory "%ROOT%..\packages" -NonInteractive
if errorlevel 1 exit /b 1

"%MSBUILD%" "%ROOT%KaiAosNotifyClient.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=x86 /v:m /nologo
if errorlevel 1 exit /b 1

if exist "%ROOT%..\CHANGELOG.md" copy /Y "%ROOT%..\CHANGELOG.md" "%OUTDIR%\CHANGELOG.md" >nul

echo Built: %OUTDIR%\KAI - AOS Renew - Client.exe
endlocal
