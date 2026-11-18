# Backfill missing UiStrings / ClientUiStrings keys with English baselines.
# Usage: powershell -File tools\backfill-i18n-en.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = "C:\Users\Administrator\KaiAosRenew" }

function Parse-Pairs([string]$body, [string]$style) {
    # style: "d" = "k","v",   "dict" = { "k", "v" },
    $pairs = New-Object System.Collections.Generic.List[object]
    $i = 0
    $len = $body.Length
    while ($i -lt $len) {
        while ($i -lt $len -and [char]::IsWhiteSpace($body[$i])) { $i++ }
        if ($i -ge $len) { break }

        if ($style -eq "dict") {
            if ($body[$i] -ne "{") { $i++; continue }
            $i++
            while ($i -lt $len -and [char]::IsWhiteSpace($body[$i])) { $i++ }
        }

        if ($i -ge $len -or $body[$i] -ne '"') { $i++; continue }
        $key = Read-CSharpString $body ([ref]$i)
        while ($i -lt $len -and ([char]::IsWhiteSpace($body[$i]) -or $body[$i] -eq ",")) { $i++ }
        if ($i -ge $len -or $body[$i] -ne '"') { continue }
        $val = Read-CSharpString $body ([ref]$i)
        $pairs.Add([pscustomobject]@{ Key = $key; Value = $val })

        while ($i -lt $len -and [char]::IsWhiteSpace($body[$i])) { $i++ }
        if ($style -eq "dict") {
            if ($i -lt $len -and $body[$i] -eq "}") { $i++ }
            while ($i -lt $len -and ([char]::IsWhiteSpace($body[$i]) -or $body[$i] -eq ",")) { $i++ }
        }
        else {
            while ($i -lt $len -and ($body[$i] -eq "," -or [char]::IsWhiteSpace($body[$i]))) { $i++ }
        }
    }
    return $pairs
}

function Read-CSharpString([string]$s, [ref]$i) {
    if ($s[$i.Value] -ne '"') { throw "Expected quote at $($i.Value)" }
    $i.Value++
    $sb = New-Object System.Text.StringBuilder
    while ($i.Value -lt $s.Length) {
        $c = $s[$i.Value]
        if ($c -eq '"') {
            # doubled "" inside verbatim? we use escaped \"
            $i.Value++
            return $sb.ToString()
        }
        if ($c -eq "\" -and ($i.Value + 1) -lt $s.Length) {
            $n = $s[$i.Value + 1]
            switch ($n) {
                '"' { [void]$sb.Append('"'); $i.Value += 2; continue }
                "\" { [void]$sb.Append("\"); $i.Value += 2; continue }
                "n" { [void]$sb.Append("`n"); $i.Value += 2; continue }
                "r" { [void]$sb.Append("`r"); $i.Value += 2; continue }
                "t" { [void]$sb.Append("`t"); $i.Value += 2; continue }
                default { [void]$sb.Append($c); $i.Value++; continue }
            }
        }
        [void]$sb.Append($c)
        $i.Value++
    }
    throw "Unterminated string"
}

function Escape-CSharp([string]$s) {
    if ($null -eq $s) { return "" }
    return ($s -replace "\\", "\\" -replace '"', '\"' -replace "`r`n", "\n" -replace "`n", "\n" -replace "`r", "\n" -replace "`t", "\t")
}

