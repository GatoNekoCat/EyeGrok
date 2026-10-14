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
$finalFlag = Join-Path $egHome "fake-final.txt"
$partFlag = Join-Path $egHome "fake-part.txt"
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

function Set-SetupFileState {
    if ($scenario -eq "partleft") {
        [System.IO.File]::WriteAllText($partFlag, "1", (New-Object System.Text.UTF8Encoding $false))
        if (Test-Path -LiteralPath $finalFlag) { Remove-Item -LiteralPath $finalFlag -Force }
        return
    }
    [System.IO.File]::WriteAllText($finalFlag, "1", (New-Object System.Text.UTF8Encoding $false))
    if (Test-Path -LiteralPath $partFlag) { Remove-Item -LiteralPath $partFlag -Force }
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
    if ($joined -match "eyegrok-setup\.json") { Set-SetupFileState }
    exit 0
}

if ($joined -match "(^| )devices( |$)") {
    Write-Output "List of devices attached"
    if ($scenario -eq "unauthorized") {
        Write-Output "R3A6 unauthorized"
    } elseif ($scenario -eq "nolepton") {
        Write-Output "R3A6 device product:frame model:Frame device:frame"
    } elseif ($scenario -eq "nodevice") {
    } elseif ($scenario -eq "wifihappy") {
        Write-Output "frame:5555 device product:frame model:Frame device:frame"
    } elseif ($scenario -eq "wifinohost") {
        $prior = ""
        if (Test-Path -LiteralPath $log) { $prior = [System.IO.File]::ReadAllText($log) }
        if ($prior.Contains("connect 192.168.4.80:5555")) {
            Write-Output "192.168.4.80:5555 device product:frame model:Frame device:frame"
        }
    } elseif ($scenario -eq "wifirefused" -or $scenario -eq "wifitimeout") {
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
    $isPart = $joined -match "eyegrok-setup\.json\.part"
    $flag = $finalFlag
    $shown = "/sdcard/Android/data/io.github.eyegrok.ping/files/eyegrok-setup.json"
    if ($isPart) {
        $flag = $partFlag
        $shown = $shown + ".part"
    }
    if (Test-Path -LiteralPath $flag) {
        Write-Output $shown
        exit 0
    }
    Write-Output "ls: No such file or directory"
    exit 1
}

if ($joined -match "(^| )am( |$)" -and $joined -match "(^| )start( |$)") {
    if ($scenario -eq "happy") {
        if (Test-Path -LiteralPath $finalFlag) { Remove-Item -LiteralPath $finalFlag -Force }
        if (Test-Path -LiteralPath $partFlag) { Remove-Item -LiteralPath $partFlag -Force }
    }
    exit 0
}

if ($joined -match "(^| )connect( |$)") {
    $target = ""
    if ($args.Count -gt 0) { $target = [string]$args[$args.Count - 1] }
    if ($scenario -eq "wifihappy" -and $target -eq "frame:5555") {
        Write-Output "connected to frame:5555"
        exit 0
    }
    if ($scenario -eq "wifinohost" -and $target -eq "frame:5555") {
        Write-Output "cannot resolve host 'frame' and port 5555: No such host is known. (11001)"
        exit 1
    }
    if ($scenario -eq "wifinohost" -and $target -eq "192.168.4.80:5555") {
        Write-Output "connected to 192.168.4.80:5555"
        exit 0
    }
    if ($scenario -eq "wifirefused") {
        Write-Output ("cannot connect to " + $target + ": No connection could be made because the target machine actively refused it. (10061)")
        exit 1
    }
    if ($scenario -eq "wifitimeout") {
        Write-Output ("cannot connect to " + $target + ": A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond. (10060)")
        exit 1
    }
    exit 0
}

exit 0
