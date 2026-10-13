# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Downloads ZXing.Net 0.16.11, checks SHA-256, and extracts the netstandard2.0 dll.
# The dll is gitignored. A mismatch deletes it and exits 1.

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PinPath = Join-Path $PSScriptRoot "deps.sha256"
$DestDir = Join-Path $RepoRoot "unity\Assets\Plugins\ZXing"
$Dest = Join-Path $DestDir "zxing.dll"
$Url = "https://www.nuget.org/api/v2/package/ZXing.Net/0.16.11"
$Version = "0.16.11"

function Get-PinnedHash([string]$fileName) {
    foreach ($line in [System.IO.File]::ReadAllLines($PinPath)) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith("#")) { continue }
        $parts = $t -split "\s+"
        if ($parts.Length -lt 2) { continue }
        if ($parts[$parts.Length - 1] -ne $fileName) { continue }
        if ($parts[0] -match "^[0-9a-fA-F]{64}$") { return $parts[0].ToLower() }
    }
    return $null
}

function Remove-Dll {
    if (Test-Path -LiteralPath $Dest) {
        Remove-Item -LiteralPath $Dest -Force
    }
}

$nupkgHash = Get-PinnedHash ("ZXing.Net." + $Version + ".nupkg")
$dllHash = Get-PinnedHash "zxing.dll"
if (-not $nupkgHash -or -not $dllHash) {
    Write-Error "deps.sha256 is missing a ZXing hash"
    exit 1
}

[void][System.Reflection.Assembly]::Load("System.IO.Compression.FileSystem, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")

$work = Join-Path $env:TEMP "eyegrok-zxing-fetch"
if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $work | Out-Null
$nupkg = Join-Path $work ("ZXing.Net." + $Version + ".nupkg")

Write-Host "ZXING_FETCH=start"
& curl.exe -L --fail --retry 3 --output $nupkg $Url
if ($LASTEXITCODE -ne 0) {
    Write-Error "zxing download failed"
    exit 1
}

$actualNupkg = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA256).Hash.ToLower()
if ($actualNupkg -ne $nupkgHash) {
    Remove-Dll
    Write-Error "zxing nupkg hash mismatch"
    exit 1
}

$extract = Join-Path $work "pkg"
New-Item -ItemType Directory -Force -Path $extract | Out-Null
[System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $extract)
$packed = Join-Path $extract "lib\netstandard2.0\zxing.dll"
if (-not (Test-Path -LiteralPath $packed)) {
    Remove-Dll
    Write-Error "zxing dll missing in nupkg"
    exit 1
}

$actualDll = (Get-FileHash -LiteralPath $packed -Algorithm SHA256).Hash.ToLower()
if ($actualDll -ne $dllHash) {
    Remove-Dll
    Write-Error "zxing dll hash mismatch"
    exit 1
}

New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
[System.IO.File]::Copy($packed, $Dest, $true)
$meta = Join-Path $DestDir "zxing.dll.meta"
if (-not (Test-Path -LiteralPath $meta)) {
    $guid = [guid]::NewGuid().ToString("N")
    $metaText = @"
fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Android:
      enabled: 0
      settings:
        CPU: ARM64
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 1
      settings:
        DefaultValueInitialized: true
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"@
    [System.IO.File]::WriteAllText($meta, $metaText.Replace("`r`n", "`n"))
}

Remove-Item -LiteralPath $work -Recurse -Force
Write-Host "ZXING_CHECK=ok"
exit 0
