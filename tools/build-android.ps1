# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Builds an EyeGrok Android APK. Unity is $env:UNITY_EDITOR, or the Hub install of 6000.3.26f1.

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("app", "probe-xr", "probe-flat")]
    [string]$Variant
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "unity"

function Test-EyeGrokModel {
    $pinPath = Join-Path $PSScriptRoot "models.sha256"
    if (-not (Test-Path -LiteralPath $pinPath)) {
        Write-Host "MODEL_CHECK=skipped pin file absent"
        return
    }

    $model = Join-Path $RepoRoot "unity\Assets\StreamingAssets\Whisper\ggml-tiny.en-q5_1.bin"
    if (-not (Test-Path -LiteralPath $model)) {
        Write-Error "model file missing"
        exit 1
    }

    $expected = $null
    foreach ($line in [System.IO.File]::ReadAllLines($pinPath)) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith("#")) { continue }
        $parts = $t -split "\s+"
        if ($parts[0] -match "^[0-9a-fA-F]{64}$") {
            $expected = $parts[0].ToLower()
            break
        }
    }
    if (-not $expected) {
        Write-Error "models.sha256 has no sha256"
        exit 1
    }

    $actual = (Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $expected) {
        Write-Error "model hash mismatch"
        exit 1
    }
    Write-Host "MODEL_CHECK=ok"
}

$unity = $env:UNITY_EDITOR
if (-not $unity) {
    $unity = "C:\Program Files\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe"
}
if (-not (Test-Path -LiteralPath $unity)) {
    Write-Error "Unity editor not found. Set UNITY_EDITOR to 6000.3.26f1."
    exit 1
}
if (-not (Test-Path -LiteralPath $Project)) {
    Write-Error "Unity project missing"
    exit 1
}

Test-EyeGrokModel

$outRel = switch ($Variant) {
    "app" { "Builds\EyeGrok-v001.apk" }
    "probe-xr" { "Builds\probes\EyeGrok-p0c-xr.apk" }
    "probe-flat" { "Builds\probes\EyeGrok-p0d-flat.apk" }
}
$outPath = Join-Path $RepoRoot $outRel
$outDir = Split-Path -Parent $outPath
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$logDir = Join-Path $RepoRoot ".local\logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir ("unity-build-" + $Variant + ".log")

Write-Host ("BUILD variant=" + $Variant)
# Unity.exe is a GUI-subsystem binary. The call operator returns before it exits.
$proc = Start-Process -FilePath $unity -ArgumentList @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $Project,
    "-buildTarget", "Android",
    "-executeMethod", "EyeGrok.Editor.BuildScript.BuildAndroid",
    "-eyegrokVariant", $Variant,
    "-eyegrokOut", $outPath,
    "-logFile", $logPath
) -Wait -PassThru
$code = $proc.ExitCode
if ($code -ne 0) {
    Write-Error ("Unity exited " + $code + ". Log: .local/logs/" + (Split-Path -Leaf $logPath))
    exit $code
}
if (-not (Test-Path -LiteralPath $outPath)) {
    Write-Error "Unity exited 0 but the APK is missing"
    exit 1
}
Write-Host ("APK " + $outRel)
exit 0
