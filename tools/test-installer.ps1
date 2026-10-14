# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Headless installer proof. Values stay off the command line. Transcripts saved redacted.

$ErrorActionPreference = "Stop"
function Write-Trace([string]$Msg) {
    $line = (Get-Date).ToString("HH:mm:ss.fff") + " " + $Msg + "`r`n"
    [System.IO.File]::AppendAllText((Join-Path $env:TEMP "eyegrok-proof-trace.txt"), $line)
}
Write-Trace "script-start"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Proof = Join-Path $RepoRoot ".local\proof"
New-Item -ItemType Directory -Force -Path $Proof | Out-Null
$Setup = Join-Path $RepoRoot "installer\EyeGrok-Setup.ps1"
$Fake = Join-Path $RepoRoot "tools\test\fake-adb\adb.cmd"
$Pwsh = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"

function Invoke-Silent {
    param(
        [string]$File,
        [string]$Arguments,
        [hashtable]$Environment
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    if ($Environment) {
        foreach ($key in @($Environment.Keys)) {
            $psi.EnvironmentVariables[[string]$key] = [string]$Environment[$key]
        }
    }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    try {
        [void]$proc.Start()
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit(8000)) { try { $proc.Kill() } catch { } }
        [void]$outTask.Wait(2000)
        [void]$errTask.Wait(2000)
    } catch { }
}

function Add-HarnessTree {
    param(
        [int]$RootPid,
        [System.Collections.Generic.List[int]]$Tracked,
        [System.Collections.Generic.HashSet[int]]$Seen
    )
    if ($RootPid -le 0) { return }
    $rows = @(Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue)
    $kids = @{}
    foreach ($row in $rows) {
        $parent = 0
        $procId = 0
        try {
            $parent = [int]$row.ParentProcessId
            $procId = [int]$row.ProcessId
        } catch {
            continue
        }
        if (-not $kids.ContainsKey($parent)) {
            $kids[$parent] = New-Object System.Collections.Generic.List[int]
        }
        [void]$kids[$parent].Add($procId)
    }
    $pending = New-Object System.Collections.Generic.Queue[int]
    $pending.Enqueue($RootPid)
    while ($pending.Count -gt 0) {
        $id = [int]$pending.Dequeue()
        if ($id -le 0 -or $id -eq $PID) { continue }
        if ($Seen.Contains($id)) { continue }
        [void]$Seen.Add($id)
        [void]$Tracked.Add($id)
        if ($kids.ContainsKey($id)) {
            foreach ($child in $kids[$id]) { $pending.Enqueue([int]$child) }
        }
    }
}

function Stop-HarnessPids {
    param([System.Collections.Generic.List[int]]$Tracked)
    if ($null -eq $Tracked) { return }
    for ($i = $Tracked.Count - 1; $i -ge 0; $i--) {
        $id = [int]$Tracked[$i]
        if ($id -le 0 -or $id -eq $PID) { continue }
        try {
            $target = Get-Process -Id $id -ErrorAction SilentlyContinue
            if ($null -ne $target) { $target.Kill() }
        } catch { }
    }
}

function Get-FreePrivateAdbPort {
    $rng = New-Object System.Random
    $used = @{}
    try {
        $listeners = @([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners())
        foreach ($ep in $listeners) {
            try { $used[[int]$ep.Port] = $true } catch { }
        }
    } catch { }
    for ($try = 0; $try -lt 40; $try++) {
        $port = $rng.Next(20000, 30000)
        if ($used.ContainsKey($port)) { continue }
        $probe = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
        try {
            $probe.Start()
            $probe.Stop()
            return $port
        } catch {
        } finally {
            try { $probe.Stop() } catch { }
        }
    }
    throw "no free adb port"
}

function Stop-PrivateAdb {
    param([string]$AdbExe, [string]$Port)
    if (-not $Port) { return }
    $n = 0
    if (-not [int]::TryParse($Port, [ref]$n)) { return }
    if ($n -lt 20000 -or $n -gt 29999) { return }
    if (-not (Test-Path -LiteralPath $AdbExe)) { return }
    Invoke-Silent -File $AdbExe -Arguments "kill-server" -Environment @{ ANDROID_ADB_SERVER_PORT = "$n" }
}

function ConvertTo-Redacted([string]$Text, [string[]]$Needles) {
    $clean = $Text
    if ($Needles) {
        foreach ($needle in $Needles) {
            if ($needle) { $clean = $clean.Replace($needle, "[REDACTED]") }
        }
    }
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($clean -split "`n")) {
        $trim = $line.TrimEnd()
        if ($trim.StartsWith("CAPTURE ")) { continue }
        [void]$kept.Add($trim)
    }
    return ($kept -join "`n")
}

function Save-Proof([string]$Name, [string]$Text, [string[]]$Needles) {
    $path = Join-Path $Proof $Name
    $body = ConvertTo-Redacted $Text $Needles
    [System.IO.File]::WriteAllText($path, $body + "`n", (New-Object System.Text.UTF8Encoding $false))
}

function Get-FilteredPath {
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($p in ($env:PATH -split ';')) {
        if (-not $p) { continue }
        if (Test-Path -LiteralPath (Join-Path $p "adb.exe")) { continue }
        [void]$parts.Add($p)
    }
    return ($parts -join ';')
}

