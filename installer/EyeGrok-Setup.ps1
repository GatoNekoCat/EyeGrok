# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Windows installer shell. Logic lives in lib/Core.psm1.
# Headless: values are one JSON line on stdin, never arguments or environment.

param(
    [switch]$Headless,
    [switch]$AcceptPlatformToolsLicense,
    [switch]$AllowLoopbackHttp,
    [switch]$CapturePairing,
    [switch]$PushOrigin,
    [ValidateSet("frame", "full")]
    [string]$Mode = "frame",
    [ValidateSet("qr", "code", "usb", "all")]
    [string]$PairMode = "usb",
    [string]$ApkPath,
    [string]$FrameHost,
    [switch]$FramePrompt,
    [switch]$FrameUsb
)

$ErrorActionPreference = "Stop"

if ($ExecutionContext.SessionState.LanguageMode -ne "FullLanguage") {
    Write-Output "PowerShell is in Constrained Language Mode. EyeGrok Setup cannot run."
    exit 2
}

Import-Module (Join-Path $PSScriptRoot "lib\Core.psm1") -Force

$script:LiveRelay = $null
$script:LiveKey = $null
$script:LiveIds = New-Object System.Collections.Generic.List[string]

function ConvertTo-HexBytes {
    param([byte[]]$Bytes)
    if ($null -eq $Bytes) { return "" }
    return ([BitConverter]::ToString($Bytes)).Replace("-", "").ToLower()
}

function Clear-LivePairing {
    if ($script:LiveRelay -and $script:LiveKey) {
        foreach ($id in @($script:LiveIds)) {
            try { Remove-PairingEntry -RelayUrl $script:LiveRelay -PairKey $script:LiveKey -Id $id | Out-Null } catch { }
        }
    }
    $script:LiveIds.Clear()
    $script:LiveRelay = $null
    $script:LiveKey = $null
}

function Find-InstallerApk {
    if ($ApkPath) { return (Find-NewestApk -Directory $null -Chosen $ApkPath) }
    $beside = Find-NewestApk -Directory $PSScriptRoot
    if ($beside) { return $beside }
    $builds = Join-Path (Split-Path -Parent $PSScriptRoot) "Builds"
    return (Find-NewestApk -Directory $builds)
}

function Read-SetupLine {
    $line = [Console]::In.ReadLine()
    if (-not $line) { return $null }
    return ($line | ConvertFrom-Json)
}

function Publish-OnePairing {
    param($Made, [string]$Relay, [string]$PairKey)
    $post = New-PairingEntry -RelayUrl $Relay -PairKey $PairKey -Id $Made.Id -Box $Made.Box
    if ($post.Status -eq 201) {
        $script:LiveRelay = $Relay
        $script:LiveKey = $PairKey
        $script:LiveIds.Add($Made.Id)
        return "OK"
    }
    if ($post.Status -eq 401 -or $post.Status -eq 403) { return "E_PAIR_KEY" }
    return "E_PAIR_OFF"
}

function Write-Capture {
    param($Made, [string]$Kind)
    if (-not $CapturePairing) { return }
    if ($Kind -eq "qr") {
        Write-Output ("CAPTURE qr-id=" + (ConvertTo-HexBytes ([byte[]]$Made.RawId)))
        Write-Output ("CAPTURE qr-secret=" + (ConvertTo-HexBytes ([byte[]]$Made.Secret)))
    }
    if ($Kind -eq "code") {
        Write-Output ("CAPTURE code-id=" + (ConvertTo-HexBytes ([byte[]]$Made.RawId)))
        Write-Output ("CAPTURE code10=" + $Made.Code10)
    }
}

function Stop-Headless {
    param([int]$Code)
    $script:HeadlessCode = $Code
}

