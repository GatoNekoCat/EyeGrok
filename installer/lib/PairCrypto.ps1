# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
# Pairing crypto for Windows PowerShell 5.1. AES-256-CBC plus HMAC-SHA256.
# HKDF-SHA256 is RFC 5869. An empty salt is 32 zero bytes. No AesGcm.

$script:EyeGrokDamm = @(
    @(0, 3, 1, 7, 5, 9, 8, 6, 4, 2),
    @(7, 0, 9, 2, 1, 5, 4, 8, 6, 3),
    @(4, 2, 0, 6, 8, 7, 1, 3, 5, 9),
    @(1, 7, 5, 0, 9, 8, 3, 4, 2, 6),
    @(6, 1, 2, 3, 0, 4, 5, 9, 7, 8),
    @(3, 6, 7, 4, 2, 0, 9, 5, 8, 1),
    @(5, 8, 6, 9, 7, 2, 0, 1, 3, 4),
    @(8, 9, 4, 5, 3, 6, 2, 0, 1, 7),
    @(9, 4, 3, 8, 6, 1, 7, 2, 0, 5),
    @(2, 5, 8, 1, 4, 3, 6, 7, 9, 0)
)

function ConvertTo-EyeGrokB64Url {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    $text = [Convert]::ToBase64String($Bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
    return $text
}

function ConvertFrom-EyeGrokB64Url {
    param([Parameter(Mandatory = $true)][string]$Text)
    $padded = $Text.Replace("-", "+").Replace("_", "/")
    $mod = $padded.Length % 4
    if ($mod -eq 2) { $padded = $padded + "==" }
    elseif ($mod -eq 3) { $padded = $padded + "=" }
    elseif ($mod -eq 1) { throw "bad b64url" }
    return ,([Convert]::FromBase64String($padded))
}

function New-EyeGrokRandomBytes {
    param([Parameter(Mandatory = $true)][int]$Count)
    $bytes = New-Object byte[] $Count
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }
    return ,$bytes
}

function Get-EyeGrokHkdf {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Ikm,
        [Parameter(Mandatory = $true)][string]$Info,
        [int]$Length = 32,
        [byte[]]$Salt
    )
    if ($null -eq $Salt -or $Salt.Length -eq 0) {
        $Salt = New-Object byte[] 32
    }
    $extract = New-Object System.Security.Cryptography.HMACSHA256
    $extract.Key = $Salt
    try {
        $prk = $extract.ComputeHash($Ikm)
    } finally {
        $extract.Dispose()
    }
    $infoBytes = [System.Text.Encoding]::UTF8.GetBytes($Info)
    $blocks = [int][Math]::Ceiling($Length / 32.0)
    $prev = New-Object byte[] 0
    $okm = New-Object System.IO.MemoryStream
    $expand = New-Object System.Security.Cryptography.HMACSHA256
    $expand.Key = $prk
    try {
        for ($i = 1; $i -le $blocks; $i++) {
            $block = New-Object System.IO.MemoryStream
            if ($prev.Length -gt 0) { $block.Write($prev, 0, $prev.Length) }
            if ($infoBytes.Length -gt 0) { $block.Write($infoBytes, 0, $infoBytes.Length) }
            $block.WriteByte([byte]$i)
            $prev = $expand.ComputeHash($block.ToArray())
            $okm.Write($prev, 0, $prev.Length)
        }
    } finally {
        $expand.Dispose()
    }
    $all = $okm.ToArray()
    $out = New-Object byte[] $Length
    [Array]::Copy($all, $out, $Length)
    return ,$out
}

function Get-DammDigit {
    param([Parameter(Mandatory = $true)][string]$Digits)
    $interim = 0
    foreach ($ch in $Digits.ToCharArray()) {
        $d = [int]$ch - 48
        if ($d -lt 0 -or $d -gt 9) { throw "damm digit" }
        $interim = $script:EyeGrokDamm[$interim][$d]
    }
    return $interim
}

function Test-DammCode {
    param([Parameter(Mandatory = $true)][string]$Code)
    return (Get-DammDigit $Code) -eq 0
}

function Get-EyeGrokPbkdf2 {
    param([Parameter(Mandatory = $true)][string]$Code10)
    $password = [System.Text.Encoding]::UTF8.GetBytes($Code10)
    $salt = [System.Text.Encoding]::UTF8.GetBytes("eyegrok-pair-v1")
    $sha = [System.Security.Cryptography.HashAlgorithmName]::SHA256
    $kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($password, $salt, 100000, $sha)
    try {
        return ,$kdf.GetBytes(32)
    } finally {
        $kdf.Dispose()
    }
}

function Get-EyeGrokMac {
    param(
        [Parameter(Mandatory = $true)][byte[]]$MacKey,
        [Parameter(Mandatory = $true)][byte[]]$Id,
        [Parameter(Mandatory = $true)][byte[]]$InitVector,
        [Parameter(Mandatory = $true)][byte[]]$Cipher
    )
    $ms = New-Object System.IO.MemoryStream
    $label = [System.Text.Encoding]::ASCII.GetBytes("EG1")
    $ms.Write($label, 0, $label.Length)
    $ms.WriteByte(0)
    $ms.Write($Id, 0, $Id.Length)
    $ms.Write($InitVector, 0, $InitVector.Length)
    $ms.Write($Cipher, 0, $Cipher.Length)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = $MacKey
    try {
        return ,$hmac.ComputeHash($ms.ToArray())
    } finally {
        $hmac.Dispose()
    }
}

