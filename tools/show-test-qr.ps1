# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Shows the fixed non-secret QR EYEGROK-P0CAM-TEST. -Dump prints the matrix and exits.

param(
    [switch]$Dump
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $RepoRoot "installer\lib\QrEncoder.ps1")

$matrix = New-EyeGrokQrMatrix -Text "EYEGROK-P0CAM-TEST"
$rows = ConvertTo-QrAsciiRows -Matrix $matrix
if ($Dump) {
    foreach ($row in $rows) {
        Write-Output $row
    }
    exit 0
}

if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
    Write-Error "Run with powershell -STA -File tools/show-test-qr.ps1"
    exit 1
}

[void][System.Reflection.Assembly]::Load("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")
[void][System.Reflection.Assembly]::Load("System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")

$n = $matrix.GetLength(0)
$scale = 8
$quiet = 4
$size = ($n + (2 * $quiet)) * $scale
$bmp = New-Object System.Drawing.Bitmap $size, $size
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
$graphics.Clear([System.Drawing.Color]::White)
$brush = [System.Drawing.Brushes]::Black
for ($y = 0; $y -lt $n; $y++) {
    for ($x = 0; $x -lt $n; $x++) {
        if ($matrix[$y, $x]) {
            $graphics.FillRectangle($brush, ($x + $quiet) * $scale, ($y + $quiet) * $scale, $scale, $scale)
        }
    }
}
$graphics.Dispose()

$form = New-Object System.Windows.Forms.Form
$form.Text = "EyeGrok P0-cam test QR"
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$box = New-Object System.Windows.Forms.PictureBox
$box.Image = $bmp
$box.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::AutoSize
$form.ClientSize = $box.Size
$form.Controls.Add($box)
[System.Windows.Forms.Application]::Run($form)
$bmp.Dispose()
exit 0