function Invoke-SetupChild {
    param(
        [string]$EgHome,
        [string]$PathValue,
        [string]$Adb,
        [string]$Scenario,
        [string[]]$Arguments,
        [string]$Stdin,
        [string]$AdbServerPort
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Pwsh
    $argText = "-NoProfile -ExecutionPolicy Bypass -File `"$Setup`""
    foreach ($arg in $Arguments) { $argText = $argText + " " + $arg }
    $psi.Arguments = $argText
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.CreateNoWindow = $true
    if ($PathValue) { $psi.EnvironmentVariables["PATH"] = $PathValue }
    if ($EgHome) { $psi.EnvironmentVariables["EYEGROK_HOME"] = $EgHome }
    if ($Adb) { $psi.EnvironmentVariables["EYEGROK_ADB"] = $Adb } else { $psi.EnvironmentVariables.Remove("EYEGROK_ADB") }
    if ($Scenario) { $psi.EnvironmentVariables["EYEGROK_FAKE_ADB_SCENARIO"] = $Scenario }
    if ($AdbServerPort) { $psi.EnvironmentVariables["ANDROID_ADB_SERVER_PORT"] = $AdbServerPort }
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    if ($Stdin) {
        $proc.StandardInput.WriteLine($Stdin)
    }
    $proc.StandardInput.Close()
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $tracked = New-Object System.Collections.Generic.List[int]
    $seen = New-Object "System.Collections.Generic.HashSet[int]"
    $exited = $false
    if ($AdbServerPort) {
        Add-HarnessTree ([int]$proc.Id) $tracked $seen
        $deadline = (Get-Date).AddMilliseconds(240000)
        while ((Get-Date) -lt $deadline) {
            if ($proc.WaitForExit(1500)) { $exited = $true; break }
            Add-HarnessTree ([int]$proc.Id) $tracked $seen
        }
    } else {
        $exited = $proc.WaitForExit(240000)
        if ([int]$proc.Id -ne $PID) {
            [void]$seen.Add([int]$proc.Id)
            [void]$tracked.Add([int]$proc.Id)
        }
    }
    if (-not $exited) {
        if ($AdbServerPort) { Add-HarnessTree ([int]$proc.Id) $tracked $seen }
        Stop-HarnessPids $tracked
        Invoke-Silent -File "$env:SystemRoot\System32\taskkill.exe" -Arguments ("/PID " + $proc.Id + " /T /F")
        if ($AdbServerPort -and $EgHome) {
            Stop-PrivateAdb -AdbExe (Join-Path $EgHome "platform-tools\adb.exe") -Port $AdbServerPort
        }
        $proc.WaitForExit(5000) | Out-Null
    }
    if (-not $stdoutTask.Wait(5000)) {
        if ($AdbServerPort) { Add-HarnessTree ([int]$proc.Id) $tracked $seen }
        Stop-HarnessPids $tracked
        if ($AdbServerPort -and $EgHome) {
            Stop-PrivateAdb -AdbExe (Join-Path $EgHome "platform-tools\adb.exe") -Port $AdbServerPort
        }
        [void]$stdoutTask.Wait(5000)
    }
    if (-not $stderrTask.Wait(5000)) { [void]$stderrTask.Wait(3000) }
    $stdout = ""
    $stderr = ""
    if ($stdoutTask.IsCompleted) { $stdout = [string]$stdoutTask.Result }
    if ($stderrTask.IsCompleted) { $stderr = [string]$stderrTask.Result }
    $code = 124
    if ($proc.HasExited) {
        try { $code = $proc.ExitCode } catch { $code = 124 }
    }
    return @{ Code = $code; Stdout = $stdout; Stderr = $stderr }
}

function Assert-Contains([string]$Text, [string]$Expected, [string]$Label) {
    if ($Text.IndexOf($Expected) -lt 0) { throw ("missing " + $Label) }
}

function New-WorkHome([string]$Name) {
    $dir = Join-Path $env:TEMP ("eyegrok-proof-" + $Name + "-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

function Remove-WorkHome([string]$Dir) {
    if ($Dir -and (Test-Path -LiteralPath $Dir)) {
        Remove-Item -LiteralPath $Dir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-Latin1Encoding {
    # Windows PowerShell 5.1 exposes Encoding.Latin1 as null. Code page 28591 is the same 1:1 byte map.
    $latin1 = $null
    try { $latin1 = [System.Text.Encoding]::Latin1 } catch { $latin1 = $null }
    if ($null -eq $latin1) { $latin1 = [System.Text.Encoding]::GetEncoding(28591) }
    return $latin1
}

function Test-SentinelFile([System.IO.FileInfo]$Info, [string[]]$Patterns, $Latin, [int64]$MaxBytes, [bool]$ApplySince, [datetime]$Since) {
    if ($Info.Name -eq ".dev.vars") { return $false }
    if (($Info.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
    if ($Info.Length -gt $MaxBytes) { return $false }
    if ($ApplySince -and ($Info.LastWriteTime -lt $Since)) { return $false }
    $bytes = $null
    try { $bytes = [System.IO.File]::ReadAllBytes($Info.FullName) } catch { return $false }
    if ($null -eq $bytes -or $bytes.Length -eq 0) { return $false }
    $text = $Latin.GetString($bytes)
    foreach ($pat in $Patterns) {
        if ($pat.Length -gt 0 -and $text.IndexOf($pat, [System.StringComparison]::Ordinal) -ge 0) { return $true }
    }
    return $false
}

function Find-SentinelInTree([string]$Root, [string]$Needle, [string[]]$SkipNames, [datetime]$Since) {
    if (-not $Needle) { return $null }
    if (-not (Test-Path -LiteralPath $Root)) { return $null }
    $latin1 = Get-Latin1Encoding
    $utf8 = [System.Text.Encoding]::UTF8.GetBytes($Needle)
    $utf16 = [System.Text.Encoding]::Unicode.GetBytes($Needle)
    $b64 = [System.Text.Encoding]::ASCII.GetBytes([Convert]::ToBase64String($utf8))
    $b16 = [System.Text.Encoding]::ASCII.GetBytes([Convert]::ToBase64String($utf16))
    $pats = @(
        $latin1.GetString($utf8),
        $latin1.GetString($utf16),
        $latin1.GetString($b64),
        $latin1.GetString($b16)
    )
    $skip = New-Object "System.Collections.Generic.HashSet[string]"
    foreach ($name in @(".git", "Library", "node_modules", "Builds", ".wrangler", ".local", "Temp", "obj")) {
        [void]$skip.Add($name.ToLowerInvariant())
    }
    if ($SkipNames) {
        foreach ($name in $SkipNames) {
            if ($name) { [void]$skip.Add($name.ToLowerInvariant()) }
        }
    }
    $maxBytes = [int64]2MB
    $applySince = $Since -gt ([datetime]"2000-01-01")
    $reparse = [System.IO.FileAttributes]::ReparsePoint
    $dirFlag = [System.IO.FileAttributes]::Directory
    $rootAttr = [System.IO.FileAttributes]::Normal
    try { $rootAttr = [System.IO.File]::GetAttributes($Root) } catch { return $null }
    if (($rootAttr -band $dirFlag) -eq 0) {
        $one = New-Object System.IO.FileInfo $Root
        if (Test-SentinelFile $one $pats $latin1 $maxBytes $applySince $Since) { return $one.FullName }
        return $null
    }
    $pending = New-Object System.Collections.Generic.Queue[string]
    $seen = New-Object "System.Collections.Generic.HashSet[string]"
    $pending.Enqueue($Root)
    while ($pending.Count -gt 0) {
        $dir = $pending.Dequeue()
        $dirFull = $dir
        try { $dirFull = [System.IO.Path]::GetFullPath($dir) } catch { continue }
        $dirKey = $dirFull.ToLowerInvariant()
        if ($seen.Contains($dirKey)) { continue }
        [void]$seen.Add($dirKey)
        $dirInfo = New-Object System.IO.DirectoryInfo $dirFull
        if (-not $dirInfo.Exists) { continue }
        try {
            foreach ($info in $dirInfo.EnumerateFileSystemInfos()) {
                try {
                    $attr = $info.Attributes
                    $isDir = ($attr -band $dirFlag) -ne 0
                    if ($isDir) {
                        if (($attr -band $reparse) -ne 0) { continue }
                        if ($skip.Contains($info.Name.ToLowerInvariant())) { continue }
                        $pending.Enqueue($info.FullName)
                        continue
                    }
                    $fileInfo = $info
                    if ($fileInfo -isnot [System.IO.FileInfo]) { $fileInfo = New-Object System.IO.FileInfo $info.FullName }
                    if (Test-SentinelFile $fileInfo $pats $latin1 $maxBytes $applySince $Since) { return $fileInfo.FullName }
                } catch { continue }
            }
        } catch [System.UnauthorizedAccessException] {
            continue
        } catch [System.IO.IOException] {
            continue
        }
    }
    return $null
}

function New-Sentinel([int]$RandomChars) {
    $alphabet = ("abcdefghijklmnop" + "qrstuvwxyz0123456789").ToCharArray()
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $buf = New-Object byte[] $RandomChars
        $rng.GetBytes($buf)
        $sb = New-Object System.Text.StringBuilder
        [void]$sb.Append("EGSENT")
        foreach ($b in $buf) { [void]$sb.Append($alphabet[$b % $alphabet.Length]) }
        return $sb.ToString()
    } finally {
        $rng.Dispose()
    }
}

function New-RandomToken([int]$Count) {
    $alphabet = ("abcdefghijklmnop" + "qrstuvwxyz0123456789").ToCharArray()
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $buf = New-Object byte[] $Count
        $rng.GetBytes($buf)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $buf) { [void]$sb.Append($alphabet[$b % $alphabet.Length]) }
        return $sb.ToString()
    } finally {
        $rng.Dispose()
    }
}

function ConvertFrom-HexBytes([string]$Hex) {
    $count = $Hex.Length / 2
    $bytes = New-Object byte[] $count
    for ($i = 0; $i -lt $count; $i++) {
        $bytes[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16)
    }
    return ,$bytes
}

function Invoke-RawHttp([string]$Method, [string]$Url, [string]$Token) {
    $request = [System.Net.HttpWebRequest]::Create($Url)
    $request.Method = $Method
    $request.Timeout = 20000
    $request.AllowAutoRedirect = $false
    if ($Token) { $request.Headers["Authorization"] = "Bearer " + $Token }
    $response = $null
    try {
        $response = $request.GetResponse()
    } catch [System.Net.WebException] {
        $response = $_.Exception.Response
        if ($null -eq $response) { return @{ Status = 0; Body = "" } }
    }
    $status = [int]$response.StatusCode
    $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
    $text = $reader.ReadToEnd()
    $reader.Close()
    $response.Close()
    return @{ Status = $status; Body = $text }
}

function Test-PortOpen([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect("127.0.0.1", $Port)
        return $true
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

function Start-ProofRelay([string]$VarText) {
    $relayDir = Join-Path $RepoRoot "relay"
    $devVars = Join-Path $relayDir ".dev.vars"
    [System.IO.File]::WriteAllText($devVars, $VarText, (New-Object System.Text.UTF8Encoding $false))
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "$env:ComSpec"
    $psi.Arguments = "/d /c npx wrangler dev --config wrangler.example.toml --port 8787 --ip 127.0.0.1 --local"
    $psi.WorkingDirectory = $relayDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables["WRANGLER_SEND_METRICS"] = "false"
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $ignore = { }
    Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived -Action $ignore | Out-Null
    Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived -Action $ignore | Out-Null
    $proc.BeginOutputReadLine()
    $proc.BeginErrorReadLine()
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        $health = Invoke-RawHttp "GET" "http://127.0.0.1:8787/health" $null
        if ($health.Status -eq 200 -and $health.Body.Trim() -eq "ok") { return $proc }
        Start-Sleep -Milliseconds 400
    }
    Stop-ProofRelay $proc
    throw "relay did not answer"
}

function Stop-CmdTree([int]$CmdPid) {
    if ($CmdPid -le 0 -or $CmdPid -eq $PID) { return }
    Invoke-Silent -File "$env:SystemRoot\System32\taskkill.exe" -Arguments ("/PID " + $CmdPid + " /T /F")
}

function Wait-ProcExit($Proc, [int]$TimeoutMs) {
    if ($null -eq $Proc) { return $false }
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        try { if ($Proc.HasExited) { return $true } } catch { return $false }
        Start-Sleep -Milliseconds 200
    }
    try { return [bool]$Proc.HasExited } catch { return $false }
}

function Stop-ProofRelay($Proc) {
    if ($null -eq $Proc) { return }
    $id = 0
    try { $id = [int]$Proc.Id } catch { return }
    Stop-CmdTree $id
    [void](Wait-ProcExit $Proc 5000)
    try { $Proc.CancelOutputRead() } catch { }
    try { $Proc.CancelErrorRead() } catch { }
}

function Clear-RelayLocal {
    $devVars = Join-Path $RepoRoot "relay\.dev.vars"
    $state = Join-Path $RepoRoot "relay\.wrangler"
    if (Test-Path -LiteralPath $devVars) { Remove-Item -LiteralPath $devVars -Force -ErrorAction SilentlyContinue }
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path -LiteralPath $state)) { break }
        Remove-Item -LiteralPath $state -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path -LiteralPath $state)) { break }
        Start-Sleep -Milliseconds 300
    }
    if (Test-Path -LiteralPath $devVars) { return $true }
    if (Test-Path -LiteralPath $state) { return $true }
    return $false
}

function Assert-BoxEquals([byte[]]$Id, [byte[]]$Enc, [byte[]]$MacKey, [string]$Expected, [string]$Relay) {
    $idText = ConvertTo-EyeGrokB64Url $Id
    $got = Invoke-RawHttp "GET" ($Relay.TrimEnd("/") + "/pair/" + $idText) $null
    if ($got.Status -ne 200) { throw "pair get" }
    $parsed = $got.Body | ConvertFrom-Json
    $plain = Unprotect-EyeGrokPlain -InitVector (ConvertFrom-EyeGrokB64Url ([string]$parsed.box.iv)) -Cipher (ConvertFrom-EyeGrokB64Url ([string]$parsed.box.ct)) -Mac (ConvertFrom-EyeGrokB64Url ([string]$parsed.box.mac)) -EncKey $Enc -MacKey $MacKey -Id $Id
    if ($null -eq $plain) { throw "pair mac" }
    $text = [System.Text.Encoding]::UTF8.GetString($plain)
    if ($text -ne $Expected) { throw "pair plain" }
    return $idText
}

$fail = 0
$notes = New-Object System.Collections.Generic.List[string]

$filtered = Get-FilteredPath
$licHome = New-WorkHome "lic"
try {
    $lic = Invoke-SetupChild -EgHome $licHome -PathValue $filtered -Arguments @("-Headless", "-Mode", "frame")
    Save-Proof "i2-license.txt" ($lic.Stdout + "`n" + $lic.Stderr) $null
    if ($lic.Code -eq 0) { throw "license exit 0" }
    Assert-Contains $lic.Stdout "E_LICENSE_NOT_ACCEPTED" "license key"
    Assert-Contains $lic.Stdout "https://developer.android.com/tools/releases/platform-tools" "license url"
    if (Test-Path -LiteralPath (Join-Path $licHome "dl")) { throw "license created dl" }
    [void]$notes.Add("AT-I2 PASS")
} catch {
    $fail = 1
    [void]$notes.Add("AT-I2 FAIL")
} finally {
    Remove-WorkHome $licHome
}
Write-Trace "after i2"

$dummy = Join-Path $env:TEMP ("eyegrok-dummy-" + [guid]::NewGuid().ToString("N") + ".apk")
[System.IO.File]::WriteAllText($dummy, "dummy")
$hookKey = "0123456789abcdef0123456789abcdef"
$readKey = "fedcba9876543210fedcba9876543210"
$usbJson = '{"webhookUrl":"https://example.com/hook","webhookKey":"' + $hookKey + '","relayUrl":"https://example.com/relay","relayReadKey":"' + $readKey + '"}'
$cases = @(
    @{ Name = "nodevice"; Mode = "frame"; Extra = @(); Stdin = $null; Key = "E_NO_FRAME"; Text = "No Frame found." },
    @{ Name = "unauthorized"; Mode = "frame"; Extra = @(); Stdin = $null; Key = "E_UNAUTHORIZED"; Text = "has not allowed this PC" },
    @{ Name = "nolepton"; Mode = "frame"; Extra = @(); Stdin = $null; Key = "E_NO_LEPTON"; Text = "Lepton Development is not running" },
    @{ Name = "installfail"; Mode = "full"; Extra = @("-PairMode", "usb", "-ApkPath", $dummy); Stdin = $usbJson; Key = "E_INSTALL"; Text = "INSUFFICIENT_STORAGE" },
    @{ Name = "importfail"; Mode = "full"; Extra = @("-PairMode", "usb", "-ApkPath", $dummy); Stdin = $usbJson; Key = "E_IMPORT"; Text = "did not take the setup" }
)
foreach ($case in $cases) {
    $caseHome = New-WorkHome $case.Name
    try {
        $args = @("-Headless", "-Mode", $case.Mode) + $case.Extra
        $run = Invoke-SetupChild -EgHome $caseHome -PathValue $filtered -Adb $Fake -Scenario $case.Name -Arguments $args -Stdin $case.Stdin
        Save-Proof ("i3-" + $case.Name + ".txt") ($run.Stdout + "`n" + $run.Stderr) @($hookKey, $readKey)
        if ($run.Code -eq 0) { throw "exit 0" }
        Assert-Contains $run.Stdout $case.Key "key"
        Assert-Contains $run.Stdout $case.Text "text"
        [void]$notes.Add("AT-I3 " + $case.Name + " PASS")
    } catch {
        $fail = 1
        [void]$notes.Add("AT-I3 " + $case.Name + " FAIL")
    } finally {
        Remove-WorkHome $caseHome
    }
}
Write-Trace "after i3"

$shortJson = '{"webhookUrl":"https://example.com/hook","webhookKey":"0123456789abcdef","relayUrl":"https://example.com/relay","relayReadKey":"' + $readKey + '"}'
$launcher = "io.github.eyegrok.ping/com.unity3d.player.UnityPlayerActivity"
$sliceCases = @(
    @{ Id = "AT-B1"; Name = "happy"; Stdin = $usbJson; Ok = $true; NeedImport = $true },
    @{ Id = "AT-B2"; Name = "partleft"; Stdin = $usbJson; Ok = $false; Key = "E_IMPORT_FAILED"; Text = "was not renamed" },
    @{ Id = "AT-B3"; Name = "notconsumed"; Stdin = $usbJson; Ok = $false; Key = "E_IMPORT"; Text = "did not take the setup" },
    @{ Id = "AT-B4"; Name = "happy"; Stdin = $usbJson; Ok = $true; NeedAm = $true },
    @{ Id = "AT-B5"; Name = "happy"; Stdin = $shortJson; Ok = $false; Key = "BAD values"; Text = "The webhook key must be at least 32 characters." }
)
foreach ($case in $sliceCases) {
    $caseHome = New-WorkHome $case.Id
    try {
        $setupArgs = @("-Headless", "-PairMode", "usb", "-ApkPath", $dummy)
        $run = Invoke-SetupChild -EgHome $caseHome -PathValue $filtered -Adb $Fake -Scenario $case.Name -Arguments $setupArgs -Stdin $case.Stdin
        Save-Proof ($case.Id + ".txt") ($run.Stdout + "`n" + $run.Stderr) @($hookKey, $readKey, "0123456789abcdef")
        $calls = ""
        $callPath = Join-Path $caseHome "fake-adb-calls.txt"
        if (Test-Path -LiteralPath $callPath) { $calls = [System.IO.File]::ReadAllText($callPath) }
        if ($case.Ok) {
            if ($run.Code -ne 0) { throw "exit" }
            if ($run.Stdout.IndexOf("Imported") -lt 0) { throw "imported" }
            if ($run.Stdout.IndexOf("`nOK") -lt 0 -and -not $run.Stdout.TrimEnd().EndsWith("OK")) { throw "ok" }
        } else {
            if ($run.Code -eq 0) { throw "exit 0" }
            Assert-Contains $run.Stdout ([string]$case.Key) "key"
            Assert-Contains $run.Stdout ([string]$case.Text) "text"
            if ($run.Stdout.IndexOf("Imported") -ge 0) { throw "imported" }
        }
        if ($case.NeedImport) {
            if ($run.Stdout.IndexOf("STEP import") -lt 0) { throw "step import" }
        }
        if ($case.NeedAm) {
            if ($calls.IndexOf("am start -n " + $launcher) -lt 0) { throw "am start" }
            if ($calls.ToLowerInvariant().IndexOf("monkey") -ge 0) { throw "monkey" }
        }
        [void]$notes.Add($case.Id + " PASS")
    } catch {
        $fail = 1
        [void]$notes.Add($case.Id + " FAIL " + [string]$_.Exception.Message)
    } finally {
        Remove-WorkHome $caseHome
    }
}
Write-Trace "after b"

function Get-CallLog([string]$WorkHome) {
    $path = Join-Path $WorkHome "fake-adb-calls.txt"
    if (-not (Test-Path -LiteralPath $path)) { return "" }
    return [System.IO.File]::ReadAllText($path)
}

$wifiCases = @(
    @{ Id = "AT-W1"; Name = "wifihappy"; Extra = @(); Stdin = $null; Ip = $null; Ok = $true },
    @{ Id = "AT-W2"; Name = "wifinohost"; Extra = @("-FramePrompt"); Stdin = "192.168.4.80"; Ip = "192.168.4.80"; Ok = $true },
    @{ Id = "AT-W3"; Name = "wifirefused"; Extra = @(); Stdin = $null; Ip = $null; Ok = $false; Key = "E_LEPTON_NOT_RUNNING"; Text = "Lepton Development from the Steam Library" },
    @{ Id = "AT-W4"; Name = "wifitimeout"; Extra = @(); Stdin = $null; Ip = $null; Ok = $false; Key = "E_WIFI_UNREACHABLE"; Text = "client isolation" },
    @{ Id = "AT-W5"; Name = "wifinohost"; Extra = @("-FramePrompt"); Stdin = "10.0.0.256"; Ip = "10.0.0.256"; Ok = $false; Key = "E_BAD_IP"; Text = "not an IPv4" },
    @{ Id = "AT-W6"; Name = "happy"; Extra = @("-FrameUsb"); Stdin = $null; Ip = $null; Ok = $true }
)
foreach ($case in $wifiCases) {
    $caseHome = New-WorkHome $case.Id
    try {
        $setupArgs = @("-Headless", "-Mode", "frame") + $case.Extra
        $run = Invoke-SetupChild -EgHome $caseHome -PathValue $filtered -Adb $Fake -Scenario $case.Name -Arguments $setupArgs -Stdin $case.Stdin
        $needles = @()
        if ($case.Ip) { $needles = @([string]$case.Ip) }
        Save-Proof ($case.Id + ".txt") ($run.Stdout + "`n" + $run.Stderr) $needles
        $calls = Get-CallLog $caseHome
        $logPath = Join-Path $caseHome "logs\installer.log"
        $logged = ""
        if (Test-Path -LiteralPath $logPath) { $logged = [System.IO.File]::ReadAllText($logPath) }
        if ($case.Ip) {
            if ($run.Stdout.IndexOf([string]$case.Ip) -ge 0) { throw "stdout ip" }
            if ($run.Stderr.IndexOf([string]$case.Ip) -ge 0) { throw "stderr ip" }
            if ($logged.IndexOf([string]$case.Ip) -ge 0) { throw "log ip" }
        }
        if ($case.Id -eq "AT-W1") {
            if ($calls.IndexOf("connect frame:5555") -lt 0) { throw "connect frame" }
            if ($calls.IndexOf("devices") -lt 0) { throw "devices" }
            if ($calls.IndexOf("forward tcp:5555") -ge 0) { throw "usb on wifi" }
        }
        if ($case.Id -eq "AT-W2") {
            if ($run.Stdout.IndexOf("PROMPT ip") -lt 0) { throw "prompt" }
            $hostAt = $calls.IndexOf("connect frame:5555")
            $ipAt = $calls.IndexOf("connect 192.168.4.80:5555")
            if ($hostAt -lt 0 -or $ipAt -lt 0 -or $ipAt -le $hostAt) { throw "connect order" }
            if ($calls.IndexOf("`ndevices") -lt 0 -and $calls.IndexOf("devices") -lt 0) { throw "devices" }
        }
        if ($case.Id -eq "AT-W4") {
            if ($run.Stdout.IndexOf("hostname frame") -lt 0) { throw "duplicate hostname" }
        }
        if ($case.Id -eq "AT-W5") {
            if ($run.Stdout.IndexOf("PROMPT ip") -lt 0) { throw "prompt" }
            if ($calls.IndexOf("10.0.0.256") -ge 0) { throw "bad ip was dialed" }
            if ($calls.IndexOf("connect frame:5555") -lt 0) { throw "connect frame" }
        }
        if ($case.Id -eq "AT-W6") {
            if ($calls.IndexOf("forward tcp:5555 tcp:5555") -lt 0) { throw "forward" }
            if ($calls.IndexOf("connect localhost:5555") -lt 0) { throw "usb connect" }
        }
        if ($case.Ok) {
            if ($run.Code -ne 0) { throw "exit" }
            if ($run.Stdout.IndexOf("`nOK") -lt 0 -and -not $run.Stdout.TrimEnd().EndsWith("OK")) { throw "ok" }
        } else {
            if ($run.Code -eq 0) { throw "exit 0" }
            Assert-Contains $run.Stdout ([string]$case.Key) "key"
            Assert-Contains $run.Stdout ([string]$case.Text) "text"
        }
        [void]$notes.Add($case.Id + " PASS")
    } catch {
        $fail = 1
        $msg = [string]$_.Exception.Message
        if ($case.Ip) { $msg = $msg.Replace([string]$case.Ip, "[REDACTED]") }
        [void]$notes.Add($case.Id + " FAIL " + $msg)
    } finally {
        Remove-WorkHome $caseHome
    }
}
Write-Trace "after wifi"

$scripts = @(
    "installer\EyeGrok-Setup.ps1",
    "installer\lib\Core.psm1",
    "installer\lib\PairCrypto.ps1",
    "installer\lib\QrEncoder.ps1"
)
$i7ok = $true
$i8ok = $true
foreach ($rel in $scripts) {
    $path = Join-Path $RepoRoot $rel
    $tokens = $null
    $errs = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errs)
    if ($errs -and $errs.Count -gt 0) {
        $fail = 1
        $i8ok = $false
        [void]$notes.Add("AT-I8 FAIL " + $rel)
    }
    $text = [System.IO.File]::ReadAllText($path)
    if ($text.Contains("Start-Transcript") -or $text.Contains("Add-Type") -or $text.Contains("Invoke-Expression") -or $text.Contains("#requires -Version 7") -or $text.Contains("??") -or $text.Contains("?.")) {
        $fail = 1
        $i7ok = $false
        [void]$notes.Add("AT-I7 FAIL " + $rel)
    }
    $hosts = [regex]::Matches($text, "https?://[A-Za-z0-9._-]+")
    foreach ($match in $hosts) {
        $hostName = $match.Value
        $allowed = $false
        if ($hostName.StartsWith("https://dl.google.com")) { $allowed = $true }
        if ($hostName.StartsWith("https://developer.android.com")) { $allowed = $true }
        if ($hostName.StartsWith("https://www.nayuki.io")) { $allowed = $true }
        if ($hostName.StartsWith("http://127.0.0.1")) { $allowed = $true }
        if ($hostName.StartsWith("http://localhost")) { $allowed = $true }
        if (-not $allowed) {
            $fail = 1
            $i7ok = $false
            [void]$notes.Add("AT-I7 HOST FAIL")
        }
    }
}
if ($i7ok) { [void]$notes.Add("AT-I7 PASS") }
if ($i8ok) { [void]$notes.Add("AT-I8 PASS") }

