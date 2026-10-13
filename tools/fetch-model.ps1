# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Downloads the pinned Whisper model and checks SHA-256. A mismatch deletes the file and exits 1.

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DestDir = Join-Path $RepoRoot "unity\Assets\StreamingAssets\Whisper"
$Dest = Join-Path $DestDir "ggml-tiny.en-q5_1.bin"
$PinPath = Join-Path $PSScriptRoot "models.sha256"
$Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en-q5_1.bin"

function Get-PinnedHash {
    foreach ($line in [System.IO.File]::ReadAllLines($PinPath)) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith("#")) { continue }
        $parts = $t -split "\s+"
        if ($parts[0] -match "^[0-9a-fA-F]{64}$") { return $parts[0].ToLower() }
    }
    return $null
}

function Remove-Model {
    if (Test-Path -LiteralPath $Dest) {
        Remove-Item -LiteralPath $Dest -Force
    }
}

$expected = Get-PinnedHash
if (-not $expected) {
    Write-Error "models.sha256 has no sha256"
    exit 1
}

New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

if (-not (Test-Path -LiteralPath $Dest)) {
    Write-Host "MODEL_FETCH=start"
    & curl.exe -L --fail --retry 3 --output $Dest $Url
    if ($LASTEXITCODE -ne 0) {
        Remove-Model
        Write-Error "download failed"
        exit 1
    }
}

$actual = (Get-FileHash -LiteralPath $Dest -Algorithm SHA256).Hash.ToLower()
if ($actual -ne $expected) {
    Remove-Model
    Write-Error "model hash mismatch"
    exit 1
}

Write-Host "MODEL_CHECK=ok"
exit 0
