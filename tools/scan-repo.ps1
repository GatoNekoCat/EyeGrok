# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Scans tracked files for host paths, mail, deploy hostnames, and key-shaped tokens.
# Fails closed. Does not print matched text.

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

$filesRaw = & git ls-files
if ($LASTEXITCODE -ne 0) {
    Write-Error "git ls-files failed"
    exit 1
}

# Patterns are concatenated so this script does not contain the literals it rejects.
$rules = @(
    @{ Name = "windows-users"; Regex = [regex]('(?i)[A-Za-z]:' + '\\' + 'Users' + '\\') },
    @{ Name = "mac-users"; Regex = [regex]('/' + 'Users' + '/') },
    @{ Name = "home"; Regex = [regex]('/' + 'home' + '/') },
    @{ Name = "gmail"; Regex = [regex]('@gmail' + '\.com') },
    @{ Name = "workers-dev"; Regex = [regex]('\.workers' + '\.dev') },
    @{ Name = "pem"; Regex = [regex]('-----' + 'BEGIN') },
    @{ Name = "bearer"; Regex = [regex]('Bearer [A-Za-z0-9_\-]{16,}') },
    @{ Name = "base64url"; Regex = [regex]('(?<![A-Za-z0-9_\-])[A-Za-z0-9_\-]{32,}(?![A-Za-z0-9_\-])') }
)

$allowedWorkers = 'REPLACE_ME' + '.workers' + '.dev'

function Test-LockOrHashFile([string]$rel) {
    $name = [System.IO.Path]::GetFileName($rel)
    if ($name -eq "packages-lock.json") { return $true }
    if ($name -eq "package-lock.json") { return $true }
    if ($name -eq "models.sha256") { return $true }
    if ($name -like "*.lock") { return $true }
    return $false
}

# 32-byte keys are base64url and almost always leave the hex alphabet.
# Pure hex of these lengths is a Unity GUID, a git SHA, or a sha256, not a key.
function Test-HexIdentifier([string]$token) {
    if ($token -notmatch '^[0-9a-fA-F]+$') { return $false }
    $n = $token.Length
    return ($n -eq 32 -or $n -eq 40 -or $n -eq 64)
}

# Unity serialized names and C# symbols are long runs of letters and underscores.
# A key-shaped token has a digit or a hyphen. Alphabetic tokens are not keys.
function Test-AlphabeticIdentifier([string]$token) {
    return $token -match '^[A-Za-z_]+$'
}

# Unity bitmasks such as m_StackTraceTypes are runs of 0 and 1, not keys.
function Test-Bitmask([string]$token) {
    return $token -match '^[01]+$'
}

# Field names like AndroidEnableArmv9SecurityFeatures. A real key is not a
# short-digit camelCase word. At most two digits and a 5-letter lowercase run.
function Test-CodeIdentifier([string]$token) {
    if ($token -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { return $false }
    if (([regex]::Matches($token, '\d')).Count -gt 2) { return $false }
    $run = 0
    $cur = 0
    foreach ($ch in $token.ToCharArray()) {
        if ([char]::IsLower($ch)) {
            $cur++
            if ($cur -gt $run) { $run = $cur }
        } else {
            $cur = 0
        }
    }
    return $run -ge 5
}

$extra = @()
$forbiddenPath = Join-Path $RepoRoot ".local/forbidden.txt"
if (Test-Path -LiteralPath $forbiddenPath) {
    $i = 0
    foreach ($line in [System.IO.File]::ReadAllLines($forbiddenPath)) {
        $i++
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith("#")) { continue }
        try {
            $extra += @{ Name = ("forbidden#" + $i); Regex = [regex]$t }
        } catch {
            Write-Output ("scan-repo: FAIL forbidden.txt line " + $i + " is not a regex")
            exit 1
        }
    }
}

$fail = $false
foreach ($rel in $filesRaw) {
    if ([string]::IsNullOrWhiteSpace($rel)) { continue }
    $full = Join-Path $RepoRoot ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) { continue }
    $info = Get-Item -LiteralPath $full
    if ($info.PSIsContainer) { continue }

    $text = [System.IO.File]::ReadAllText($full)
    $lines = $text -split "`n"
    $skipB64 = Test-LockOrHashFile $rel

    for ($n = 0; $n -lt $lines.Length; $n++) {
        $line = $lines[$n]
        foreach ($rule in $rules) {
            if ($rule.Name -eq "base64url" -and $skipB64) { continue }
            $matches = $rule.Regex.Matches($line)
            if ($matches.Count -eq 0) { continue }

            if ($rule.Name -eq "workers-dev") {
                $stripped = $line.Replace($allowedWorkers, "")
                if (-not $rule.Regex.IsMatch($stripped)) { continue }
            }

            if ($rule.Name -eq "base64url") {
                # Stock Unity PlayerSettings placeholders, not project secrets.
                if ($line -match '^\s*ps4(Passcode|ContentID)\s*:') { continue }
                $bad = $false
                foreach ($m in $matches) {
                    if (Test-HexIdentifier $m.Value) { continue }
                    if (Test-AlphabeticIdentifier $m.Value) { continue }
                    if (Test-Bitmask $m.Value) { continue }
                    if (Test-CodeIdentifier $m.Value) { continue }
                    $bad = $true
                    break
                }
                if (-not $bad) { continue }
            }

            Write-Output ("scan-repo: FAIL " + $rel + ":" + ($n + 1) + " " + $rule.Name)
            $fail = $true
        }

        foreach ($rule in $extra) {
            if ($rule.Regex.IsMatch($line)) {
                Write-Output ("scan-repo: FAIL " + $rel + ":" + ($n + 1) + " " + $rule.Name)
                $fail = $true
            }
        }
    }
}

if ($fail) { exit 1 }
Write-Output "scan-repo: clean"
exit 0
