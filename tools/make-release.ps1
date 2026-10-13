# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Packs an existing APK and the installer into dist/. Does not build.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Version must look like 0.1.0"
    exit 1
}

$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$apkSrc = Join-Path $repo "Builds\EyeGrok-v001.apk"
if (-not (Test-Path -LiteralPath $apkSrc)) {
    Write-Error "Builds/EyeGrok-v001.apk is missing. This script does not build it."
    exit 1
}

$name = "EyeGrok-" + $Version
$dist = Join-Path $repo "dist"
$stage = Join-Path $dist $name
$zipPath = Join-Path $dist ($name + ".zip")
$sumsPath = Join-Path $dist "SHA256SUMS"
$notesPath = Join-Path $dist ("RELEASE_NOTES-" + $Version + ".md")

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage "lib") | Out-Null

Copy-Item -LiteralPath $apkSrc -Destination (Join-Path $stage ($name + ".apk"))
Copy-Item -LiteralPath (Join-Path $repo "installer\EyeGrok-Setup.cmd") -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo "installer\EyeGrok-Setup.ps1") -Destination $stage
Get-ChildItem -LiteralPath (Join-Path $repo "installer\lib") -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage "lib")
}
Copy-Item -LiteralPath (Join-Path $repo "LICENSE") -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo "NOTICE") -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo "THIRD_PARTY_NOTICES.md") -Destination $stage

$readme = @"
EyeGrok $Version

Before you start: your relay (with PAIR_KEY) and Bot webhook, see docs/RELAY.md in the source tree.

1. Turn on Developer Mode on the Frame.
2. Start Lepton Development from the Steam Library on the Frame.
3. Plug the Frame into the PC with USB.
4. Double-click EyeGrok-Setup.cmd and follow it.
5. In EyeGrok, scan the QR or enter the code, then hold to talk.

Platform-tools are not in this zip. The installer downloads them after you accept Google's terms.
"@
$readmeLines = @($readme -split "`r?`n")
if ($readmeLines.Count -gt 15) {
    Write-Error "README.txt is longer than 15 lines"
    exit 1
}
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $stage "README.txt"), $readme.TrimEnd() + "`n", $utf8)

foreach ($file in (Get-ChildItem -LiteralPath $stage -Recurse -Force -File)) {
    if ($file.Name -eq ".dev.vars") {
        Write-Error "refusing to pack .dev.vars"
        exit 1
    }
    $rel = $file.FullName.Substring($stage.Length)
    if ($rel -match '\\tools\\test\\' -or $file.Name -like '*fixture*') {
        Write-Error "refusing to pack test or fixture files"
        exit 1
    }
}

$changelog = Join-Path $repo "CHANGELOG.md"
$lines = [System.IO.File]::ReadAllLines($changelog)
$header = "## [" + $Version + "]"
$start = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i].StartsWith($header)) { $start = $i; break }
}
if ($start -lt 0) {
    Write-Error "CHANGELOG has no section for this version"
    exit 1
}
$end = $lines.Length
for ($i = $start + 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i].StartsWith("## [")) { $end = $i; break }
}
$section = ($lines[$start..($end - 1)] -join "`n").TrimEnd()
$notes = "# Release notes " + $Version + "`n`nDraft. Not a published release.`n`n" + $section + "`n"
[System.IO.File]::WriteAllText($notesPath, $notes, $utf8)

$pack = Join-Path $dist "_pack"
if (Test-Path -LiteralPath $pack) { Remove-Item -LiteralPath $pack -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pack | Out-Null
$packedStage = Join-Path $pack $name
Move-Item -LiteralPath $stage -Destination $packedStage
[void][System.Reflection.Assembly]::Load("System.IO.Compression.FileSystem, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")
try {
    [System.IO.Compression.ZipFile]::CreateFromDirectory($pack, $zipPath)
} finally {
    Move-Item -LiteralPath $packedStage -Destination $stage
    Remove-Item -LiteralPath $pack -Recurse -Force -ErrorAction SilentlyContinue
}

function Get-LowerSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLower()
}

$zipHash = Get-LowerSha256 $zipPath
$apkHash = Get-LowerSha256 (Join-Path $stage ($name + ".apk"))
$sumText = $zipHash + "  " + $name + ".zip`n" + $apkHash + "  " + $name + "/" + $name + ".apk`n"
[System.IO.File]::WriteAllText($sumsPath, $sumText, $utf8)

foreach ($sumLine in ($sumText -split "`n")) {
    if ($sumLine.Length -eq 0) { continue }
    $parts = $sumLine -split '  ', 2
    $full = Join-Path $dist ($parts[1] -replace '/', '\')
    $actual = Get-LowerSha256 $full
    if ($actual -ne $parts[0]) {
        Write-Error "SHA256SUMS does not match the file"
        exit 1
    }
}

function Invoke-NativeChecked {
    param([scriptblock]$Command, [string]$Label)
    $saved = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $Command
    $code = $LASTEXITCODE
    $ErrorActionPreference = $saved
    if ($code -ne 0) {
        Write-Error ($Label + " failed")
        exit 1
    }
}

$gitleaks = Get-Command gitleaks -ErrorAction SilentlyContinue
if (-not $gitleaks) {
    Write-Error "gitleaks is not on PATH"
    exit 1
}
Invoke-NativeChecked -Label "gitleaks" -Command { gitleaks dir $dist --no-banner --redact }
Invoke-NativeChecked -Label "scan-repo" -Command { powershell.exe -NoProfile -File (Join-Path $repo "tools\scan-repo.ps1") -Root $dist }

Write-Output "make-release: ok"
exit 0
