@echo off
setlocal EnableExtensions EnableDelayedExpansion
set "ROOT=%~dp0"
set "OUTDIR=%ROOT%bin\Release"
set "CLIENT_ROOT=%ROOT%KaiAosNotifyClient"
set "CLIENT_OUT=%CLIENT_ROOT%\bin\Release"
set "DIST=%ROOT%dist"
set "PACK_SERVER=%DIST%\pack-server"
set "PACK_CLIENT=%DIST%\pack-client"
set "VELO_SERVER=%DIST%\velo-server"
set "VELO_CLIENT=%DIST%\velo-client"
set "REPO=https://github.com/danieltutuianu/KaiAosRenew"
set "NUGET=%ROOT%tools\nuget.exe"

REM Optional version override: release.bat 1.2.1
set "VER_ARG=%~1"

taskkill /F /IM "KAI - AOS Renew.exe" >nul 2>&1
taskkill /F /IM "KAI - AOS Renew - Client.exe" >nul 2>&1

if not exist "%NUGET%" (
  if not exist "%ROOT%tools" mkdir "%ROOT%tools"
  powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile '%NUGET%'"
)
"%NUGET%" restore "%ROOT%packages.config" -PackagesDirectory "%ROOT%packages" -NonInteractive
if errorlevel 1 exit /b 1
"%NUGET%" restore "%CLIENT_ROOT%\packages.config" -PackagesDirectory "%ROOT%packages" -NonInteractive
if errorlevel 1 exit /b 1

call "%ROOT%build.bat"
if errorlevel 1 exit /b 1
call "%CLIENT_ROOT%\build.bat"
if errorlevel 1 exit /b 1

if defined VER_ARG (
  set "SERVER_VER=%VER_ARG%"
  set "CLIENT_VER=%VER_ARG%"
) else (
  for /f "usebackq tokens=2 delims=()" %%A in (`findstr /R /C:"AssemblyFileVersion" "%ROOT%Properties\AssemblyInfo.cs"`) do set "SERVER_FILE=%%~A"
  for /f "usebackq tokens=2 delims=()" %%A in (`findstr /R /C:"AssemblyFileVersion" "%CLIENT_ROOT%\Properties\AssemblyInfo.cs"`) do set "CLIENT_FILE=%%~A"
  for /f "tokens=1-3 delims=." %%a in ("!SERVER_FILE!") do set "SERVER_VER=%%a.%%b.%%c"
  for /f "tokens=1-3 delims=." %%a in ("!CLIENT_FILE!") do set "CLIENT_VER=%%a.%%b.%%c"
)

echo Server packVersion=!SERVER_VER!
echo Client packVersion=!CLIENT_VER!

if exist "%PACK_SERVER%" rmdir /S /Q "%PACK_SERVER%"
if exist "%PACK_CLIENT%" rmdir /S /Q "%PACK_CLIENT%"
mkdir "%PACK_SERVER%"
mkdir "%PACK_CLIENT%"

copy /Y "%OUTDIR%\KAI - AOS Renew.exe" "%PACK_SERVER%\" >nul
copy /Y "%OUTDIR%\BMW.ISPI.KAI.Decentral.KaiAdminLibData.dll" "%PACK_SERVER%\" >nul
copy /Y "%OUTDIR%\BMW.ISPI.KAI.Decentral.KaiBasicData.dll" "%PACK_SERVER%\" >nul
if exist "%OUTDIR%\Velopack.dll" copy /Y "%OUTDIR%\Velopack.dll" "%PACK_SERVER%\" >nul
if exist "%OUTDIR%\Newtonsoft.Json.dll" copy /Y "%OUTDIR%\Newtonsoft.Json.dll" "%PACK_SERVER%\" >nul
if exist "%OUTDIR%\log4net.dll" copy /Y "%OUTDIR%\log4net.dll" "%PACK_SERVER%\" >nul
if exist "%OUTDIR%\System.Data.SQLite.dll" copy /Y "%OUTDIR%\System.Data.SQLite.dll" "%PACK_SERVER%\" >nul
if exist "%OUTDIR%\SQLite.Interop.dll" copy /Y "%OUTDIR%\SQLite.Interop.dll" "%PACK_SERVER%\" >nul
if exist "%OUTDIR%\kai-key.png" copy /Y "%OUTDIR%\kai-key.png" "%PACK_SERVER%\" >nul
if exist "%OUTDIR%\CHANGELOG.md" copy /Y "%OUTDIR%\CHANGELOG.md" "%PACK_SERVER%\" >nul
if exist "%ROOT%CHANGELOG.md" copy /Y "%ROOT%CHANGELOG.md" "%PACK_SERVER%\" >nul