$fresh = $null
$adbPort = ""
try {
    $fresh = New-WorkHome "fresh"
    $adbPort = [string](Get-FreePrivateAdbPort)
    # 192.0.2.1 is TEST-NET (RFC 5737). adb connect to it times out, so the frame step cannot reach a headset.
    $run = Invoke-SetupChild -EgHome $fresh -PathValue $filtered -AdbServerPort $adbPort -Arguments @(
        "-Headless", "-AcceptPlatformToolsLicense", "-Mode", "frame", "-FrameHost", "192.0.2.1"
    )
    Save-Proof "i1-fresh.txt" ($run.Stdout + "`n" + $run.Stderr) $null
    $adbExe = Join-Path $fresh "platform-tools\adb.exe"
    if (-not (Test-Path -LiteralPath $adbExe)) { throw "adb missing" }
    Assert-Contains $run.Stdout "LICENSE https://developer.android.com/tools/releases/platform-tools" "license"
    Assert-Contains $run.Stdout "E_WIFI_UNREACHABLE" "frame key"
    Assert-Contains $run.Stdout "The headset did not answer over Wi-Fi." "frame text"
    if ($run.Code -eq 0) { throw "fresh exit 0" }
    [void]$notes.Add("AT-I1 PASS")
} catch {
    $fail = 1
    [void]$notes.Add("AT-I1 FAIL " + [string]$_.Exception.Message)
} finally {
    if ($adbPort -and $fresh) {
        Stop-PrivateAdb -AdbExe (Join-Path $fresh "platform-tools\adb.exe") -Port $adbPort
    }
    Remove-WorkHome $fresh
}
Write-Trace "after i1"

