# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Installer logic for Windows PowerShell 5.1. No UI. No setup values or headset addresses in the log.

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$script:LicenseUrl = "https://developer.android.com/tools/releases/platform-tools"
$script:PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip"
# SHA-1 of that zip, from dl.google.com/android/repository/repository2-3.xml (platform-tools 37.0.1, windows).
$script:PlatformToolsSha1 = "e03e78b1d80b396f1c3358e31251cb31740e1110"
$script:PackageId = "io.github.eyegrok.ping"
# Launcher from unity/Assets/Plugins/Android/AndroidManifest.xml.
# BuildSettingsApplier sets AndroidApplicationEntry.Activity, which is this class.
$script:LauncherActivity = "com.unity3d.player.UnityPlayerActivity"
$script:LibDir = $PSScriptRoot

. (Join-Path $script:LibDir "PairCrypto.ps1")
. (Join-Path $script:LibDir "QrEncoder.ps1")

function Get-EyeGrokHome {
    if ($env:EYEGROK_HOME) { return $env:EYEGROK_HOME }
    return (Join-Path $env:LOCALAPPDATA "EyeGrok")
}

function Get-EyeGrokLicenseUrl {
    return $script:LicenseUrl
}

function Get-EyeGrokErrorText {
    param(
        [Parameter(Mandatory = $true)][string]$Key,
        [string]$Detail
    )
    if ($Key -eq "E_NO_FRAME") {
        return "No Frame found. Check the USB cable, turn on Developer Mode, and start Lepton Development from the Steam Library on the Frame. Then click Retry."
    }
    if ($Key -eq "E_UNAUTHORIZED") {
        return "The Frame is connected but has not allowed this PC. Put on the headset, allow USB debugging, then click Retry."
    }
    if ($Key -eq "E_NO_LEPTON") {
        return "The Frame is connected, but Lepton Development is not running. Start Lepton Development from the Steam Library on the Frame, then click Retry."
    }
    if ($Key -eq "E_WIFI_NOHOST") {
        $name = "frame"
        if ($Detail -and (Test-SafeFrameHost $Detail) -and -not (Test-IPv4Literal $Detail)) { $name = $Detail }
        return "This PC cannot find a headset named " + $name + ". On the Frame, open Quick Settings, click your Wi-Fi network, and enter the IP address shown there. Steam Settings, then Internet, shows the same address, for example 192.168.4.80. Make sure no two devices share the hostname " + $name + "."
    }
    if ($Key -eq "E_LEPTON_NOT_RUNNING") {
        return "The Frame refused the Wi-Fi connection. Start Lepton Development from the Steam Library on the Frame, or any Android game from Steam, then click Retry."
    }
    if ($Key -eq "E_WIFI_UNREACHABLE") {
        return "The headset did not answer over Wi-Fi. Put this PC and the Frame on the same network, and turn off client isolation on the router. Or use USB instead. Make sure no two devices share the hostname frame."
    }
    if ($Key -eq "E_BAD_IP") {
        return "That is not an IPv4 address. Enter four numbers from 0 to 255, separated by dots."
    }
    if ($Key -eq "E_INSTALL") {
        $code = "UNKNOWN"
        $hint = "Try again."
        if ($Detail -eq "INSUFFICIENT_STORAGE") {
            $code = "INSUFFICIENT_STORAGE"
            $hint = "Free some space on the Frame."
        } elseif ($Detail -eq "UPDATE_INCOMPATIBLE") {
            $code = "UPDATE_INCOMPATIBLE"
            $hint = "Remove the old EyeGrok first."
        } elseif ($Detail) {
            $code = $Detail
        }
        return "EyeGrok did not install (" + $code + "). " + $hint
    }
    if ($Key -eq "E_IMPORT") {
        return "The headset did not take the setup. Open EyeGrok on the Frame and try again, or use another pairing tab."
    }
    if ($Key -eq "E_IMPORT_FAILED") {
        return "The setup file was not renamed on the headset. eyegrok-setup.json.part is still there, or eyegrok-setup.json is missing."
    }
    if ($Key -eq "E_PAIR_OFF") {
        return "Pairing is off on your relay. Add PAIR_KEY (see docs/RELAY.md), or use the USB push tab."
    }
    if ($Key -eq "E_PAIR_KEY") {
        return "The relay rejected the pairing key."
    }
    if ($Key -eq "E_NO_APK") {
        return "No EyeGrok APK next to the installer. Click Browse to pick one."
    }
    if ($Key -eq "E_DOWNLOAD") {
        return "Could not download Android platform-tools. Check your internet connection, then click Retry."
    }
    if ($Key -eq "E_VERIFY") {
        return "The downloaded platform-tools did not pass the signature check. Nothing was run."
    }
    if ($Key -eq "E_LICENSE_NOT_ACCEPTED") {
        return "Accept the Android SDK Platform-Tools terms before download."
    }
    return $Key
}