copy /Y "%CLIENT_OUT%\KAI - AOS Renew - Client.exe" "%PACK_CLIENT%\" >nul
if exist "%CLIENT_OUT%\Velopack.dll" copy /Y "%CLIENT_OUT%\Velopack.dll" "%PACK_CLIENT%\" >nul
if exist "%CLIENT_OUT%\Newtonsoft.Json.dll" copy /Y "%CLIENT_OUT%\Newtonsoft.Json.dll" "%PACK_CLIENT%\" >nul
if exist "%CLIENT_OUT%\System.Data.SQLite.dll" copy /Y "%CLIENT_OUT%\System.Data.SQLite.dll" "%PACK_CLIENT%\" >nul
if exist "%CLIENT_OUT%\SQLite.Interop.dll" copy /Y "%CLIENT_OUT%\SQLite.Interop.dll" "%PACK_CLIENT%\" >nul
if exist "%CLIENT_OUT%\CHANGELOG.md" copy /Y "%CLIENT_OUT%\CHANGELOG.md" "%PACK_CLIENT%\" >nul

where vpk >nul 2>&1
if errorlevel 1 (
  echo Installing vpk global tool...
  dotnet tool install -g vpk
  if errorlevel 1 (
    echo Failed to install vpk. Install .NET SDK and retry.
    exit /b 1
  )
)

if exist "%VELO_SERVER%" rmdir /S /Q "%VELO_SERVER%"
if exist "%VELO_CLIENT%" rmdir /S /Q "%VELO_CLIENT%"
mkdir "%VELO_SERVER%"
mkdir "%VELO_CLIENT%"

echo Downloading previous server releases for delta (ok to fail on first ship)...
vpk download github --repoUrl %REPO% -c server-win-x86 -o "%VELO_SERVER%" 2>nul
echo Downloading previous client releases for delta (ok to fail on first ship)...
vpk download github --repoUrl %REPO% -c client-win-x86 -o "%VELO_CLIENT%" 2>nul

REM Optional Authenticode signing (OV/EV from DigiCert/Sectigo/SSL.com etc.).
REM Self-signed does NOT clear SmartScreen. Prefer EV for instant reputation.
REM   set CODESIGN_PFX=C:\absolute\path\to\codesign.pfx
REM   set CODESIGN_PASSWORD=...
REM Never commit the PFX or password.
set "SIGN_ENABLED="
set "SIGN_PARAMS="
if defined CODESIGN_PFX if defined CODESIGN_PASSWORD (
  if exist "!CODESIGN_PFX!" (
    set "SIGN_ENABLED=1"
    set "SIGN_PARAMS=/fd sha256 /td sha256 /f !CODESIGN_PFX! /p !CODESIGN_PASSWORD! /tr http://timestamp.digicert.com /td sha256"
    echo Signing enabled: !CODESIGN_PFX!
  ) else (
    echo CODESIGN_PFX not found: !CODESIGN_PFX!
    exit /b 1
  )
) else (
  echo Signing skipped ^(no CODESIGN_PFX / CODESIGN_PASSWORD^)
)

set "NOTES=%DIST%\release-notes.md"
echo Extracting CHANGELOG notes for !SERVER_VER!...
powershell -NoProfile -File "%ROOT%tools\extract-changelog.ps1" -Version !SERVER_VER! -OutFile "%NOTES%"
if errorlevel 1 exit /b 1

echo Packing server...
if defined SIGN_ENABLED (
  vpk pack -u KaiAosRenew -v !SERVER_VER! -p "%PACK_SERVER%" -e "KAI - AOS Renew.exe" -c server-win-x86 --packTitle "KAI - AOS Renew" -o "%VELO_SERVER%" --noPortable --releaseNotes "%NOTES%" --signParams "!SIGN_PARAMS!"
) else (
  vpk pack -u KaiAosRenew -v !SERVER_VER! -p "%PACK_SERVER%" -e "KAI - AOS Renew.exe" -c server-win-x86 --packTitle "KAI - AOS Renew" -o "%VELO_SERVER%" --noPortable --releaseNotes "%NOTES%"
)
if errorlevel 1 exit /b 1