# AT-I4 happy path, then AT-I5 and AT-I6 sentinel scans. Webhook, relay read, and pair keys are at least 32 characters.
Import-Module (Join-Path $RepoRoot "installer\lib\Core.psm1") -Force
. (Join-Path $RepoRoot "installer\lib\PairCrypto.ps1")
$relayProc = $null
$relayCmdPid = 0
$happyHome = $null
$stage = "AT-I4"
$scanStart = Get-Date
$hookSent = New-Sentinel 26
$readSent = New-Sentinel 26
$pathSent = New-Sentinel 24
$pairSent = New-Sentinel 26
$needles = @($hookSent, $readSent, $pathSent, $pairSent)
$relayUrl = "http://127.0.0.1:8787"
try {
    if (Test-Path -LiteralPath (Join-Path $RepoRoot "relay\.dev.vars")) { throw "dev.vars present" }
    if (Test-Path -LiteralPath (Join-Path $RepoRoot "relay\.wrangler")) { throw "wrangler state present" }
    if (Test-PortOpen 8787) { throw "port busy" }
    [System.IO.File]::WriteAllText($dummy, "dummy")
    $writeKey = New-RandomToken 43
    $readKey = New-RandomToken 43
    $varText = "WRITE_KEY=" + $writeKey + "`nREAD_KEY=" + $readKey + "`nPAIR_KEY=" + $pairSent + "`nPAIR_TTL_SECONDS=300`n"
    Write-Trace "start relay"
    $relayProc = Start-ProofRelay $varText
    try { $relayCmdPid = [int]$relayProc.Id } catch { $relayCmdPid = 0 }
    Write-Trace ("relay pid " + $relayCmdPid)
    $happyHome = New-WorkHome "happy"
    $stdin = '{"webhookUrl":"https://example.com/' + $pathSent + '","webhookKey":"' + $hookSent + '","relayUrl":"' + $relayUrl + '","relayReadKey":"' + $readSent + '","pairKey":"' + $pairSent + '"}'
    $values = $stdin | ConvertFrom-Json
    $expected = ConvertTo-SetupJson $values
    $run = Invoke-SetupChild -EgHome $happyHome -PathValue $filtered -Adb $Fake -Scenario "happy" -Arguments @(
        "-Headless", "-Mode", "full", "-PairMode", "all", "-CapturePairing", "-AllowLoopbackHttp", "-ApkPath", $dummy
    ) -Stdin $stdin
    Save-Proof "i4-happy.txt" ($run.Stdout + "`n" + $run.Stderr) $needles
    Write-Trace ("happy saved code " + $run.Code)
    if ($run.Code -ne 0) { throw "happy exit" }
    if ($run.Stdout.IndexOf("`nOK") -lt 0 -and -not $run.Stdout.TrimEnd().EndsWith("OK")) { throw "happy ok" }
    $qrId = $null
    $qrSecret = $null
    $code10 = $null
    foreach ($line in ($run.Stdout -split "`n")) {
        $trim = $line.Trim()
        if ($trim.StartsWith("CAPTURE qr-id=")) { $qrId = $trim.Substring("CAPTURE qr-id=".Length) }
        if ($trim.StartsWith("CAPTURE qr-secret=")) { $qrSecret = $trim.Substring("CAPTURE qr-secret=".Length) }
        if ($trim.StartsWith("CAPTURE code10=")) { $code10 = $trim.Substring("CAPTURE code10=".Length) }
    }
    if (-not $qrId -or -not $qrSecret -or -not $code10) { throw "capture" }
    $qrIdBytes = ConvertFrom-HexBytes $qrId
    $qrSecretBytes = ConvertFrom-HexBytes $qrSecret
    $qrEnc = Get-EyeGrokHkdf -Ikm $qrSecretBytes -Info "enc" -Length 32
    $qrMac = Get-EyeGrokHkdf -Ikm $qrSecretBytes -Info "mac" -Length 32
    $qrB64 = Assert-BoxEquals $qrIdBytes $qrEnc $qrMac $expected $relayUrl
    $codePair = New-CodePairing -Code10 $code10
    $codeB64 = Assert-BoxEquals $codePair.Id $codePair.Enc $codePair.Mac $expected $relayUrl
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $expectHash = ([BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($expected)))).Replace("-", "").ToLower()
    } finally {
        $sha.Dispose()
    }
    if ($run.Stdout.IndexOf("STDIN_SHA256=" + $expectHash) -lt 0) { throw "stdin hash" }
    [void]$notes.Add("AT-I4 PASS")
    Write-Trace "AT-I4 pass"
    $stage = "AT-I6"
    $hit = $null
    foreach ($needle in $needles) {
        if (-not $hit) { $hit = Find-SentinelInTree $happyHome $needle $null ([datetime]::MinValue) }
        $calls = Join-Path $happyHome "fake-adb-calls.txt"
        if ((Test-Path -LiteralPath $calls) -and -not $hit) { $hit = Find-SentinelInTree $calls $needle $null ([datetime]::MinValue) }
        if ($run.Stdout.IndexOf($needle) -ge 0 -or $run.Stderr.IndexOf($needle) -ge 0) { $hit = "stdio" }
    }
    if ($hit) { throw ("stdio or calls " + $hit) }
    [void]$notes.Add("AT-I6 PASS")
    Write-Trace "AT-I6 pass"
    $stage = "AT-I5"
    $wrangler = Join-Path $RepoRoot "relay\.wrangler"
    $skip = @(".git", "Library", "node_modules", "Builds", ".wrangler")
    foreach ($needle in $needles) {
        Write-Trace "scan disk"
        if (-not $hit) { $hit = Find-SentinelInTree $happyHome $needle $null ([datetime]::MinValue) }
        if (-not $hit) { $hit = Find-SentinelInTree $wrangler $needle $null ([datetime]::MinValue) }
        Write-Trace "scan temp"
        if (-not $hit) { $hit = Find-SentinelInTree $env:TEMP $needle $null $scanStart }
        Write-Trace "scan repo"
        if (-not $hit) { $hit = Find-SentinelInTree $RepoRoot $needle $skip ([datetime]::MinValue) }
        Write-Trace "scan repo done"
    }
    if ($hit) { throw ("disk " + $hit) }
    Remove-PairingEntry -RelayUrl $relayUrl -PairKey $pairSent -Id $qrB64 | Out-Null
    Remove-PairingEntry -RelayUrl $relayUrl -PairKey $pairSent -Id $codeB64 | Out-Null
    Write-Trace "stop relay"
    Stop-ProofRelay $relayProc
    Write-Trace "clear relay"
    if (Clear-RelayLocal) { throw "cleanup" }
    if (Test-Path -LiteralPath (Join-Path $RepoRoot "relay\.dev.vars")) { throw "dev.vars remained" }
    foreach ($needle in $needles) {
        if (-not $hit) { $hit = Find-SentinelInTree (Join-Path $RepoRoot "relay") $needle @(".wrangler") ([datetime]::MinValue) }
    }
    if ($hit) { throw ("after-clean " + $hit) }
    [void]$notes.Add("AT-I5 PASS")
} catch {
    $fail = 1
    $msg = [string]$_.Exception.Message
    foreach ($needle in $needles) { $msg = $msg.Replace($needle, "[REDACTED]") }
    $msg = $msg.Replace($RepoRoot, "[repo]").Replace($env:TEMP, "[temp]").Replace($env:USERPROFILE, "[profile]")
    [void]$notes.Add($stage + " FAIL " + $msg)
    try { Set-Content -LiteralPath (Join-Path $Proof "stage.txt") -Value ($stage + " FAIL " + $msg) -ErrorAction SilentlyContinue } catch { }
} finally {
    Write-Trace "finally"
    $cmdPid = $relayCmdPid
    try { if ($null -ne $relayProc) { $cmdPid = [int]$relayProc.Id } } catch { }
    if ($cmdPid -gt 0 -and $cmdPid -ne $PID) {
        Invoke-Silent -File "$env:SystemRoot\System32\taskkill.exe" -Arguments ("/PID " + $cmdPid + " /T /F")
    }
    try { [void](Wait-ProcExit $relayProc 5000) } catch { }
    try { if ($null -ne $relayProc) { $relayProc.CancelOutputRead() } } catch { }
    try { if ($null -ne $relayProc) { $relayProc.CancelErrorRead() } } catch { }
    $devVarsLeft = Join-Path $RepoRoot "relay\.dev.vars"
    try {
        if (Test-Path -LiteralPath $devVarsLeft) { Remove-Item -LiteralPath $devVarsLeft -Force -ErrorAction SilentlyContinue }
    } catch { }
    try { Clear-RelayLocal | Out-Null } catch { }
    Remove-WorkHome $happyHome
    Remove-Item -LiteralPath $dummy -Force -ErrorAction SilentlyContinue
    Get-EventSubscriber | Where-Object { $_.SourceObject -is [System.Diagnostics.Process] } | ForEach-Object { Unregister-Event -SourceIdentifier $_.SourceIdentifier -ErrorAction SilentlyContinue }
    try { if ($null -ne $relayProc) { $relayProc.Dispose() } } catch { }
}

