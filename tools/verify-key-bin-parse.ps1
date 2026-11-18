$ErrorActionPreference = 'Stop'
$asmPath = 'C:\Users\Administrator\KaiAosRenew\bin\Release\KAI - AOS Renew.exe'
$binPath = 'C:\key-bins\WBA78FF0408D74503.bin'
$asm = [Reflection.Assembly]::LoadFrom($asmPath)
$t = $asm.GetType('KaiAosRenew.KeyActivityMonitor')
if (-not $t) {
    $asm.GetTypes() | Where-Object { $_.Name -like '*Key*' } | ForEach-Object { $_.FullName }
    throw 'KeyActivityMonitor type missing'
}
$m = $t.GetMethod('TryParseKeyReaderBin', [Reflection.BindingFlags]'Public,NonPublic,Static')
$refArgs = New-Object 'object[]' 2
$refArgs[0] = $binPath
$refArgs[1] = $null
$ok = $m.Invoke($null, $refArgs)
$f = $refArgs[1]
Write-Host "ok=$ok"
Write-Host ("Vin=" + $f.Vin)
Write-Host ("Paint=" + $f.Paint)
Write-Host ("Upholstery=" + $f.Upholstery)
Write-Host ("MileageKm=" + $f.MileageKm)
Write-Host ("ILevelCurrent=" + $f.ILevelCurrent)
Write-Host ("ILevelBackup=" + $f.ILevelBackup)
Write-Host ("ILevelPlant=" + $f.ILevelPlant)
$pass = ($f.Vin -eq 'WBA78FF0408D74503') `
    -and ($f.Paint -eq '0475') `
    -and ($f.Upholstery -eq 'KGNL') `
    -and ($f.MileageKm -eq 74846) `
    -and ($f.ILevelCurrent -eq 'S18A-25-07-538')
Write-Host "PASS=$pass"
if (-not $pass) { exit 2 }