function Write-InstallerLog {
    param([string]$Step, [int]$Code, [string]$Key)
    $dir = Join-Path (Get-EyeGrokHome) "logs"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $line = $Step + " code=" + $Code
    if ($Key) { $line = $line + " key=" + $Key }
    $path = Join-Path $dir "installer.log"
    [System.IO.File]::AppendAllText($path, $line + "`n", (New-Object System.Text.UTF8Encoding $false))
}

function Find-Adb {
    if ($env:EYEGROK_ADB -and (Test-Path -LiteralPath $env:EYEGROK_ADB)) {
        return $env:EYEGROK_ADB
    }
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }
    $local = Join-Path (Get-EyeGrokHome) "platform-tools\adb.exe"
    if (Test-Path -LiteralPath $local) { return $local }
    return $null
}

function ConvertTo-QuotedArgs {
    param([string[]]$ArgumentList)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($arg in $ArgumentList) {
        if ($arg -match '[\s"]') {
            $parts.Add('"' + $arg.Replace('"', '\"') + '"')
        } else {
            $parts.Add($arg)
        }
    }
    return ($parts -join " ")
}

function Invoke-Adb {
    param(
        [string[]]$ArgumentList,
        [byte[]]$StdinBytes
    )
    $adb = Find-Adb
    if (-not $adb) { return @{ Code = 127; Stdout = ""; Stderr = "no adb" } }
    $file = $adb
    $argsText = ConvertTo-QuotedArgs $ArgumentList
    $shim = Join-Path (Split-Path -Parent $adb) "fake-adb.ps1"
    if ((Test-Path -LiteralPath $shim) -and ($adb.EndsWith(".cmd") -or $adb.EndsWith(".ps1"))) {
        $file = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
        $argsText = "-NoProfile -ExecutionPolicy Bypass -File " + (ConvertTo-QuotedArgs @($shim)) + " " + $argsText
    }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $file
    $psi.Arguments = $argsText
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrTask = $proc.StandardError.ReadToEndAsync()
    if ($StdinBytes -ne $null -and $StdinBytes.Length -gt 0) {
        $proc.StandardInput.BaseStream.Write($StdinBytes, 0, $StdinBytes.Length)
        $proc.StandardInput.BaseStream.Flush()
    }
    $proc.StandardInput.Close()
    $exited = $proc.WaitForExit(60000)
    if (-not $exited) {
        try { $proc.Kill() } catch { }
        Get-Process -Name adb -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
        $proc.WaitForExit(5000) | Out-Null
    }
    if (-not $stdoutTask.Wait(3000)) {
        Get-Process -Name adb -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
        [void]$stdoutTask.Wait(3000)
    }
    if (-not $stderrTask.Wait(3000)) { [void]$stderrTask.Wait(2000) }
    $stdoutText = ""
    $stderrText = ""
    if ($stdoutTask.IsCompleted -and -not $stdoutTask.IsFaulted -and -not $stdoutTask.IsCanceled) { $stdoutText = [string]$stdoutTask.Result }
    if ($stderrTask.IsCompleted -and -not $stderrTask.IsFaulted -and -not $stderrTask.IsCanceled) { $stderrText = [string]$stderrTask.Result }
    $code = 124
    if ($proc.HasExited) {
        try { $code = $proc.ExitCode } catch { $code = 124 }
    }
    return @{ Code = $code; Stdout = $stdoutText; Stderr = $stderrText }
}

