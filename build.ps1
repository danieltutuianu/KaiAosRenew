# Unified Release build + regression tests for KaiAosRenew / NotifyClient.
# Usage:  powershell -ExecutionPolicy Bypass -File .\build.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

function Find-MSBuild {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($p in $candidates) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" 2>$null |
            Select-Object -First 1
        if ($found -and (Test-Path $found)) { return $found }
    }
    return $null
}

$msb = Find-MSBuild
if (-not $msb) {
    Write-Error "MSBuild.exe not found. Install Visual Studio Build Tools."
    exit 1
}
Write-Host "MSBuild: $msb"

$extra = @()
$tfmPack = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
if (-not (Test-Path $tfmPack)) {
    $fw = "C:\Windows\Microsoft.NET\Framework\v4.0.30319"
    Write-Host "NOTE: .NET 4.8 targeting pack missing — using FrameworkPathOverride=$fw"
    $extra += "/p:FrameworkPathOverride=$fw"
}

function Invoke-Build([string]$project, [switch]$AllowLockedOutput) {
    Write-Host ""
    Write-Host "=== Build $project ===" -ForegroundColor Cyan
    # Prefer Build over Rebuild so a running app does not fail on cleaning locked bin\ files.
    & $msb $project /t:Build /p:Configuration=Release /p:Platform=x86 /v:m /nologo @extra
    if ($LASTEXITCODE -eq 0) { return }
    if ($AllowLockedOutput) {
        $locked = Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -like "*AOS Renew*" -or $_.MainWindowTitle -like "*AOS Renew*" }
        if ($locked) {
            Write-Host "WARNING: output locked by running app — compile may still be OK in obj\; continuing." -ForegroundColor Yellow
            return
        }
    }
    throw "Build failed: $project (exit $LASTEXITCODE)"
}

function Invoke-TestExe([string]$exe) {
    if (-not (Test-Path $exe)) {
        throw "Test exe missing: $exe"
    }
    Write-Host ""
    Write-Host "=== Run $exe ===" -ForegroundColor Cyan
    & $exe
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed: $exe (exit $LASTEXITCODE)"
    }
}

function Ensure-Nuget {
    $nuget = Join-Path $Root "tools\nuget.exe"
    if (-not (Test-Path $nuget)) {
        New-Item -ItemType Directory -Force -Path (Join-Path $Root "tools") | Out-Null
        Write-Host "Downloading nuget.exe..."
        Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/v6.11.0/nuget.exe" -OutFile $nuget
    }
    return $nuget
}

try {
    $nuget = Ensure-Nuget
    Write-Host "NuGet restore..."
    & $nuget restore (Join-Path $Root "packages.config") -PackagesDirectory (Join-Path $Root "packages") -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "nuget restore packages.config failed" }
    & $nuget restore (Join-Path $Root "KaiAosNotifyClient\packages.config") -PackagesDirectory (Join-Path $Root "packages") -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "nuget restore client packages.config failed" }

    Invoke-Build (Join-Path $Root "KaiAosRenew.csproj") -AllowLockedOutput
    Invoke-Build (Join-Path $Root "KaiAosNotifyClient\KaiAosNotifyClient.csproj") -AllowLockedOutput
    Invoke-Build (Join-Path $Root "tests\KaiAos.Logic.Tests\KaiAos.Logic.Tests.csproj")
    Invoke-Build (Join-Path $Root "tests\KaiAos.Protocol.Tests\KaiAos.Protocol.Tests.csproj")

    Invoke-TestExe (Join-Path $Root "tests\KaiAos.Logic.Tests\bin\Release\KaiAos.Logic.Tests.exe")
    Invoke-TestExe (Join-Path $Root "tests\KaiAos.Protocol.Tests\bin\Release\KaiAos.Protocol.Tests.exe")

    Write-Host ""
    Write-Host "ALL BUILD + TESTS PASSED" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ""
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