function Invoke-Headless {
    $script:HeadlessCode = 2
    Write-Output "STEP tools"
    $adb = Find-Adb
    if (-not $adb) {
        Write-Output ("LICENSE " + (Get-EyeGrokLicenseUrl))
        $accepted = $false
        if ($AcceptPlatformToolsLicense) { $accepted = $true }
        $tools = Install-PlatformTools -Accepted:$accepted
        if ($tools -ne "OK") {
            Write-InstallerLog "tools" 2 $tools
            Write-Output $tools
            Write-Output (Get-EyeGrokErrorText -Key $tools)
            Stop-Headless 2
            return
        }
    }
    Write-InstallerLog "tools" 0 ""

    Write-Output "STEP frame"
    if ($FrameUsb) {
        $link = Resolve-FrameLink -UseUsb
    } else {
        $link = Resolve-FrameLink -HostName $FrameHost
        if (-not $link.Ok -and $link.Signed -and $FramePrompt) {
            Write-Output "PROMPT ip"
            $answer = [Console]::In.ReadLine()
            if ($null -eq $answer) { $answer = "" }
            $answer = $answer.Trim()
            if ($answer.Length -gt 0) {
                $link = Resolve-FrameLink -IpAddress $answer -IpOnly
            }
        }
    }
    if (-not $link.Ok) {
        $frameKey = "E_NO_FRAME"
        if ($link.Key) { $frameKey = [string]$link.Key }
        Write-InstallerLog "frame" 2 $frameKey
        Write-Output $frameKey
        $detail = $null
        if ($frameKey -eq "E_WIFI_NOHOST") { $detail = $FrameHost }
        Write-Output (Get-EyeGrokErrorText -Key $frameKey -Detail $detail)
        Stop-Headless 2
        return
    }
    Write-InstallerLog "frame" 0 ""
    if ($Mode -eq "frame") {
        Write-Output "OK"
        Stop-Headless 0
        return
    }

    Write-Output "STEP install"
    $apk = Find-InstallerApk
    if (-not $apk) {
        Write-InstallerLog "install" 2 "E_NO_APK"
        Write-Output "E_NO_APK"
        Write-Output (Get-EyeGrokErrorText -Key "E_NO_APK")
        Stop-Headless 2
        return
    }
    $installed = Install-EyeGrokApk -ApkPath $apk
    if ($installed.Key -ne "OK") {
        Write-Output $installed.Key
        Write-Output (Get-EyeGrokErrorText -Key $installed.Key -Detail $installed.Detail)
        Stop-Headless 2
        return
    }

    Write-Output "STEP values"
    $values = Read-SetupLine
    $bad = Test-SetupValues -Values $values -AllowLoopbackHttp:$AllowLoopbackHttp
    if ($bad) {
        Write-InstallerLog "values" 2 "bad"
        Write-Output "BAD values"
        Stop-Headless 2
        return
    }
    $json = ConvertTo-SetupJson $values
    $relay = [string]$values.relayUrl
    $pairKey = [string]$values.pairKey
    $needsRelay = $false
    if ($PairMode -eq "qr" -or $PairMode -eq "code" -or $PairMode -eq "all") { $needsRelay = $true }
    if ($needsRelay -and -not (Test-KeyValue $pairKey)) {
        Write-InstallerLog "pair" 2 "E_PAIR_KEY"
        Write-Output "E_PAIR_KEY"
        Write-Output (Get-EyeGrokErrorText -Key "E_PAIR_KEY")
        Stop-Headless 2
        return
    }

    $doQr = $false
    $doCode = $false
    $doUsb = $false
    if ($PairMode -eq "qr" -or $PairMode -eq "all") { $doQr = $true }
    if ($PairMode -eq "code" -or $PairMode -eq "all") { $doCode = $true }
    if ($PairMode -eq "usb" -or $PairMode -eq "all") { $doUsb = $true }

    if ($doQr -or $doCode) { Write-Output "STEP pair" }
    if ($doQr) {
        $made = New-SetupPairing -Json $json -RelayUrl $relay -Mode "qr"
        $key = Publish-OnePairing -Made $made -Relay $relay -PairKey $pairKey
        if ($key -ne "OK") {
            Write-InstallerLog "pair" 2 $key
            Write-Output $key
            Write-Output (Get-EyeGrokErrorText -Key $key)
            Stop-Headless 2
            return
        }
        Write-Capture $made "qr"
        Write-InstallerLog "pair" 0 "qr"
    }
    if ($doCode) {
        if ($PushOrigin) {
            $origin = Send-PairOrigin -RelayUrl $relay
            if ($origin -ne "OK") {
                Write-Output $origin
                Write-Output (Get-EyeGrokErrorText -Key $origin)
                Stop-Headless 2
                return
            }
        }
        $made = New-SetupPairing -Json $json -RelayUrl $relay -Mode "code"
        $key = Publish-OnePairing -Made $made -Relay $relay -PairKey $pairKey
        if ($key -ne "OK") {
            Write-InstallerLog "pair" 2 $key
            Write-Output $key
            Write-Output (Get-EyeGrokErrorText -Key $key)
            Stop-Headless 2
            return
        }
        Write-Capture $made "code"
        Write-InstallerLog "pair" 0 "code"
    }
    if ($doUsb) {
        Write-Output "STEP import"
        $pushed = Send-SetupOverAdb -Json $json
        if ($pushed.Stdout -match "STDIN_SHA256=([0-9a-fA-F]+)") {
            Write-Output ("STDIN_SHA256=" + $Matches[1].ToLower())
        }
        if ($pushed.Key -ne "OK") {
            Write-Output "E_IMPORT"
            Write-Output (Get-EyeGrokErrorText -Key "E_IMPORT")
            Stop-Headless 2
            return
        }
        Write-Output "Imported"
    }
    Write-Output "OK"
    Stop-Headless 0
    return
}