function Install-PlatformTools {
    param([switch]$Accepted)
    $egHome = Get-EyeGrokHome
    $existing = Join-Path $egHome "platform-tools\adb.exe"
    if (Test-Path -LiteralPath $existing) { return "OK" }
    if (-not $Accepted) { return "E_LICENSE_NOT_ACCEPTED" }
    $dl = Join-Path $egHome "dl"
    New-Item -ItemType Directory -Force -Path $dl | Out-Null
    $zip = Join-Path $dl "platform-tools.zip"
    try {
        $request = [System.Net.HttpWebRequest]::Create($script:PlatformToolsUrl)
        $request.Method = "GET"
        $request.Timeout = 120000
        $request.ReadWriteTimeout = 120000
        $response = $request.GetResponse()
        $inputStream = $response.GetResponseStream()
        $output = [System.IO.File]::Create($zip)
        try {
            $inputStream.CopyTo($output)
        } finally {
            $output.Close()
            $inputStream.Close()
            $response.Close()
        }
    } catch {
        Write-InstallerLog "download" 1 "E_DOWNLOAD"
        if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
        return "E_DOWNLOAD"
    }
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA1).Hash.ToLower()
    if ($actual -ne $script:PlatformToolsSha1) {
        Remove-Item -LiteralPath $zip -Force
        Write-InstallerLog "verify" 1 "E_VERIFY"
        return "E_VERIFY"
    }
    $extract = Join-Path $dl "extract"
    try {
        New-Item -ItemType Directory -Force -Path $extract | Out-Null
        [void][System.Reflection.Assembly]::Load("System.IO.Compression.FileSystem, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)
        Remove-Item -LiteralPath $zip -Force
        $adb = Join-Path $extract "platform-tools\adb.exe"
        if (-not (Test-Path -LiteralPath $adb)) {
            Write-InstallerLog "verify" 1 "E_VERIFY"
            return "E_VERIFY"
        }
        $sig = Get-AuthenticodeSignature -LiteralPath $adb
        $subject = ""
        if ($sig.SignerCertificate) { $subject = $sig.SignerCertificate.Subject }
        if ($sig.Status -ne "Valid" -or $subject.IndexOf("CN=Google LLC") -lt 0) {
            Write-InstallerLog "verify" 1 "E_VERIFY"
            return "E_VERIFY"
        }
        $dest = Join-Path $egHome "platform-tools"
        if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $egHome | Out-Null
        [System.IO.Directory]::Move((Join-Path $extract "platform-tools"), $dest)
        Write-InstallerLog "tools" 0 ""
        return "OK"
    } finally {
        try {
            if (Test-Path -LiteralPath $extract) {
                Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
            }
        } catch {
        }
    }
}

function Get-FrameSerial {
    if ($script:FrameSerial) { return $script:FrameSerial }
    return "localhost:5555"
}

function Test-IPv4Literal {
    param([string]$Text)
    if (-not $Text) { return $false }
    $parts = $Text.Split(".")
    if ($parts.Length -ne 4) { return $false }
    foreach ($part in $parts) {
        if ($part.Length -lt 1 -or $part.Length -gt 3) { return $false }
        foreach ($ch in $part.ToCharArray()) {
            $code = [int]$ch
            if ($code -lt 48 -or $code -gt 57) { return $false }
        }
        if ([int]$part -gt 255) { return $false }
    }
    return $true
}

function Test-SafeFrameHost {
    param([string]$Name)
    if (-not $Name) { return $false }
    if ($Name.Length -gt 253) { return $false }
    if ($Name -notmatch '^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$') { return $false }
    if ($Name.Contains("..")) { return $false }
    return $true
}

