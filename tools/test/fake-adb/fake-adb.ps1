# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Test shim. Scenario comes from EYEGROK_FAKE_ADB_SCENARIO.
# Records arguments only. Stdin is hashed and discarded.

$ErrorActionPreference = "Continue"
$egHome = $env:EYEGROK_HOME
if (-not $egHome) { $egHome = Join-Path $env:TEMP "eyegrok-fake-adb" }
New-Item -ItemType Directory -Force -Path $egHome | Out-Null
$scenario = $env:EYEGROK_FAKE_ADB_SCENARIO
if (-not $scenario) { $scenario = "nodevice" }
$log = Join-Path $egHome "fake-adb-calls.txt"
$line = ($args -join " ")
[System.IO.File]::AppendAllText($log, $line + "`n", (New-Object System.Text.UTF8Encoding $false))
$joined = $args -join " "

function Read-StdinBytes {
    $stdin = [Console]::OpenStandardInput()
    $buffer = New-Object byte[] 8192
    $all = New-Object System.IO.MemoryStream
    while ($true) {
        $n = $stdin.Read($buffer, 0, $buffer.Length)
        if ($n -le 0) { break }
        $all.Write($buffer, 0, $n)
    }
    return $all.ToArray()
}

if ($joined -match "exec-in" -or ($joined -match "shell" -and $joined -match "cat >")) {
    $bytes = Read-StdinBytes
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLower()
    } finally {
        $sha.Dispose()
    }
    Write-Output ("STDIN_SHA256=" + $hash)
    if ($scenario -eq "happy" -and $joined -match "eyegrok-setup.json") {
        [System.IO.File]::WriteAllText((Join-Path $egHome "fake-imported.txt"), "1")
    }
    exit 0
}

if ($joined -match "(^| )devices( |$)") {
    Write-Output "List of devices attached"
    if ($scenario -eq "unauthorized") {
        Write-Output "R3A6 unauthorized"
    } elseif ($scenario -eq "nolepton") {
        Write-Output "R3A6 device product:frame model:Frame device:frame"
    } elseif ($scenario -eq "nodevice") {
    } else {
        Write-Output "localhost:5555 device product:frame model:Frame device:frame"
    }
    exit 0
}

if ($joined -match "(^| )install( |$)") {
    if ($scenario -eq "installfail") {
        Write-Output "Failure [INSUFFICIENT_STORAGE]"
        exit 1
    }
    Write-Output "Success"
    exit 0
}

if ($joined -match "(^| )ls( |$)") {
    $flag = Join-Path $egHome "fake-imported.txt"
    if ((Test-Path -LiteralPath $flag) -or $scenario -eq "nodevice") {
        Write-Output "ls: No such file or directory"
        exit 1
    }
    Write-Output "/sdcard/Android/data/io.github.eyegrok.ping/files/eyegrok-setup.json"
    exit 0
}

exit 0