if ($Headless) {
    $script:HeadlessCode = 2
    try {
        Invoke-Headless
    } catch {
        Write-InstallerLog "fail" 1 "E_DOWNLOAD"
        Write-Output "E_DOWNLOAD"
        Write-Output (Get-EyeGrokErrorText -Key "E_DOWNLOAD")
        $script:HeadlessCode = 2
    } finally {
        if (-not $CapturePairing) { Clear-LivePairing }
    }
    exit $script:HeadlessCode
}

if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
    Write-Output "EyeGrok Setup needs powershell -STA. Use EyeGrok-Setup.cmd."
    exit 2
}

[void][System.Reflection.Assembly]::Load("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")
[void][System.Reflection.Assembly]::Load("System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:Boxes = @{}
$script:QrBitmap = $null
$script:CountdownLabel = $null
$script:SecondsLeft = 0
$script:ChosenApk = $null
$script:Status = $null

function Set-Status([string]$Text) {
    if ($script:Status) { $script:Status.Text = $Text }
}

function Read-BoxValues {
    return [pscustomobject]@{
        webhookUrl = $script:Boxes["webhookUrl"].Text
        webhookKey = $script:Boxes["webhookKey"].Text
        relayUrl = $script:Boxes["relayUrl"].Text
        relayReadKey = $script:Boxes["relayReadKey"].Text
        pairKey = $script:Boxes["pairKey"].Text
    }
}

function Clear-Boxes {
    foreach ($name in @($script:Boxes.Keys)) {
        $script:Boxes[$name].Text = ""
    }
}

function New-QrBitmap($Matrix) {
    $n = $Matrix.GetLength(0)
    $scale = 6
    $quiet = 4
    $size = ($n + (2 * $quiet)) * $scale
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.Clear([System.Drawing.Color]::White)
    $brush = [System.Drawing.Brushes]::Black
    for ($y = 0; $y -lt $n; $y++) {
        for ($x = 0; $x -lt $n; $x++) {
            if ($Matrix[$y, $x]) {
                $graphics.FillRectangle($brush, ($x + $quiet) * $scale, ($y + $quiet) * $scale, $scale, $scale)
            }
        }
    }
    $graphics.Dispose()
    return $bmp
}

function Start-Countdown([System.Windows.Forms.Label]$Label) {
    $script:CountdownLabel = $Label
    $script:SecondsLeft = 300
    $Label.Text = "300"
    if (-not $script:PairTimer) {
        $script:PairTimer = New-Object System.Windows.Forms.Timer
        $script:PairTimer.Interval = 1000
        $script:PairTimer.Add_Tick({
            $script:SecondsLeft = $script:SecondsLeft - 1
            if ($script:CountdownLabel) { $script:CountdownLabel.Text = [string]$script:SecondsLeft }
            if ($script:SecondsLeft -le 0) {
                $script:PairTimer.Stop()
                Clear-LivePairing
                Set-Status "Expired"
            }
        })
    }
    $script:PairTimer.Stop()
    $script:PairTimer.Start()
}

function New-LabeledBox([System.Windows.Forms.Control]$Parent, [string]$Name, [string]$Caption, [int]$Top) {
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Caption
    $label.Location = New-Object System.Drawing.Point 8, $Top
    $label.Size = New-Object System.Drawing.Size 160, 24
    $box = New-Object System.Windows.Forms.TextBox
    $box.Location = New-Object System.Drawing.Point 172, $Top
    $box.Size = New-Object System.Drawing.Size 420, 24
    $box.UseSystemPasswordChar = $true
    $Parent.Controls.Add($label)
    $Parent.Controls.Add($box)
    $script:Boxes[$Name] = $box
}

$script:Form = New-Object System.Windows.Forms.Form
$script:Form.Text = "EyeGrok Setup"
$script:Form.ClientSize = New-Object System.Drawing.Size 920, 640
$script:Form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$script:Form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$script:Form.MaximizeBox = $false

$steps = New-Object System.Windows.Forms.ListBox
$steps.Location = New-Object System.Drawing.Point 8, 8
$steps.Size = New-Object System.Drawing.Size 140, 560
[void]$steps.Items.Add("1 Tools")
[void]$steps.Items.Add("2 Frame")
[void]$steps.Items.Add("3 Install")
[void]$steps.Items.Add("4 Values")
[void]$steps.Items.Add("5 Pair")
$script:Form.Controls.Add($steps)

$content = New-Object System.Windows.Forms.Panel
$content.Location = New-Object System.Drawing.Point 156, 8
$content.Size = New-Object System.Drawing.Size 756, 560
$script:Form.Controls.Add($content)

$script:Status = New-Object System.Windows.Forms.Label
$script:Status.Location = New-Object System.Drawing.Point 8, 580
$script:Status.Size = New-Object System.Drawing.Size 900, 48
$script:Form.Controls.Add($script:Status)

function Show-Tools {
    $content.Controls.Clear()
    $steps.SelectedIndex = 0
    $notice = New-Object System.Windows.Forms.Label
    $egHome = Get-EyeGrokHome
    $url = Get-EyeGrokLicenseUrl
    $notice.Text = "EyeGrok needs Google's Android SDK Platform-Tools (adb). They are not part of EyeGrok. They are downloaded from dl.google.com under Google's terms: " + $url + ". Accept to download about 8 MB to " + $egHome + "."
    $notice.Location = New-Object System.Drawing.Point 8, 8
    $notice.Size = New-Object System.Drawing.Size 720, 120
    $content.Controls.Add($notice)
    $accept = New-Object System.Windows.Forms.Button
    $accept.Text = "Accept and download"
    $accept.Location = New-Object System.Drawing.Point 8, 140
    $accept.Size = New-Object System.Drawing.Size 180, 32
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = "Cancel"
    $cancel.Location = New-Object System.Drawing.Point 200, 140
    $cancel.Size = New-Object System.Drawing.Size 120, 32
    $next = New-Object System.Windows.Forms.Button
    $next.Text = "Next"
    $next.Location = New-Object System.Drawing.Point 340, 140
    $next.Size = New-Object System.Drawing.Size 120, 32
    $content.Controls.Add($accept)
    $content.Controls.Add($cancel)
    $content.Controls.Add($next)
    if (Find-Adb) {
        Set-Status "Platform-tools are already available."
    } else {
        Set-Status "Accept the platform-tools terms to download adb."
    }
    $accept.Add_Click({
        if (Find-Adb) {
            Set-Status "Platform-tools are already available."
            return
        }
        Set-Status "Downloading platform-tools."
        $result = Install-PlatformTools -Accepted
        if ($result -eq "OK") {
            Set-Status "Platform-tools are ready."
        } else {
            Set-Status (Get-EyeGrokErrorText -Key $result)
        }
    })
    $cancel.Add_Click({ $script:Form.Close() })
    $next.Add_Click({ Show-Frame })
}

function Show-FrameOutcome {
    param($Link)
    if ($Link.Ok) {
        if ($script:FramePromptPanel) { $script:FramePromptPanel.Visible = $false }
        Set-Status "Frame is ready."
        return
    }
    if ($script:FramePromptPanel) { $script:FramePromptPanel.Visible = $true }
    $key = "E_NO_FRAME"
    if ($Link.Key) { $key = [string]$Link.Key }
    $detail = $null
    if ($key -eq "E_WIFI_NOHOST" -and $script:FrameHostBox) { $detail = $script:FrameHostBox.Text.Trim() }
    Set-Status (Get-EyeGrokErrorText -Key $key -Detail $detail)
    if ($script:FrameIpBox) { $script:FrameIpBox.Focus() }
}

function Show-Frame {
    $content.Controls.Clear()
    $steps.SelectedIndex = 1
    $retry = New-Object System.Windows.Forms.Button
    $retry.Text = "Retry"
    $retry.Location = New-Object System.Drawing.Point 8, 8
    $retry.Size = New-Object System.Drawing.Size 120, 32
    $next = New-Object System.Windows.Forms.Button
    $next.Text = "Next"
    $next.Location = New-Object System.Drawing.Point 140, 8
    $next.Size = New-Object System.Drawing.Size 120, 32
    $hostLabel = New-Object System.Windows.Forms.Label
    $hostLabel.Text = "Headset name"
    $hostLabel.Location = New-Object System.Drawing.Point 8, 52
    $hostLabel.Size = New-Object System.Drawing.Size 140, 24
    $hostBox = New-Object System.Windows.Forms.TextBox
    $hostBox.Text = "frame"
    $hostBox.Location = New-Object System.Drawing.Point 152, 48
    $hostBox.Size = New-Object System.Drawing.Size 280, 24
    $hostBox.MaxLength = 253
    $script:FrameHostBox = $hostBox
    $content.Controls.Add($retry)
    $content.Controls.Add($next)
    $content.Controls.Add($hostLabel)
    $content.Controls.Add($hostBox)

    $panel = New-Object System.Windows.Forms.Panel
    $panel.Location = New-Object System.Drawing.Point 8, 88
    $panel.Size = New-Object System.Drawing.Size 720, 220
    $panel.Visible = $false
    $script:FramePromptPanel = $panel
    $hint = New-Object System.Windows.Forms.Label
    $hint.Text = "On the Frame, open Quick Settings, click your Wi-Fi network, and read the IP address. Steam Settings, then Internet, shows the same address, for example 192.168.4.80."
    $hint.Location = New-Object System.Drawing.Point 0, 0
    $hint.Size = New-Object System.Drawing.Size 700, 48
    $ipLabel = New-Object System.Windows.Forms.Label
    $ipLabel.Text = "Headset IP"
    $ipLabel.Location = New-Object System.Drawing.Point 0, 56
    $ipLabel.Size = New-Object System.Drawing.Size 140, 24
    $ipBox = New-Object System.Windows.Forms.TextBox
    $ipBox.Location = New-Object System.Drawing.Point 144, 52
    $ipBox.Size = New-Object System.Drawing.Size 280, 24
    $ipBox.MaxLength = 15
    $script:FrameIpBox = $ipBox
    $connect = New-Object System.Windows.Forms.Button
    $connect.Text = "Connect"
    $connect.Location = New-Object System.Drawing.Point 0, 92
    $connect.Size = New-Object System.Drawing.Size 120, 32
    $usb = New-Object System.Windows.Forms.Button
    $usb.Text = "Use USB instead"
    $usb.Location = New-Object System.Drawing.Point 132, 92
    $usb.Size = New-Object System.Drawing.Size 160, 32
    $panel.Controls.Add($hint)
    $panel.Controls.Add($ipLabel)
    $panel.Controls.Add($ipBox)
    $panel.Controls.Add($connect)
    $panel.Controls.Add($usb)
    $content.Controls.Add($panel)

    $retry.Add_Click({
        Show-FrameOutcome (Resolve-FrameLink -HostName $script:FrameHostBox.Text)
    })
    $connect.Add_Click({
        Show-FrameOutcome (Resolve-FrameLink -IpAddress $script:FrameIpBox.Text -IpOnly)
    })
    $usb.Add_Click({
        Show-FrameOutcome (Resolve-FrameLink -UseUsb)
    })
    $next.Add_Click({ Show-Install })
    Show-FrameOutcome (Resolve-FrameLink -HostName $script:FrameHostBox.Text)
}

function Show-Install {
    $content.Controls.Clear()
    $steps.SelectedIndex = 2
    $browse = New-Object System.Windows.Forms.Button
    $browse.Text = "Browse"
    $browse.Location = New-Object System.Drawing.Point 8, 8
    $browse.Size = New-Object System.Drawing.Size 120, 32
    $install = New-Object System.Windows.Forms.Button
    $install.Text = "Install"
    $install.Location = New-Object System.Drawing.Point 140, 8
    $install.Size = New-Object System.Drawing.Size 120, 32
    $next = New-Object System.Windows.Forms.Button
    $next.Text = "Next"
    $next.Location = New-Object System.Drawing.Point 272, 8
    $next.Size = New-Object System.Drawing.Size 120, 32
    $remove = New-Object System.Windows.Forms.Button
    $remove.Text = "Remove the old EyeGrok"
    $remove.Location = New-Object System.Drawing.Point 8, 48
    $remove.Size = New-Object System.Drawing.Size 220, 32
    $remove.Visible = $false
    $content.Controls.Add($browse)
    $content.Controls.Add($install)
    $content.Controls.Add($next)
    $content.Controls.Add($remove)
    $script:RemoveButton = $remove
    $browse.Add_Click({
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Filter = "APK (*.apk)|*.apk"
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $script:ChosenApk = $dialog.FileName
            Set-Status "APK selected."
        }
    })
    $install.Add_Click({
        $apk = $script:ChosenApk
        if (-not $apk) { $apk = Find-InstallerApk }
        if (-not $apk) {
            Set-Status (Get-EyeGrokErrorText -Key "E_NO_APK")
            return
        }
        $result = Install-EyeGrokApk -ApkPath $apk
        if ($result.Key -eq "OK") {
            Set-Status "Installed."
            $script:RemoveButton.Visible = $false
        } else {
            Set-Status (Get-EyeGrokErrorText -Key $result.Key -Detail $result.Detail)
            if ($result.Detail -eq "UPDATE_INCOMPATIBLE") { $script:RemoveButton.Visible = $true }
        }
    })
    $remove.Add_Click({
        $serial = Get-FrameSerial
        Invoke-Adb -ArgumentList @("-s", $serial, "uninstall", "io.github.eyegrok.ping") | Out-Null
        Set-Status "Uninstall requested."
    })
    $next.Add_Click({ Show-Values })
}