function Get-WifiFailureKey {
    param([string]$Text)
    if (-not $Text) { return $null }
    $lower = $Text.ToLowerInvariant()
    if ($lower.Contains("cannot resolve host") -or $lower.Contains("11001")) { return "E_WIFI_NOHOST" }
    if ($lower.Contains("10061") -or $lower.Contains("connection refused") -or $lower.Contains("actively refused")) { return "E_LEPTON_NOT_RUNNING" }
    if ($lower.Contains("10060") -or $lower.Contains("timed out") -or $lower.Contains("unreachable")) { return "E_WIFI_UNREACHABLE" }
    return $null
}

function Test-FrameDevice {
    param([string]$Serial)
    if (-not $Serial) { return $false }
    $result = Invoke-Adb -ArgumentList @("devices")
    foreach ($line in ([string]$result.Stdout -split "`n")) {
        $text = $line.Trim()
        if ($text.Length -le $Serial.Length) { continue }
        if (-not $text.StartsWith($Serial)) { continue }
        $next = $text.Substring($Serial.Length, 1)
        if ($next -ne " " -and $next -ne "`t") { continue }
        if ($text -match "\sdevice(\s|$)") { return $true }
    }
    return $false
}

function Invoke-WifiConnect {
    param([string]$Endpoint)
    $result = Invoke-Adb -ArgumentList @("connect", $Endpoint)
    $text = [string]$result.Stdout
    if ($result.Stderr) { $text = $text + "`n" + [string]$result.Stderr }
    $lower = ""
    if ($text) { $lower = $text.ToLowerInvariant() }
    $key = Get-WifiFailureKey $text
    $said = $false
    if ($lower.Contains("connected to ")) { $said = $true }
    if ($said -and (Test-FrameDevice -Serial $Endpoint)) {
        $script:FrameSerial = $Endpoint
        return @{ Ok = $true; Key = $null; Signed = $false }
    }
    if ($key) { return @{ Ok = $false; Key = $key; Signed = $true } }
    if ($said) { return @{ Ok = $false; Key = "E_NO_FRAME"; Signed = $true } }
    return @{ Ok = $false; Key = $null; Signed = $false }
}

function Resolve-FrameLink {
    param(
        [string]$HostName,
        [string]$IpAddress,
        [switch]$UseUsb,
        [switch]$IpOnly
    )
    if ($UseUsb) {
        $state = Connect-Lepton
        if ($state -eq "Ready") { return @{ Ok = $true; Key = $null; Signed = $false } }
        $key = "E_NO_FRAME"
        if ($state -eq "Unauthorized") { $key = "E_UNAUTHORIZED" }
        if ($state -eq "UsbOnly") { $key = "E_NO_LEPTON" }
        return @{ Ok = $false; Key = $key; Signed = $false }
    }
    if ($IpOnly) {
        $ip = ""
        if ($IpAddress) { $ip = $IpAddress.Trim() }
        if (-not (Test-IPv4Literal $ip)) { return @{ Ok = $false; Key = "E_BAD_IP"; Signed = $true } }
        return (Invoke-WifiConnect -Endpoint ($ip + ":5555"))
    }
    $name = "frame"
    if ($HostName -and $HostName.Trim().Length -gt 0) { $name = $HostName.Trim() }
    if (-not (Test-SafeFrameHost $name)) {
        return @{ Ok = $false; Key = "E_WIFI_NOHOST"; Signed = $true }
    }
    $wifi = Invoke-WifiConnect -Endpoint ($name + ":5555")
    if ($wifi.Ok) { return $wifi }
    if ($IpAddress -and $IpAddress.Trim().Length -gt 0) {
        $ip = $IpAddress.Trim()
        if (-not (Test-IPv4Literal $ip)) { return @{ Ok = $false; Key = "E_BAD_IP"; Signed = $true } }
        return (Invoke-WifiConnect -Endpoint ($ip + ":5555"))
    }
    if ($wifi.Signed) { return $wifi }
    $state = Get-FrameState
    if ($state -eq "UsbOnly") { $state = Connect-Lepton }
    if ($state -eq "Ready") {
        $script:FrameSerial = "localhost:5555"
        return @{ Ok = $true; Key = $null; Signed = $false }
    }
    if ($state -eq "Unauthorized") { return @{ Ok = $false; Key = "E_UNAUTHORIZED"; Signed = $false } }
    if ($state -eq "UsbOnly") { return @{ Ok = $false; Key = "E_NO_LEPTON"; Signed = $false } }
    return @{ Ok = $false; Key = "E_NO_FRAME"; Signed = $false }
}

