# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Writes fixed pairing vectors as spaced hex. Byte-identical on every run.

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $RepoRoot "installer\lib\PairCrypto.ps1")

function Format-SpacedHex {
    param([byte[]]$Bytes)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($b in $Bytes) { [void]$parts.Add($b.ToString("x2")) }
    return ($parts -join " ")
}

$json = '{"webhookUrl":"https://example.com/hook","webhookKey":"0123456789abcdef","relayUrl":"https://example.com/relay","relayReadKey":"fedcba9876543210"}'
$code10 = "1234567890"
$iv = New-Object byte[] 16
$pair = New-CodePairing -Code10 $code10
$box = Protect-SetupJson -Json $json -EncKey $pair.Enc -MacKey $pair.Mac -Id $pair.Id -InitVector $iv
$ct = ConvertFrom-EyeGrokB64Url $box.ct
$tag = ConvertFrom-EyeGrokB64Url $box.mac
$qrId = New-Object byte[] 16
$qrSecret = New-Object byte[] 32
for ($i = 0; $i -lt 16; $i++) { $qrId[$i] = [byte]($i + 1) }
for ($i = 0; $i -lt 32; $i++) { $qrSecret[$i] = [byte]($i + 32) }
$qr = New-QrPairing -RelayUrl "https://example.com/relay" -Id $qrId -Secret $qrSecret
$qrBox = Protect-SetupJson -Json $json -EncKey $qr.Enc -MacKey $qr.Mac -Id $qr.Id -InitVector $iv
$qrCt = ConvertFrom-EyeGrokB64Url $qrBox.ct
$qrTag = ConvertFrom-EyeGrokB64Url $qrBox.mac
# Parentheses are required. The comma operator binds tighter than +, so an
# ungrouped "a " + $b, "c " + $d collapses into one space-joined string.
$lines = @(
    ("code10 " + $code10),
    ("damm " + (Get-DammDigit $code10)),
    ("code11 " + $pair.Code11),
    ("iv " + (Format-SpacedHex $iv)),
    ("id " + (Format-SpacedHex $pair.Id)),
    ("master " + (Format-SpacedHex $pair.Master)),
    ("enc " + (Format-SpacedHex $pair.Enc)),
    ("mac " + (Format-SpacedHex $pair.Mac)),
    ("ct " + (Format-SpacedHex $ct)),
    ("tag " + (Format-SpacedHex $tag)),
    ("json " + (Format-SpacedHex ([System.Text.Encoding]::UTF8.GetBytes($json)))),
    ("qr-id " + (Format-SpacedHex $qr.Id)),
    ("qr-secret " + (Format-SpacedHex $qr.Secret)),
    ("qr-enc " + (Format-SpacedHex $qr.Enc)),
    ("qr-mac " + (Format-SpacedHex $qr.Mac)),
    ("qr-ct " + (Format-SpacedHex $qrCt)),
    ("qr-tag " + (Format-SpacedHex $qrTag)),
    ("qr-payload " + (Format-SpacedHex ([System.Text.Encoding]::UTF8.GetBytes($qr.Payload))))
)
$outDir = Join-Path $RepoRoot "unity\Assets\EyeGrok\Tests\EditMode\Fixtures"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$path = Join-Path $outDir "pair-vectors.txt"
$nl = [string]([char]10)
$text = ($lines -join $nl) + $nl
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false))
Write-Output "PAIR_VECTORS=ok"
exit 0