function Show-Values {
    $content.Controls.Clear()
    $steps.SelectedIndex = 3
    New-LabeledBox $content "webhookUrl" "Webhook URL" 8
    New-LabeledBox $content "webhookKey" "Webhook key" 40
    New-LabeledBox $content "relayUrl" "Relay URL" 72
    New-LabeledBox $content "relayReadKey" "Relay read key" 104
    New-LabeledBox $content "pairKey" "Pair key" 136
    $paste = New-Object System.Windows.Forms.Button
    $paste.Text = "Paste blob"
    $paste.Location = New-Object System.Drawing.Point 8, 176
    $paste.Size = New-Object System.Drawing.Size 140, 32
    $clearClip = New-Object System.Windows.Forms.Button
    $clearClip.Text = "Clear clipboard"
    $clearClip.Location = New-Object System.Drawing.Point 160, 176
    $clearClip.Size = New-Object System.Drawing.Size 140, 32
    $next = New-Object System.Windows.Forms.Button
    $next.Text = "Next"
    $next.Location = New-Object System.Drawing.Point 312, 176
    $next.Size = New-Object System.Drawing.Size 120, 32
    $content.Controls.Add($paste)
    $content.Controls.Add($clearClip)
    $content.Controls.Add($next)
    $paste.Add_Click({
        try {
            $raw = [System.Windows.Forms.Clipboard]::GetText()
            $obj = $raw | ConvertFrom-Json
            foreach ($name in @("webhookUrl", "webhookKey", "relayUrl", "relayReadKey")) {
                $value = [string]$obj.$name
                if ($value) { $script:Boxes[$name].Text = $value }
            }
            Set-Status "Pasted."
        } catch {
            Set-Status "Could not read the clipboard."
        }
    })
    $clearClip.Add_Click({
        [System.Windows.Forms.Clipboard]::Clear()
        Set-Status "Clipboard cleared."
    })
    $next.Add_Click({ Show-Pair })
    Set-Status "Enter values. They stay in these fields only."
}