function Get-FrameState {
    $result = Invoke-Adb -ArgumentList @("devices", "-l")
    $ready = $false
    $unauth = $false
    $usb = $false
    foreach ($line in ($result.Stdout -split "`n")) {
        $text = $line.Trim()
        if ($text.Length -eq 0) { continue }
        if ($text.StartsWith("List of devices")) { continue }
        if ($text.StartsWith("localhost:5555") -and $text -match "\sdevice(\s|$)") { $ready = $true }
        elseif ($text -match "unauthorized") { $unauth = $true }
        elseif ($text -match "\sdevice(\s|$)") { $usb = $true }
    }
    if ($ready) { return "Ready" }
    if ($unauth) { return "Unauthorized" }
    if ($usb) { return "UsbOnly" }
    return "NoDevice"
}

function Connect-Lepton {
    Invoke-Adb -ArgumentList @("forward", "tcp:5555", "tcp:5555") | Out-Null
    Invoke-Adb -ArgumentList @("connect", "localhost:5555") | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if ((Get-FrameState) -eq "Ready") {
            $script:FrameSerial = "localhost:5555"
            return "Ready"
        }
        Start-Sleep -Milliseconds 400
    } while ([DateTime]::UtcNow -lt $deadline)
    $state = Get-FrameState
    if ($state -eq "Ready") { $script:FrameSerial = "localhost:5555" }
    return $state
}

function Find-NewestApk {
    param([string]$Directory, [string]$Chosen)
    if ($Chosen -and (Test-Path -LiteralPath $Chosen)) { return $Chosen }
    if (-not $Directory) { return $null }
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter "EyeGrok-*.apk" -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $null }
    return ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Install-EyeGrokApk {
    param([Parameter(Mandatory = $true)][string]$ApkPath)
    $serial = Get-FrameSerial
    $result = Invoke-Adb -ArgumentList @("-s", $serial, "install", "-r", $ApkPath)
    $text = $result.Stdout + "`n" + $result.Stderr
    if ($result.Code -eq 0 -and $text -match "Success") {
        Write-InstallerLog "install" 0 ""
        return @{ Key = "OK"; Detail = "" }
    }
    $detail = "UNKNOWN"
    if ($text -match "Failure \[([A-Z0-9_]+)\]") { $detail = $Matches[1] }
    Write-InstallerLog "install" $result.Code $detail
    return @{ Key = "E_INSTALL"; Detail = $detail }
}

function ConvertTo-SetupJson {
    param($Values)
    $pairs = @(
        @("webhookUrl", [string]$Values.webhookUrl),
        @("webhookKey", [string]$Values.webhookKey),
        @("relayUrl", [string]$Values.relayUrl),
        @("relayReadKey", [string]$Values.relayReadKey)
    )
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("{")
    for ($i = 0; $i -lt $pairs.Count; $i++) {
        if ($i -gt 0) { [void]$sb.Append(",") }
        [void]$sb.Append('"')
        [void]$sb.Append($pairs[$i][0])
        [void]$sb.Append('":"')
        [void]$sb.Append((ConvertTo-JsonString $pairs[$i][1]))
        [void]$sb.Append('"')
    }
    [void]$sb.Append("}")
    return $sb.ToString()
}