REM Version Setup filename (Velopack omits version; nupkgs already have it).
call :RenameVersionedAsset "%VELO_SERVER%" "KaiAosRenew-server-win-x86-Setup.exe" "KaiAosRenew-!SERVER_VER!-server-win-x86-Setup.exe" "assets.server-win-x86.json"
if errorlevel 1 exit /b 1

echo Packing client...
if defined SIGN_ENABLED (
  vpk pack -u KaiAosNotifyClient -v !CLIENT_VER! -p "%PACK_CLIENT%" -e "KAI - AOS Renew - Client.exe" -c client-win-x86 --packTitle "KAI - AOS Renew - Client" -o "%VELO_CLIENT%" --noPortable --releaseNotes "%NOTES%" --signParams "!SIGN_PARAMS!"
) else (
  vpk pack -u KaiAosNotifyClient -v !CLIENT_VER! -p "%PACK_CLIENT%" -e "KAI - AOS Renew - Client.exe" -c client-win-x86 --packTitle "KAI - AOS Renew - Client" -o "%VELO_CLIENT%" --noPortable --releaseNotes "%NOTES%"
)
if errorlevel 1 exit /b 1

call :RenameVersionedAsset "%VELO_CLIENT%" "KaiAosNotifyClient-client-win-x86-Setup.exe" "KaiAosNotifyClient-!CLIENT_VER!-client-win-x86-Setup.exe" "assets.client-win-x86.json"
if errorlevel 1 exit /b 1

set "GH_TOKEN="
for /f "usebackq delims=" %%T in (`gh auth token 2^>nul`) do set "GH_TOKEN=%%T"
if not defined GH_TOKEN if defined GITHUB_TOKEN set "GH_TOKEN=%GITHUB_TOKEN%"
if not defined GH_TOKEN (
  echo Missing GitHub token. Run "gh auth login" or set GITHUB_TOKEN.
  exit /b 1
)

echo Uploading server release v!SERVER_VER!...
vpk upload github --repoUrl %REPO% --token "!GH_TOKEN!" -o "%VELO_SERVER%" -c server-win-x86 --publish --tag v!SERVER_VER! --releaseName "KAI - AOS Renew !SERVER_VER!"
if errorlevel 1 exit /b 1

echo Uploading client assets onto same release...
vpk upload github --repoUrl %REPO% --token "!GH_TOKEN!" -o "%VELO_CLIENT%" -c client-win-x86 --merge --publish --tag v!SERVER_VER!
if errorlevel 1 exit /b 1

echo Setting GitHub release notes from CHANGELOG...
gh release edit v!SERVER_VER! --notes-file "%NOTES%"
if errorlevel 1 (
  echo Warning: gh release edit failed; release assets are still uploaded.
)

echo.
echo Release done: %REPO%/releases/tag/v!SERVER_VER!
echo Install from the versioned Setup.exe on that release, then test Auto update.
endlocal
exit /b 0

:RenameVersionedAsset
REM %1=dir  %2=oldName  %3=newName  %4=assetsJsonName
set "RADIR=%~1"
set "RAOLD=%~2"
set "RANEW=%~3"
set "RAJSON=%~4"
if not exist "%RADIR%\%RAOLD%" (
  echo Missing asset to rename: %RADIR%\%RAOLD%
  exit /b 1
)
if exist "%RADIR%\%RANEW%" del /F /Q "%RADIR%\%RANEW%" >nul 2>&1
move /Y "%RADIR%\%RAOLD%" "%RADIR%\%RANEW%" >nul
if errorlevel 1 (
  echo Failed to rename %RAOLD% to %RANEW%
  exit /b 1
)
if exist "%RADIR%\%RAJSON%" (
  powershell -NoProfile -Command ^
    "$p=Join-Path '%RADIR%' '%RAJSON%';" ^
    "$c=[IO.File]::ReadAllText($p);" ^
    "$c=$c.Replace('%RAOLD%','%RANEW%');" ^
    "[IO.File]::WriteAllText($p,$c)"
  if errorlevel 1 (
    echo Failed to patch %RAJSON% for %RANEW%
    exit /b 1
  )
)
echo Renamed %RAOLD% -^> %RANEW%
exit /b 0
