# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Headless installer proof. Values stay off the command line. Transcripts saved redacted.

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Proof = Join-Path $RepoRoot ".local\proof"
New-Item -ItemType Directory -Force -Path $Proof | Out-Null
$Setup = Join-Path $RepoRoot "installer\EyeGrok-Setup.ps1"
$Fake = Join-Path $RepoRoot "tools\test\fake-adb\adb.cmd"
$Pwsh = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"

function Invoke-Silent {
    param([string]$File, [string]$Arguments)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
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
        [string]$Stdin
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
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    if ($Stdin) {
        $proc.StandardInput.WriteLine($Stdin)
    }
    $proc.StandardInput.Close()
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    $exited = $proc.WaitForExit(240000)
    if (-not $exited) {
        Invoke-Silent -File "$env:SystemRoot\System32\taskkill.exe" -Arguments ("/PID " + $proc.Id + " /T /F")
        Get-Process -Name adb -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
        $proc.WaitForExit(5000) | Out-Null
    }
    if (-not $stdoutTask.Wait(5000)) {
        Get-Process -Name adb -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
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

function Test-BytesContain([byte[]]$Hay, [byte[]]$Needle) {
    if ($null -eq $Needle -or $Needle.Length -eq 0 -or $null -eq $Hay) { return $false }
    if ($Hay.Length -lt $Needle.Length) { return $false }
    for ($i = 0; $i -le ($Hay.Length - $Needle.Length); $i++) {
        $match = $true
        for ($j = 0; $j -lt $Needle.Length; $j++) {
            if ($Hay[$i + $j] -ne $Needle[$j]) { $match = $false; break }
        }
        if ($match) { return $true }
    }
    return $false
}

function Find-SentinelInTree([string]$Root, [string]$Needle, [string[]]$SkipNames, [datetime]$Since) {
    if (-not (Test-Path -LiteralPath $Root)) { return $null }
    $utf8 = [System.Text.Encoding]::UTF8.GetBytes($Needle)
    $utf16 = [System.Text.Encoding]::Unicode.GetBytes($Needle)
    $b64 = [System.Text.Encoding]::ASCII.GetBytes([Convert]::ToBase64String($utf8))
    $b16 = [System.Text.Encoding]::ASCII.GetBytes([Convert]::ToBase64String($utf16))
    $files = Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        if ($Since -gt [datetime]"2000-01-01" -and $file.LastWriteTime -lt $Since) { continue }
        $skip = $false
        if ($file.Name -eq ".dev.vars") { $skip = $true }
        if ($SkipNames) {
            foreach ($name in $SkipNames) {
                if ($file.FullName.IndexOf("\" + $name + "\") -ge 0) { $skip = $true }
            }
        }
        if ($skip) { continue }
        $bytes = $null
        try { $bytes = [System.IO.File]::ReadAllBytes($file.FullName) } catch { continue }
        if ((Test-BytesContain $bytes $utf8) -or (Test-BytesContain $bytes $utf16) -or (Test-BytesContain $bytes $b64) -or (Test-BytesContain $bytes $b16)) {
            return $file.FullName
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
    Invoke-Silent -File "$env:SystemRoot\System32\taskkill.exe" -Arguments ("/PID " + $proc.Id + " /T /F")
    throw "relay did not answer"
}

function Stop-ProofRelay($Proc) {
    if ($null -eq $Proc) { return }
    $id = 0
    try { $id = [int]$Proc.Id } catch { return }
    if ($id -le 0 -or $id -eq $PID) { return }
    $kids = @(Get-CimInstance Win32_Process -Filter ("ParentProcessId = " + $id) -ErrorAction SilentlyContinue)
    foreach ($kid in $kids) {
        if ($kid.ProcessId -eq $PID) { continue }
        try { Stop-Process -Id $kid.ProcessId -Force -ErrorAction SilentlyContinue } catch { }
    }
    try { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue } catch { }
}

function Clear-RelayLocal {
    $devVars = Join-Path $RepoRoot "relay\.dev.vars"
    $state = Join-Path $RepoRoot "relay\.wrangler"
    $self = $PID
    $procs = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -and ($_.CommandLine.IndexOf("wrangler") -ge 0 -or $_.CommandLine.IndexOf("workerd") -ge 0)
    })
    foreach ($proc in $procs) {
        if ($proc.ProcessId -eq $self) { continue }
        $parent = $proc.ParentProcessId
        $ancestor = $false
        $walk = $self
        for ($n = 0; $n -lt 6; $n++) {
            if ($walk -eq $proc.ProcessId) { $ancestor = $true; break }
            $node = Get-CimInstance Win32_Process -Filter ("ProcessId = " + $walk) -ErrorAction SilentlyContinue
            if (-not $node) { break }
            $walk = $node.ParentProcessId
        }
        if ($ancestor) { continue }
        try { Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue } catch { }
        if ($parent) {
            Get-CimInstance Win32_Process -Filter ("ParentProcessId = " + $proc.ProcessId) -ErrorAction SilentlyContinue | ForEach-Object {
                try { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } catch { }
            }
        }
    }
    if (Test-Path -LiteralPath $devVars) { Remove-Item -LiteralPath $devVars -Force -ErrorAction SilentlyContinue }
    for ($i = 0; $i -lt 10; $i++) {
        if (-not (Test-Path -LiteralPath $state)) { break }
        Remove-Item -LiteralPath $state -Recurse -Force -ErrorAction SilentlyContinue
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

$dummy = Join-Path $env:TEMP ("eyegrok-dummy-" + [guid]::NewGuid().ToString("N") + ".apk")
[System.IO.File]::WriteAllText($dummy, "dummy")
$usbJson = '{"webhookUrl":"https://example.com/hook","webhookKey":"0123456789abcdef","relayUrl":"https://example.com/relay","relayReadKey":"fedcba9876543210"}'
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
        Save-Proof ("i3-" + $case.Name + ".txt") ($run.Stdout + "`n" + $run.Stderr) @("0123456789abcdef", "fedcba9876543210")
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

$scripts = @(
    "installer\EyeGrok-Setup.ps1",
    "installer\lib\Core.psm1",
    "installer\lib\PairCrypto.ps1",
    "installer\lib\QrEncoder.ps1"
)
foreach ($rel in $scripts) {
    $path = Join-Path $RepoRoot $rel
    $tokens = $null
    $errs = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errs)
    if ($errs -and $errs.Count -gt 0) { $fail = 1; [void]$notes.Add("AT-I8 FAIL " + $rel) }
    $text = [System.IO.File]::ReadAllText($path)
    if ($text.Contains("Start-Transcript") -or $text.Contains("Add-Type") -or $text.Contains("Invoke-Expression") -or $text.Contains("#requires -Version 7") -or $text.Contains("??") -or $text.Contains("?.")) {
        $fail = 1
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
            [void]$notes.Add("AT-I7 HOST FAIL")
        }
    }
}
if ($fail -eq 0) { [void]$notes.Add("AT-I7 PASS"); [void]$notes.Add("AT-I8 PASS") }

$deviceSkip = $false
$sysAdb = Get-Command adb -ErrorAction SilentlyContinue
if ($sysAdb) {
    $listed = ""
    $adbPsi = New-Object System.Diagnostics.ProcessStartInfo
    $adbPsi.FileName = $sysAdb.Source
    $adbPsi.Arguments = "devices"
    $adbPsi.UseShellExecute = $false
    $adbPsi.RedirectStandardOutput = $true
    $adbPsi.RedirectStandardError = $true
    $adbPsi.CreateNoWindow = $true
    $adbProc = New-Object System.Diagnostics.Process
    $adbProc.StartInfo = $adbPsi
    [void]$adbProc.Start()
    if ($adbProc.WaitForExit(8000)) {
        $listed = $adbProc.StandardOutput.ReadToEnd()
    } else {
        try { $adbProc.Kill() } catch { }
    }
    foreach ($line in ($listed -split "`n")) {
        if ($line -match "\sdevice(\s|$)") { $deviceSkip = $true }
    }
}
if ($deviceSkip) {
    [void]$notes.Add("AT-I1 SKIP device attached")
    Save-Proof "i1-fresh.txt" "SKIP a device was already attached" $null
} else {
    $fresh = New-WorkHome "fresh"
    try {
        $run = Invoke-SetupChild -EgHome $fresh -PathValue $filtered -Arguments @("-Headless", "-AcceptPlatformToolsLicense", "-Mode", "frame")
        Save-Proof "i1-fresh.txt" ($run.Stdout + "`n" + $run.Stderr) $null
        $adbExe = Join-Path $fresh "platform-tools\adb.exe"
        if (-not (Test-Path -LiteralPath $adbExe)) { throw "adb missing" }
        $frameHit = $false
        foreach ($token in @("E_NO_FRAME", "E_WIFI_NOHOST", "E_WIFI_UNREACHABLE", "E_LEPTON_NOT_RUNNING")) {
            if ($run.Stdout.IndexOf($token) -ge 0) { $frameHit = $true }
        }
        if (-not $frameHit) { throw "expected frame error" }
        if ($run.Code -eq 0) { throw "fresh exit 0" }
        Invoke-Silent -File $adbExe -Arguments "kill-server"
        [void]$notes.Add("AT-I1 PASS")
    } catch {
        $fail = 1
        [void]$notes.Add("AT-I1 FAIL")
    } finally {
        Remove-WorkHome $fresh
    }
}

# AT-I4 happy path, then AT-I5 and AT-I6 sentinel scans. PAIR_KEY is 32 chars so the relay accepts it.
Import-Module (Join-Path $RepoRoot "installer\lib\Core.psm1") -Force
. (Join-Path $RepoRoot "installer\lib\PairCrypto.ps1")
$relayProc = $null
$happyHome = $null
$stage = "AT-I4"
$scanStart = Get-Date
$hookSent = New-Sentinel 24
$readSent = New-Sentinel 24
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
    $relayProc = Start-ProofRelay $varText
    $happyHome = New-WorkHome "happy"
    $stdin = '{"webhookUrl":"https://example.com/' + $pathSent + '","webhookKey":"' + $hookSent + '","relayUrl":"' + $relayUrl + '","relayReadKey":"' + $readSent + '","pairKey":"' + $pairSent + '"}'
    $values = $stdin | ConvertFrom-Json
    $expected = ConvertTo-SetupJson $values
    $run = Invoke-SetupChild -EgHome $happyHome -PathValue $filtered -Adb $Fake -Scenario "happy" -Arguments @(
        "-Headless", "-Mode", "full", "-PairMode", "all", "-CapturePairing", "-AllowLoopbackHttp", "-ApkPath", $dummy
    ) -Stdin $stdin
    Save-Proof "i4-happy.txt" ($run.Stdout + "`n" + $run.Stderr) $needles
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
    $stage = "AT-I5"
    $wrangler = Join-Path $RepoRoot "relay\.wrangler"
    $skip = @(".git", "Library", "node_modules", "Builds", ".wrangler")
    foreach ($needle in $needles) {
        if (-not $hit) { $hit = Find-SentinelInTree $happyHome $needle $null ([datetime]::MinValue) }
        if (-not $hit) { $hit = Find-SentinelInTree $wrangler $needle $null ([datetime]::MinValue) }
        if (-not $hit) { $hit = Find-SentinelInTree $env:TEMP $needle $null $scanStart }
        if (-not $hit) { $hit = Find-SentinelInTree $RepoRoot $needle $skip ([datetime]::MinValue) }
    }
    if ($hit) { throw ("disk " + $hit) }
    Remove-PairingEntry -RelayUrl $relayUrl -PairKey $pairSent -Id $qrB64 | Out-Null
    Remove-PairingEntry -RelayUrl $relayUrl -PairKey $pairSent -Id $codeB64 | Out-Null
    Stop-ProofRelay $relayProc
    $relayProc = $null
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
    try { Stop-ProofRelay $relayProc } catch { }
    try { Clear-RelayLocal | Out-Null } catch { }
    Remove-WorkHome $happyHome
    Remove-Item -LiteralPath $dummy -Force -ErrorAction SilentlyContinue
    Get-EventSubscriber | Where-Object { $_.SourceObject -is [System.Diagnostics.Process] } | ForEach-Object { Unregister-Event -SourceIdentifier $_.SourceIdentifier -ErrorAction SilentlyContinue }
}

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