function ConvertTo-JsonString {
    param([string]$Text)
    if ($null -eq $Text) { return "" }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Text.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '"') { [void]$sb.Append('\"') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($code -lt 32) { [void]$sb.Append('\u' + $code.ToString("x4")) }
        else { [void]$sb.Append($ch) }
    }
    return $sb.ToString()
}

function Test-SetupValues {
    param($Values, [switch]$AllowLoopbackHttp)
    if ($null -eq $Values) { return "webhookUrl" }
    foreach ($name in @("webhookUrl", "webhookKey", "relayUrl", "relayReadKey")) {
        $value = [string]$Values.$name
        if (-not $value) { return $name }
    }
    if (-not (Test-HttpsOrLoopback ([string]$Values.webhookUrl) $AllowLoopbackHttp)) { return "webhookUrl" }
    if (-not (Test-KeyValue ([string]$Values.webhookKey))) { return "webhookKey" }
    if (-not (Test-HttpsOrLoopback ([string]$Values.relayUrl) $AllowLoopbackHttp)) { return "relayUrl" }
    if (-not (Test-KeyValue ([string]$Values.relayReadKey))) { return "relayReadKey" }
    return $null
}

function Test-HttpsOrLoopback {
    param([string]$Url, [bool]$AllowLoopbackHttp)
    if (-not $Url -or $Url.Length -gt 2048) { return $false }
    if ($Url.StartsWith("https://") -and $Url.Length -gt 8) { return $true }
    if (-not $AllowLoopbackHttp) { return $false }
    if ($Url.StartsWith("http://127.0.0.1")) { return $true }
    if ($Url.StartsWith("http://localhost")) { return $true }
    return $false
}

function Get-SetupKeyError {
    param([string]$Key, [string]$Label)
    if (-not $Label) { $Label = "The key" }
    if (-not $Key -or $Key.Length -lt 32) { return $Label + " must be at least 32 characters." }
    if ($Key.Length -gt 512) { return $Label + " must be at most 512 characters." }
    foreach ($ch in $Key.ToCharArray()) {
        if ([char]::IsWhiteSpace($ch)) { return $Label + " must not contain spaces." }
    }
    return $null
}

function Test-KeyValue {
    param([string]$Key)
    if (Get-SetupKeyError -Key $Key -Label "The key") { return $false }
    return $true
}

function Invoke-PairHttp {
    param([string]$Method, [string]$Url, [string]$Token, [byte[]]$Body)
    $request = [System.Net.HttpWebRequest]::Create($Url)
    $request.Method = $Method
    $request.Timeout = 20000
    $request.AllowAutoRedirect = $false
    if ($Token) { $request.Headers["Authorization"] = "Bearer " + $Token }
    if ($Body -ne $null) {
        $request.ContentType = "application/json; charset=utf-8"
        $request.ContentLength = $Body.Length
        $stream = $request.GetRequestStream()
        $stream.Write($Body, 0, $Body.Length)
        $stream.Close()
    }
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

function New-PairingEntry {
    param([string]$RelayUrl, [string]$PairKey, [string]$Id, $Box)
    $root = $RelayUrl.TrimEnd("/")
    $payload = '{"id":"' + $Id + '","box":{"v":1,"iv":"' + $Box.iv + '","ct":"' + $Box.ct + '","mac":"' + $Box.mac + '"}}'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    return Invoke-PairHttp "POST" ($root + "/pair") $PairKey $bytes
}

function Remove-PairingEntry {
    param([string]$RelayUrl, [string]$PairKey, [string]$Id)
    $root = $RelayUrl.TrimEnd("/")
    return Invoke-PairHttp "DELETE" ($root + "/pair/" + $Id) $PairKey $null
}

function Send-StreamOverAdb {
    param([string]$RemotePath, [byte[]]$Bytes)
    $part = $RemotePath + ".part"
    $command = "cat > " + $part + " && mv " + $part + " " + $RemotePath
    $serial = Get-FrameSerial
    $exec = Invoke-Adb -ArgumentList @("-s", $serial, "exec-in", $command) -StdinBytes $Bytes
    if ($exec.Code -eq 0) { return @{ Path = "exec-in"; Result = $exec } }
    $shell = Invoke-Adb -ArgumentList @("-s", $serial, "shell", $command) -StdinBytes $Bytes
    if ($shell.Code -eq 0) { return @{ Path = "shell"; Result = $shell } }
    $temp = Write-B3TempFile $Bytes
    Write-InstallerLog "b3" 0 "tempfile"
    try {
        $pushed = Invoke-Adb -ArgumentList @("-s", $serial, "push", $temp, $RemotePath)
        return @{ Path = "tempfile"; Result = $pushed }
    } finally {
        Clear-B3TempFile $temp
    }
}

function Write-B3TempFile {
    param([byte[]]$Bytes)
    $dir = Join-Path (Get-EyeGrokHome) "tmp"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $path = Join-Path $dir ("b3-" + [guid]::NewGuid().ToString("N"))
    $stream = [System.IO.File]::Create($path)
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
    } finally {
        $stream.Close()
    }
    $acl = Get-Acl -LiteralPath $path
    $acl.SetAccessRuleProtection($true, $false)
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
        "FullControl",
        "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $path -AclObject $acl
    return $path
}

