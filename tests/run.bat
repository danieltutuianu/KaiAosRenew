@echo off
setlocal EnableExtensions
set "MSBUILD=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
set "ROOT=%~dp0"
set "NUGET=%ROOT%..\tools\nuget.exe"
set "PACKAGES=%ROOT%..\packages"
set "PROTO_DIR=%ROOT%KaiAos.Protocol.Tests"
set "LOGIC_DIR=%ROOT%KaiAos.Logic.Tests"
rem No trailing backslash inside quoted /p:OutputPath= (escapes the closing quote).
set "PROTO_OUT=%PROTO_DIR%\bin\Release"
set "LOGIC_OUT=%LOGIC_DIR%\bin\Release"

if not exist "%MSBUILD%" (
  echo MSBuild.exe not found
  exit /b 1
)

if not exist "%PACKAGES%\Newtonsoft.Json.13.0.4\lib\net45\Newtonsoft.Json.dll" (
  if not exist "%NUGET%" (
    if not exist "%ROOT%..\tools" mkdir "%ROOT%..\tools"
    powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile '%NUGET%'"
  )
  "%NUGET%" restore "%ROOT%..\packages.config" -PackagesDirectory "%PACKAGES%" -NonInteractive
  if errorlevel 1 exit /b 1
)

echo Building protocol tests...
"%MSBUILD%" "%PROTO_DIR%\KaiAos.Protocol.Tests.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=x86 /p:OutputPath="%PROTO_OUT%" /p:OutDir="%PROTO_OUT%\\" /v:m /nologo
if errorlevel 1 exit /b 1

set "PROTO_EXE=%PROTO_OUT%\KaiAos.Protocol.Tests.exe"
if not exist "%PROTO_EXE%" (
  echo Missing %PROTO_EXE%
  exit /b 1
)

echo Running protocol tests...
"%PROTO_EXE%"
set "TEST_EXIT=%ERRORLEVEL%"
if not "%TEST_EXIT%"=="0" (
  echo Protocol tests failed with exit %TEST_EXIT%
  exit /b %TEST_EXIT%
)
echo Protocol tests OK

echo Building logic tests...
"%MSBUILD%" "%LOGIC_DIR%\KaiAos.Logic.Tests.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=x86 /p:OutputPath="%LOGIC_OUT%" /p:OutDir="%LOGIC_OUT%\\" /v:m /nologo
if errorlevel 1 exit /b 1

set "LOGIC_EXE=%LOGIC_OUT%\KaiAos.Logic.Tests.exe"
if not exist "%LOGIC_EXE%" (
  echo Missing %LOGIC_EXE%
  exit /b 1
)

echo Running logic tests...
"%LOGIC_EXE%"
set "TEST_EXIT=%ERRORLEVEL%"
if not "%TEST_EXIT%"=="0" (
  echo Logic tests failed with exit %TEST_EXIT%
  exit /b %TEST_EXIT%
)
echo Logic tests OK

endlocal
exit /b 0