function Test-EyeGrokMacEqual {
    param([byte[]]$Left, [byte[]]$Right)
    if ($Left -eq $null -or $Right -eq $null) { return $false }
    $diff = $Left.Length -bxor $Right.Length
    $n = $Left.Length
    if ($Right.Length -lt $n) { $n = $Right.Length }
    for ($i = 0; $i -lt $n; $i++) {
        $diff = $diff -bor ($Left[$i] -bxor $Right[$i])
    }
    return ($diff -eq 0)
}

function Protect-EyeGrokPlain {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Plain,
        [Parameter(Mandatory = $true)][byte[]]$EncKey,
        [Parameter(Mandatory = $true)][byte[]]$MacKey,
        [Parameter(Mandatory = $true)][byte[]]$Id,
        [byte[]]$InitVector
    )
    if ($null -eq $InitVector -or $InitVector.Length -eq 0) {
        $InitVector = New-EyeGrokRandomBytes 16
    }
    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $EncKey
        $aes.IV = $InitVector
        $encryptor = $aes.CreateEncryptor()
        try {
            $ct = $encryptor.TransformFinalBlock($Plain, 0, $Plain.Length)
        } finally {
            $encryptor.Dispose()
        }
    } finally {
        $aes.Dispose()
    }
    $mac = Get-EyeGrokMac $MacKey $Id $InitVector $ct
    return @{
        V = 1
        Iv = $InitVector
        Ct = $ct
        Mac = $mac
    }
}

function Unprotect-EyeGrokPlain {
    param(
        [Parameter(Mandatory = $true)][byte[]]$InitVector,
        [Parameter(Mandatory = $true)][byte[]]$Cipher,
        [Parameter(Mandatory = $true)][byte[]]$Mac,
        [Parameter(Mandatory = $true)][byte[]]$EncKey,
        [Parameter(Mandatory = $true)][byte[]]$MacKey,
        [Parameter(Mandatory = $true)][byte[]]$Id
    )
    $expect = Get-EyeGrokMac $MacKey $Id $InitVector $Cipher
    if (-not (Test-EyeGrokMacEqual $expect $Mac)) { return $null }
    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $EncKey
        $aes.IV = $InitVector
        $decryptor = $aes.CreateDecryptor()
        try {
            return ,$decryptor.TransformFinalBlock($Cipher, 0, $Cipher.Length)
        } finally {
            $decryptor.Dispose()
        }
    } finally {
        $aes.Dispose()
    }
}

function ConvertTo-EyeGrokBox {
    param([Parameter(Mandatory = $true)]$Sealed)
    return @{
        v = 1
        iv = (ConvertTo-EyeGrokB64Url $Sealed.Iv)
        ct = (ConvertTo-EyeGrokB64Url $Sealed.Ct)
        mac = (ConvertTo-EyeGrokB64Url $Sealed.Mac)
    }
}

function New-QrPairing {
    param(
        [Parameter(Mandatory = $true)][string]$RelayUrl,
        [byte[]]$Id,
        [byte[]]$Secret
    )
    if ($Id -eq $null -or $Id.Length -eq 0) { $Id = New-EyeGrokRandomBytes 16 }
    if ($Secret -eq $null -or $Secret.Length -eq 0) { $Secret = New-EyeGrokRandomBytes 32 }
    $enc = Get-EyeGrokHkdf -Ikm $Secret -Info "enc" -Length 32
    $mac = Get-EyeGrokHkdf -Ikm $Secret -Info "mac" -Length 32
    $payload = "EG1|" + $RelayUrl + "|" + (ConvertTo-EyeGrokB64Url $Id) + "|" + (ConvertTo-EyeGrokB64Url $Secret)
    return @{
        Id = $Id
        Secret = $Secret
        Enc = $enc
        Mac = $mac
        Payload = $payload
    }
}

function New-CodePairing {
    param(
        [string]$Code10,
        [byte[]]$Master
    )
    if ($Master -eq $null -or $Master.Length -eq 0) {
        if (-not $Code10) {
            $digits = New-Object System.Text.StringBuilder
            $raw = New-EyeGrokRandomBytes 10
            foreach ($b in $raw) {
                [void]$digits.Append([string]($b % 10))
            }
            $Code10 = $digits.ToString()
        }
        $Master = Get-EyeGrokPbkdf2 $Code10
    }
    $idFull = Get-EyeGrokHkdf -Ikm $Master -Info "id" -Length 32
    $id = New-Object byte[] 16
    [Array]::Copy($idFull, $id, 16)
    $enc = Get-EyeGrokHkdf -Ikm $Master -Info "enc" -Length 32
    $mac = Get-EyeGrokHkdf -Ikm $Master -Info "mac" -Length 32
    $check = Get-DammDigit $Code10
    $code11 = $Code10 + [string]$check
    return @{
        Code10 = $Code10
        Code11 = $code11
        Master = $Master
        Id = $id
        Enc = $enc
        Mac = $mac
    }
}

function Protect-SetupJson {
    param(
        [Parameter(Mandatory = $true)][string]$Json,
        [Parameter(Mandatory = $true)][byte[]]$EncKey,
        [Parameter(Mandatory = $true)][byte[]]$MacKey,
        [Parameter(Mandatory = $true)][byte[]]$Id,
        [byte[]]$InitVector
    )
    $plain = [System.Text.Encoding]::UTF8.GetBytes($Json)
    $sealed = Protect-EyeGrokPlain -Plain $plain -EncKey $EncKey -MacKey $MacKey -Id $Id -InitVector $InitVector
    return ConvertTo-EyeGrokBox $sealed
}