function Clear-B3TempFile {
    param([string]$Path)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return }
    $length = (Get-Item -LiteralPath $Path).Length
    $zeros = New-Object byte[] $length
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Write)
    try {
        $stream.Write($zeros, 0, $zeros.Length)
        $stream.Flush()
    } finally {
        $stream.Close()
    }
    Remove-Item -LiteralPath $Path -Force
}

function Test-RemoteFilePresent {
    param([string]$Serial, [string]$RemotePath)
    $listing = Invoke-Adb -ArgumentList @("-s", $Serial, "shell", "ls", $RemotePath)
    if ($listing.Code -ne 0) { return $false }
    $base = $RemotePath
    $slash = $RemotePath.LastIndexOf("/")
    if ($slash -ge 0) { $base = $RemotePath.Substring($slash + 1) }
    foreach ($line in ([string]$listing.Stdout -split "`n")) {
        $trim = $line.Trim()
        if ($trim -eq $RemotePath -or $trim -eq $base) { return $true }
    }
    return $false
}

function Send-SetupOverAdb {
    param([string]$Json)
    $remote = "/sdcard/Android/data/" + $script:PackageId + "/files/eyegrok-setup.json"
    $part = $remote + ".part"
    $bytes = New-Object System.Text.UTF8Encoding $false
    $sent = Send-StreamOverAdb -RemotePath $remote -Bytes ($bytes.GetBytes($Json))
    $stdout = ""
    if ($sent.Result) { $stdout = [string]$sent.Result.Stdout }
    $serial = Get-FrameSerial
    # A missing final file before launch is a failed rename, not a consumed import.
    $finalThere = Test-RemoteFilePresent -Serial $serial -RemotePath $remote
    $partThere = Test-RemoteFilePresent -Serial $serial -RemotePath $part
    if ($partThere -or -not $finalThere) {
        Write-InstallerLog "import" 1 "E_IMPORT_FAILED"
        return @{ Key = "E_IMPORT_FAILED"; Stdout = $stdout }
    }
    $component = $script:PackageId + "/" + $script:LauncherActivity
    [void](Invoke-Adb -ArgumentList @("-s", $serial, "shell", "am", "start", "-n", $component))
    # Wall-clock budget, not a fixed call count. A slow adb still returns the error.
    $consumed = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    while ($true) {
        $finalThere = Test-RemoteFilePresent -Serial $serial -RemotePath $remote
        $partThere = Test-RemoteFilePresent -Serial $serial -RemotePath $part
        if ($partThere) {
            Write-InstallerLog "import" 1 "E_IMPORT_FAILED"
            return @{ Key = "E_IMPORT_FAILED"; Stdout = $stdout }
        }
        if (-not $finalThere) {
            $consumed = $true
            break
        }
        $remain = ($deadline - [DateTime]::UtcNow).TotalMilliseconds
        if ($remain -le 50) { break }
        $nap = 250
        if ($remain -lt $nap) { $nap = [int]$remain }
        if ($nap -gt 0) { Start-Sleep -Milliseconds $nap }
    }
    if ($consumed) {
        Write-InstallerLog "import" 0 ""
        return @{ Key = "OK"; Stdout = $stdout }
    }
    Write-InstallerLog "import" 1 "E_IMPORT"
    return @{ Key = "E_IMPORT"; Stdout = $stdout }
}