function Show-Pair {
    $content.Controls.Clear()
    $steps.SelectedIndex = 4
    $tabs = New-Object System.Windows.Forms.TabControl
    $tabs.Location = New-Object System.Drawing.Point 8, 8
    $tabs.Size = New-Object System.Drawing.Size 730, 540
    $qrPage = New-Object System.Windows.Forms.TabPage
    $qrPage.Text = "QR"
    $codePage = New-Object System.Windows.Forms.TabPage
    $codePage.Text = "Code"
    $usbPage = New-Object System.Windows.Forms.TabPage
    $usbPage.Text = "USB push"
    $tabs.TabPages.Add($qrPage)
    $tabs.TabPages.Add($codePage)
    $tabs.TabPages.Add($usbPage)
    $content.Controls.Add($tabs)

    $newQr = New-Object System.Windows.Forms.Button
    $newQr.Text = "New QR"
    $newQr.Location = New-Object System.Drawing.Point 8, 8
    $newQr.Size = New-Object System.Drawing.Size 120, 32
    $qrCount = New-Object System.Windows.Forms.Label
    $qrCount.Location = New-Object System.Drawing.Point 140, 12
    $qrCount.Size = New-Object System.Drawing.Size 80, 24
    $qrCount.Text = "300"
    $picture = New-Object System.Windows.Forms.PictureBox
    $picture.Location = New-Object System.Drawing.Point 8, 48
    $picture.Size = New-Object System.Drawing.Size 480, 480
    $picture.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $qrPage.Controls.Add($newQr)
    $qrPage.Controls.Add($qrCount)
    $qrPage.Controls.Add($picture)
    $script:QrPicture = $picture
    $script:QrCount = $qrCount

    $never = New-Object System.Windows.Forms.CheckBox
    $never.Text = "This headset has never been paired"
    $never.Location = New-Object System.Drawing.Point 8, 8
    $never.Size = New-Object System.Drawing.Size 360, 24
    $newCode = New-Object System.Windows.Forms.Button
    $newCode.Text = "New code"
    $newCode.Location = New-Object System.Drawing.Point 8, 40
    $newCode.Size = New-Object System.Drawing.Size 120, 32
    $codeCount = New-Object System.Windows.Forms.Label
    $codeCount.Location = New-Object System.Drawing.Point 140, 44
    $codeCount.Size = New-Object System.Drawing.Size 80, 24
    $codeLabel = New-Object System.Windows.Forms.Label
    $codeLabel.Location = New-Object System.Drawing.Point 8, 88
    $codeLabel.Size = New-Object System.Drawing.Size 680, 64
    $codeLabel.Font = New-Object System.Drawing.Font "Segoe UI", 28
    $codePage.Controls.Add($never)
    $codePage.Controls.Add($newCode)
    $codePage.Controls.Add($codeCount)
    $codePage.Controls.Add($codeLabel)
    $script:NeverPaired = $never
    $script:CodeLabel = $codeLabel
    $script:CodeCount = $codeCount

    $push = New-Object System.Windows.Forms.Button
    $push.Text = "Push"
    $push.Location = New-Object System.Drawing.Point 8, 8
    $push.Size = New-Object System.Drawing.Size 120, 32
    $usbPage.Controls.Add($push)

    $newQr.Add_Click({
        $values = Read-BoxValues
        $bad = Test-SetupValues -Values $values -AllowLoopbackHttp:$AllowLoopbackHttp
        if ($bad -or -not (Test-KeyValue ([string]$values.pairKey))) {
            Set-Status "Check the values."
            return
        }
        Clear-LivePairing
        $json = ConvertTo-SetupJson $values
        $made = New-SetupPairing -Json $json -RelayUrl ([string]$values.relayUrl) -Mode "qr"
        $key = Publish-OnePairing -Made $made -Relay ([string]$values.relayUrl) -PairKey ([string]$values.pairKey)
        if ($key -ne "OK") {
            Set-Status (Get-EyeGrokErrorText -Key $key)
            return
        }
        $matrix = New-EyeGrokQrMatrix -Text $made.Payload
        if ($script:QrBitmap) { $script:QrBitmap.Dispose() }
        $script:QrBitmap = New-QrBitmap $matrix
        $script:QrPicture.Image = $script:QrBitmap
        Start-Countdown $script:QrCount
        Set-Status "QR ready."
    })
    $newCode.Add_Click({
        $values = Read-BoxValues
        $bad = Test-SetupValues -Values $values -AllowLoopbackHttp:$AllowLoopbackHttp
        if ($bad -or -not (Test-KeyValue ([string]$values.pairKey))) {
            Set-Status "Check the values."
            return
        }
        if ($script:NeverPaired.Checked) {
            $origin = Send-PairOrigin -RelayUrl ([string]$values.relayUrl)
            if ($origin -ne "OK") {
                Set-Status (Get-EyeGrokErrorText -Key $origin)
                return
            }
        }
        Clear-LivePairing
        $json = ConvertTo-SetupJson $values
        $made = New-SetupPairing -Json $json -RelayUrl ([string]$values.relayUrl) -Mode "code"
        $key = Publish-OnePairing -Made $made -Relay ([string]$values.relayUrl) -PairKey ([string]$values.pairKey)
        if ($key -ne "OK") {
            Set-Status (Get-EyeGrokErrorText -Key $key)
            return
        }
        $script:CodeLabel.Text = Format-PairCode $made.Code11
        Start-Countdown $script:CodeCount
        Set-Status "Code ready."
    })
    $push.Add_Click({
        $values = Read-BoxValues
        $bad = Test-SetupValues -Values $values -AllowLoopbackHttp:$AllowLoopbackHttp
        if ($bad) {
            Set-Status "Check the values."
            return
        }
        $json = ConvertTo-SetupJson $values
        $pushed = Send-SetupOverAdb -Json $json
        if ($pushed.Key -eq "OK") { Set-Status "Imported" } else { Set-Status "Not imported" }
    })
    Set-Status "Choose a pairing tab."
}

$script:Form.Add_FormClosing({
    if ($script:PairTimer) { $script:PairTimer.Stop() }
    Clear-LivePairing
    Clear-Boxes
    if ($script:QrBitmap) { $script:QrBitmap.Dispose(); $script:QrBitmap = $null }
})

Show-Tools
[System.Windows.Forms.Application]::Run($script:Form)
exit 0