Write-Trace "after finally"
$vecScript = Join-Path $RepoRoot "tools\test\pair-vectors.ps1"
$vecPath = Join-Path $RepoRoot "unity\Assets\EyeGrok\Tests\EditMode\Fixtures\pair-vectors.txt"
try {
    & $Pwsh -NoProfile -ExecutionPolicy Bypass -File $vecScript
    if ($LASTEXITCODE -ne 0) { throw "vectors" }
    $first = [System.IO.File]::ReadAllBytes($vecPath)
    & $Pwsh -NoProfile -ExecutionPolicy Bypass -File $vecScript
    if ($LASTEXITCODE -ne 0) { throw "vectors" }
    $second = [System.IO.File]::ReadAllBytes($vecPath)
    if ($first.Length -ne $second.Length) { throw "len" }
    for ($i = 0; $i -lt $first.Length; $i++) {
        if ($first[$i] -ne $second[$i]) { throw "byte" }
    }
    [void]$notes.Add("AT-I9 PASS")
} catch {
    $fail = 1
    [void]$notes.Add("AT-I9 FAIL")
}

Save-Proof "summary.txt" ($notes -join "`n") $null
if ($fail -ne 0) { Write-Output "INSTALLER_PROOF=fail"; exit 1 }
Write-Output "INSTALLER_PROOF=ok"
exit 0