function Send-PairOrigin {
    param([string]$RelayUrl)
    if (-not $RelayUrl -or $RelayUrl.Length -gt 2048 -or $RelayUrl.Contains("`n")) { return "E_IMPORT" }
    $remote = "/sdcard/Android/data/" + $script:PackageId + "/files/eyegrok-pair-origin.txt"
    $bytes = New-Object System.Text.UTF8Encoding $false
    $sent = Send-StreamOverAdb -RemotePath $remote -Bytes ($bytes.GetBytes($RelayUrl + "`n"))
    if ($sent.Result.Code -eq 0) {
        Write-InstallerLog "origin" 0 ""
        return "OK"
    }
    Write-InstallerLog "origin" 1 "E_IMPORT"
    return "E_IMPORT"
}

function Format-PairCode {
    param([string]$Code11)
    return $Code11.Substring(0, 4) + " " + $Code11.Substring(4, 4) + " " + $Code11.Substring(8, 3)
}

function New-SetupPairing {
    param([string]$Json, [string]$RelayUrl, [string]$Mode, [string]$Code10)
    if ($Mode -eq "code") {
        $pair = New-CodePairing -Code10 $Code10
        $box = Protect-SetupJson -Json $Json -EncKey $pair.Enc -MacKey $pair.Mac -Id $pair.Id
        return @{
            Id = (ConvertTo-EyeGrokB64Url $pair.Id)
            Box = $box
            Code10 = $pair.Code10
            Code11 = $pair.Code11
            Enc = $pair.Enc
            Mac = $pair.Mac
            RawId = $pair.Id
        }
    }
    $pair = New-QrPairing -RelayUrl $RelayUrl
    $box = Protect-SetupJson -Json $Json -EncKey $pair.Enc -MacKey $pair.Mac -Id $pair.Id
    return @{
        Id = (ConvertTo-EyeGrokB64Url $pair.Id)
        Box = $box
        Payload = $pair.Payload
        Secret = $pair.Secret
        Enc = $pair.Enc
        Mac = $pair.Mac
        RawId = $pair.Id
    }
}

Export-ModuleMember -Function @(
    "Get-EyeGrokHome",
    "Get-EyeGrokLicenseUrl",
    "Get-EyeGrokErrorText",
    "Write-InstallerLog",
    "Find-Adb",
    "Invoke-Adb",
    "Install-PlatformTools",
    "Get-FrameState",
    "Get-FrameSerial",
    "Resolve-FrameLink",
    "Connect-Lepton",
    "Find-NewestApk",
    "Install-EyeGrokApk",
    "ConvertTo-SetupJson",
    "Test-SetupValues",
    "Get-SetupKeyError",
    "Test-KeyValue",
    "New-PairingEntry",
    "Remove-PairingEntry",
    "Send-SetupOverAdb",
    "Send-PairOrigin",
    "Format-PairCode",
    "New-SetupPairing",
    "New-EyeGrokQrMatrix",
    "ConvertTo-QrAsciiRows",
    "Get-DammDigit",
    "ConvertTo-EyeGrokB64Url",
    "ConvertFrom-EyeGrokB64Url",
    "Unprotect-EyeGrokPlain",
    "Get-EyeGrokHkdf",
    "New-CodePairing",
    "New-QrPairing"
)
