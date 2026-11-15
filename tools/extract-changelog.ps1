# Extract a Keep a Changelog section for GitHub release notes.
# Usage: extract-changelog.ps1 -Version 1.2.7 -OutFile dist\release-notes.md
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,
    [Parameter(Mandatory = $true)]
    [string] $OutFile,
    [string] $ChangelogPath = ""
)

$ErrorActionPreference = "Stop"
$ver = $Version.Trim().TrimStart("v", "V")

if ([string]::IsNullOrWhiteSpace($ChangelogPath)) {
    $ChangelogPath = Join-Path (Split-Path $PSScriptRoot -Parent) "CHANGELOG.md"
}

$outDir = Split-Path -Parent $OutFile
if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

if (-not (Test-Path $ChangelogPath)) {
    "Release $ver — see CHANGELOG.md" | Set-Content -Path $OutFile -Encoding utf8
    Write-Host "CHANGELOG missing; wrote fallback notes."
    exit 0
}

$lines = Get-Content -Path $ChangelogPath -Encoding utf8
$capture = $false
$buf = New-Object System.Collections.Generic.List[string]
# Match ## [1.2.7] or ## [1.2.7] - date
$startRe = [regex]::new('^##\s+\[' + [regex]::Escape($ver) + '\]')

foreach ($line in $lines) {
    if ($startRe.IsMatch($line)) {
        $capture = $true
        $buf.Add($line)
        continue
    }
    if ($capture -and $line -match '^##\s+') {
        break
    }
    if ($capture) {
        $buf.Add($line)
    }
}

if ($buf.Count -eq 0) {
    "Release $ver — see CHANGELOG.md" | Set-Content -Path $OutFile -Encoding utf8
    Write-Host "No CHANGELOG section for $ver; wrote fallback notes."
    exit 0
}

# Trim trailing blank lines
while ($buf.Count -gt 0 -and [string]::IsNullOrWhiteSpace($buf[$buf.Count - 1])) {
    $buf.RemoveAt($buf.Count - 1)
}

$buf -join "`n" | Set-Content -Path $OutFile -Encoding utf8 -NoNewline
Add-Content -Path $OutFile -Value "" -Encoding utf8
Write-Host "Wrote release notes for $ver -> $OutFile"
exit 0