function Backfill-File([string]$path, [string]$style, [string[]]$skipLangs) {
    Write-Host "Backfilling $path ($style)..."
    $text = [System.IO.File]::ReadAllText($path)
    $methodRe = [regex]'private static Dictionary<string, string> ([A-Za-z]+)\(\)\s*\{'
    $matches = $methodRe.Matches($text)
    if ($matches.Count -eq 0) { throw "No methods in $path" }

    # Find method bodies: from match to matching brace end of method
    $methods = @{}
    foreach ($m in $matches) {
        $name = $m.Groups[1].Value
        $startBrace = $m.Index + $m.Length - 1  # '{'
        $depth = 0
        $j = $startBrace
        for (; $j -lt $text.Length; $j++) {
            $ch = $text[$j]
            if ($ch -eq "{") { $depth++ }
            elseif ($ch -eq "}") {
                $depth--
                if ($depth -eq 0) { break }
            }
        }
        $full = $text.Substring($m.Index, $j - $m.Index + 1)
        $methods[$name] = [pscustomobject]@{
            Name = $name
            Start = $m.Index
            End = $j
            Full = $full
        }
    }

    if (-not $methods.ContainsKey("En")) { throw "En() missing in $path" }
    $enBody = $methods["En"].Full
    # Extract inner of D(...) or { ... }
    if ($style -eq "d") {
        $dStart = $enBody.IndexOf("return D(")
        if ($dStart -lt 0) { throw "return D( not found in En" }
        $innerStart = $dStart + "return D(".Length
        $innerEnd = $enBody.LastIndexOf(");")
        $enInner = $enBody.Substring($innerStart, $innerEnd - $innerStart)
    }
    else {
        $braceOpen = $enBody.IndexOf("{", $enBody.IndexOf("return new Dictionary"))
        # first { after return is the dictionary initializer — find matching }
        $depth = 0
        $innerStart = -1
        for ($k = $braceOpen; $k -lt $enBody.Length; $k++) {
            if ($enBody[$k] -eq "{") {
                $depth++
                if ($depth -eq 1) { $innerStart = $k + 1 }
            }
            elseif ($enBody[$k] -eq "}") {
                $depth--
                if ($depth -eq 0) {
                    $enInner = $enBody.Substring($innerStart, $k - $innerStart)
                    break
                }
            }
        }
    }
    $enPairs = Parse-Pairs $enInner $style
    $enMap = @{}
    foreach ($p in $enPairs) { $enMap[$p.Key] = $p.Value }
    Write-Host ("  EN keys: {0}" -f $enMap.Count)

    # Process languages from end to start so indices stay valid
    $ordered = $methods.Values | Sort-Object Start -Descending
    $newText = $text
    foreach ($meth in $ordered) {
        $langCode = $meth.Name.ToLowerInvariant()
        if ($langCode -eq "en") { continue }
        if ($skipLangs -contains $langCode) {
            Write-Host ("  Skip {0}" -f $meth.Name)
            continue
        }

        $full = $meth.Full
        if ($style -eq "d") {
            $dStart = $full.IndexOf("return D(")
            if ($dStart -lt 0) { throw "return D( not found in $($meth.Name)" }
            $innerStart = $dStart + "return D(".Length
            $innerEnd = $full.LastIndexOf(");")
            $inner = $full.Substring($innerStart, $innerEnd - $innerStart)
        }
        else {
            $braceOpen = $full.IndexOf("{", $full.IndexOf("return new Dictionary"))
            $depth = 0
            $innerStartLocal = -1
            $innerEndLocal = -1
            for ($k = $braceOpen; $k -lt $full.Length; $k++) {
                if ($full[$k] -eq "{") {
                    $depth++
                    if ($depth -eq 1) { $innerStartLocal = $k + 1 }
                }
                elseif ($full[$k] -eq "}") {
                    $depth--
                    if ($depth -eq 0) {
                        $innerEndLocal = $k
                        break
                    }
                }
            }
            $inner = $full.Substring($innerStartLocal, $innerEndLocal - $innerStartLocal)
            $innerStart = $innerStartLocal
            $innerEnd = $innerEndLocal
        }

        $existing = Parse-Pairs $inner $style
        $have = @{}
        foreach ($p in $existing) { $have[$p.Key] = $true }
        $missing = @()
        foreach ($k in $enMap.Keys) {
            if (-not $have.ContainsKey($k)) { $missing += $k }
        }
        $missing = $missing | Sort-Object
        Write-Host ("  {0}: have {1}, missing {2}" -f $meth.Name, $have.Count, $missing.Count)
        if ($missing.Count -eq 0) { continue }

        $sb = New-Object System.Text.StringBuilder
        [void]$sb.Append($inner.TrimEnd())
        if (-not $inner.TrimEnd().EndsWith(",")) {
            # ensure trailing comma on last existing entry for d-style continuity
        }
        [void]$sb.AppendLine()
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("                // EN baseline backfill")
        foreach ($k in $missing) {
            $v = Escape-CSharp $enMap[$k]
            $ek = Escape-CSharp $k
            if ($style -eq "d") {
                [void]$sb.AppendLine(('                "{0}", "{1}",' -f $ek, $v))
            }
            else {
                [void]$sb.AppendLine(('                {{ "{0}", "{1}" }},' -f $ek, $v))
            }
        }
        # trim trailing comma on last line for cleanliness — both styles tolerate trailing commas in C#
        $newInner = $sb.ToString().TrimEnd()
        if ($style -eq "d" -and $newInner.EndsWith(",")) {
            $newInner = $newInner.Substring(0, $newInner.Length - 1)
        }

        if ($style -eq "d") {
            $newFull = $full.Substring(0, $innerStart) + "`r`n" + $newInner + "`r`n            " + $full.Substring($innerEnd)
        }
        else {
            $newFull = $full.Substring(0, $innerStart) + "`r`n" + $newInner + "`r`n            " + $full.Substring($innerEnd)
        }
        $newText = $newText.Substring(0, $meth.Start) + $newFull + $newText.Substring($meth.End + 1)
    }

    [System.IO.File]::WriteAllText($path, $newText)
    Write-Host "  Wrote $path"
}

Backfill-File (Join-Path $root "UiStrings.cs") "d" @("ro")
Backfill-File (Join-Path $root "KaiAosNotifyClient\ClientUiStrings.cs") "dict" @("ro")
Write-Host "Done."
