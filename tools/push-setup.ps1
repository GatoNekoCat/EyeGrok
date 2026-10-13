# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Pushes a setup file to the headset and restarts the app. Never prints file contents.

param(
    [Parameter(Mandatory = $true)]
    [string]$File
)

$ErrorActionPreference = "Stop"
$Package = "io.github.eyegrok.ping"
$Remote = "/sdcard/Android/data/$Package/files/eyegrok-setup.json"

if (-not (Test-Path -LiteralPath $File)) {
    Write-Error "setup file not found"
    exit 1
}

& adb push $File $Remote
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& adb shell am force-stop $Package
& adb shell monkey -p $Package 1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$imported = $false
for ($i = 0; $i -lt 20; $i++) {
    # Windows PowerShell 5.1 with ErrorActionPreference Stop turns native stderr
    # into a terminating error. A missing file is the imported case.
    $savedPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $listing = & adb shell ls $Remote 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }
    $text = ""
    if ($null -ne $listing) { $text = ($listing | Out-String) }
    if ($code -ne 0 -or ($text -match "No such file")) {
        $imported = $true
        break
    }
    Start-Sleep -Seconds 1
}

if ($imported) {
    Write-Host "IMPORTED"
    Write-Host "Delete the local setup file."
    exit 0
}

Write-Host "NOT_IMPORTED"
Write-Host "Delete the local setup file."
exit 1
