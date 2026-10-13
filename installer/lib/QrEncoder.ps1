# Copyright (c) Project Nayuki. (MIT License)
# https://www.nayuki.io/page/qr-code-generator-library
# Ported to Windows PowerShell 5.1. Byte mode, error correction M, versions 1-20.
# Boosting to a higher ECC level is off, so the symbol stays at M.
# Copyright 2026 EyeGrok contributors
# SPDX-License-Identifier: Apache-2.0
#
# Permission is hereby granted, free of charge, to any person obtaining a copy of
# this software and associated documentation files (the "Software"), to deal in
# the Software without restriction, including without limitation the rights to
# use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
# the Software, and to permit persons to whom the Software is furnished to do so,
# subject to the following conditions:
# - The above copyright notice and this permission notice shall be included in
#   all copies or substantial portions of the Software.
# - The Software is provided "as is", without warranty of any kind, express or
#   implied, including but not limited to the warranties of merchantability,
#   fitness for a particular purpose and noninfringement. In no event shall the
#   authors or copyright holders be liable for any claim, damages or other
#   liability, whether in an action of contract, tort or otherwise, arising from,
#   out of or in connection with the Software or the use or other dealings in the
#   Software.

# Index 0 is unused. Rows are Low, Medium, Quartile, High. Columns are versions 0-40.
$script:QrEccPerBlock = @(
    @(-1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30),
    @(-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28),
    @(-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30),
    @(-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30)
)
$script:QrEccBlocks = @(
    @(-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4,  4,  4,  4,  4,  6,  6,  6,  6,  7,  8,  8,  9,  9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25),
    @(-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5,  5,  8,  9,  9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49),
    @(-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8,  8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68),
    @(-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81)
)

function Get-QrDiv([int]$a, [int]$b) {
    return [int][Math]::Floor($a / [double]$b)
}

function Get-QrBit([int]$x, [int]$i) {
    return (($x -shr $i) -band 1) -ne 0
}

function Get-QrRawModules([int]$ver) {
    $result = (16 * $ver + 128) * $ver + 64
    if ($ver -ge 2) {
        $numalign = (Get-QrDiv $ver 7) + 2
        $result -= (25 * $numalign - 10) * $numalign - 55
        if ($ver -ge 7) { $result -= 36 }
    }
    return $result
}

function Get-QrDataCodewords([int]$ver, [int]$ecl) {
    $raw = Get-QrDiv (Get-QrRawModules $ver) 8
    return $raw - ($script:QrEccPerBlock[$ecl][$ver] * $script:QrEccBlocks[$ecl][$ver])
}

function Get-QrRsMul([int]$x, [int]$y) {
    $z = 0
    for ($i = 7; $i -ge 0; $i--) {
        $z = ($z -shl 1) -bxor ((($z -shr 7) -band 1) * 0x11D)
        $z = $z -bxor (((($y -shr $i) -band 1) * $x))
        $z = $z -band 0xFF
    }
    return $z
}

function Get-QrRsDivisor([int]$degree) {
    $result = New-Object int[] $degree
    $result[$degree - 1] = 1
    $root = 1
    for ($i = 0; $i -lt $degree; $i++) {
        for ($j = 0; $j -lt $degree; $j++) {
            $result[$j] = Get-QrRsMul $result[$j] $root
            if (($j + 1) -lt $degree) { $result[$j] = $result[$j] -bxor $result[$j + 1] }
        }
        $root = Get-QrRsMul $root 0x02
    }
    return $result
}

function Get-QrRsRemainder([byte[]]$data, [int[]]$divisor) {
    $result = New-Object int[] $divisor.Length
    foreach ($b in $data) {
        $factor = ([int]$b) -bxor $result[0]
        for ($i = 0; $i -lt ($result.Length - 1); $i++) { $result[$i] = $result[$i + 1] }
        $result[$result.Length - 1] = 0
        for ($i = 0; $i -lt $divisor.Length; $i++) {
            $result[$i] = $result[$i] -bxor (Get-QrRsMul $divisor[$i] $factor)
        }
    }
    $bytes = New-Object byte[] $result.Length
    for ($i = 0; $i -lt $result.Length; $i++) { $bytes[$i] = [byte]$result[$i] }
    return $bytes
}

function Add-QrBits([System.Collections.Generic.List[int]]$bb, [int]$val, [int]$n) {
    for ($i = $n - 1; $i -ge 0; $i--) {
        $bb.Add((($val -shr $i) -band 1))
    }
}

function Set-QrFunction([hashtable]$qr, [int]$x, [int]$y, [bool]$dark) {
    $qr.Modules[$y, $x] = $dark
    $qr.IsFunction[$y, $x] = $true
}

function Add-QrFinder([hashtable]$qr, [int]$x, [int]$y) {
    for ($dy = -4; $dy -le 4; $dy++) {
        for ($dx = -4; $dx -le 4; $dx++) {
            $xx = $x + $dx
            $yy = $y + $dy
            if ($xx -ge 0 -and $xx -lt $qr.Size -and $yy -ge 0 -and $yy -lt $qr.Size) {
                $dist = [Math]::Max([Math]::Abs($dx), [Math]::Abs($dy))
                Set-QrFunction $qr $xx $yy (($dist -ne 2) -and ($dist -ne 4))
            }
        }
    }
}

function Add-QrAlignment([hashtable]$qr, [int]$x, [int]$y) {
    for ($dy = -2; $dy -le 2; $dy++) {
        for ($dx = -2; $dx -le 2; $dx++) {
            $dist = [Math]::Max([Math]::Abs($dx), [Math]::Abs($dy))
            Set-QrFunction $qr ($x + $dx) ($y + $dy) ($dist -ne 1)
        }
    }
}

function Get-QrAlignPos([hashtable]$qr) {
    if ($qr.Version -eq 1) { return @() }
    $numalign = (Get-QrDiv $qr.Version 7) + 2
    $step = (Get-QrDiv (($qr.Version * 8) + ($numalign * 3) + 5) (($numalign * 4) - 4)) * 2
    $result = New-Object System.Collections.Generic.List[int]
    for ($i = 0; $i -lt ($numalign - 1); $i++) {
        $result.Add($qr.Size - 7 - ($i * $step))
    }
    $result.Add(6)
    $result.Reverse()
    return $result.ToArray()
}

function Set-QrFormat([hashtable]$qr, [int]$mask) {
    $data = ($qr.FormatBits -shl 3) -bor $mask
    $rem = $data
    for ($i = 0; $i -lt 10; $i++) {
        $rem = ($rem -shl 1) -bxor ((($rem -shr 9) -band 1) * 0x537)
    }
    $bits = (($data -shl 10) -bor ($rem -band 0x3FF)) -bxor 0x5412
    for ($i = 0; $i -lt 6; $i++) { Set-QrFunction $qr 8 $i (Get-QrBit $bits $i) }
    Set-QrFunction $qr 8 7 (Get-QrBit $bits 6)
    Set-QrFunction $qr 8 8 (Get-QrBit $bits 7)
    Set-QrFunction $qr 7 8 (Get-QrBit $bits 8)
    for ($i = 9; $i -lt 15; $i++) { Set-QrFunction $qr (14 - $i) 8 (Get-QrBit $bits $i) }
    for ($i = 0; $i -lt 8; $i++) { Set-QrFunction $qr ($qr.Size - 1 - $i) 8 (Get-QrBit $bits $i) }
    for ($i = 8; $i -lt 15; $i++) { Set-QrFunction $qr 8 ($qr.Size - 15 + $i) (Get-QrBit $bits $i) }
    Set-QrFunction $qr 8 ($qr.Size - 8) $true
}

function Set-QrVersion([hashtable]$qr) {
    if ($qr.Version -lt 7) { return }
    $rem = $qr.Version
    for ($i = 0; $i -lt 12; $i++) {
        $rem = ($rem -shl 1) -bxor ((($rem -shr 11) -band 1) * 0x1F25)
    }
    $bits = ($qr.Version -shl 12) -bor ($rem -band 0xFFF)
    for ($i = 0; $i -lt 18; $i++) {
        $bit = Get-QrBit $bits $i
        $a = $qr.Size - 11 + ($i % 3)
        $b = Get-QrDiv $i 3
        Set-QrFunction $qr $a $b $bit
        Set-QrFunction $qr $b $a $bit
    }
}

function Set-QrFunctions([hashtable]$qr) {
    for ($i = 0; $i -lt $qr.Size; $i++) {
        $dark = (($i % 2) -eq 0)
        Set-QrFunction $qr 6 $i $dark
        Set-QrFunction $qr $i 6 $dark
    }
    Add-QrFinder $qr 3 3
    Add-QrFinder $qr ($qr.Size - 4) 3
    Add-QrFinder $qr 3 ($qr.Size - 4)
    $pos = @(Get-QrAlignPos $qr)
    $n = $pos.Length
    for ($i = 0; $i -lt $n; $i++) {
        for ($j = 0; $j -lt $n; $j++) {
            $skip = (($i -eq 0) -and ($j -eq 0)) -or (($i -eq 0) -and ($j -eq ($n - 1))) -or (($i -eq ($n - 1)) -and ($j -eq 0))
            if (-not $skip) { Add-QrAlignment $qr $pos[$i] $pos[$j] }
        }
    }
    Set-QrFormat $qr 0
    Set-QrVersion $qr
}

function Get-QrEccInterleaved([hashtable]$qr, [byte[]]$data) {
    $ver = $qr.Version
    $ecl = $qr.Ecl
    $numblocks = $script:QrEccBlocks[$ecl][$ver]
    $blockecclen = $script:QrEccPerBlock[$ecl][$ver]
    $rawcodewords = Get-QrDiv (Get-QrRawModules $ver) 8
    $numshort = $numblocks - ($rawcodewords % $numblocks)
    $shortlen = Get-QrDiv $rawcodewords $numblocks
    $rsdiv = Get-QrRsDivisor $blockecclen
    $blocks = @()
    $k = 0
    for ($i = 0; $i -lt $numblocks; $i++) {
        $datLen = $shortlen - $blockecclen
        if ($i -ge $numshort) { $datLen += 1 }
        $dat = New-Object System.Collections.Generic.List[byte]
        for ($n = 0; $n -lt $datLen; $n++) { $dat.Add($data[$k + $n]) }
        $k += $datLen
        $ecc = Get-QrRsRemainder $dat.ToArray() $rsdiv
        if ($i -lt $numshort) { $dat.Add(0) }
        foreach ($e in $ecc) { $dat.Add($e) }
        $blocks += ,$dat.ToArray()
    }
    $result = New-Object System.Collections.Generic.List[byte]
    $width = $blocks[0].Length
    for ($i = 0; $i -lt $width; $i++) {
        for ($j = 0; $j -lt $blocks.Length; $j++) {
            if (($i -ne ($shortlen - $blockecclen)) -or ($j -ge $numshort)) {
                $result.Add($blocks[$j][$i])
            }
        }
    }
    return $result.ToArray()
}

function Set-QrCodewords([hashtable]$qr, [byte[]]$data) {
    $i = 0
    $limit = $data.Length * 8
    for ($right0 = $qr.Size - 1; $right0 -gt 0; $right0 -= 2) {
        $right = $right0
        if ($right -le 6) { $right -= 1 }
        for ($vert = 0; $vert -lt $qr.Size; $vert++) {
            for ($j = 0; $j -lt 2; $j++) {
                $x = $right - $j
                $up = ((($right + 1) -band 2) -eq 0)
                if ($up) { $y = $qr.Size - 1 - $vert } else { $y = $vert }
                if ((-not $qr.IsFunction[$y, $x]) -and ($i -lt $limit)) {
                    $qr.Modules[$y, $x] = Get-QrBit ([int]$data[$i -shr 3]) (7 - ($i -band 7))
                    $i += 1
                }
            }
        }
    }
}

function Use-QrMask([hashtable]$qr, [int]$mask) {
    for ($y = 0; $y -lt $qr.Size; $y++) {
        for ($x = 0; $x -lt $qr.Size; $x++) {
            if ($qr.IsFunction[$y, $x]) { continue }
            $on = $false
            switch ($mask) {
                0 { $on = (($x + $y) % 2) -eq 0 }
                1 { $on = ($y % 2) -eq 0 }
                2 { $on = ($x % 3) -eq 0 }
                3 { $on = (($x + $y) % 3) -eq 0 }
                4 { $on = (((Get-QrDiv $x 3) + (Get-QrDiv $y 2)) % 2) -eq 0 }
                5 { $on = (((($x * $y) % 2) + (($x * $y) % 3)) -eq 0) }
                6 { $on = ((((($x * $y) % 2) + (($x * $y) % 3)) % 2) -eq 0) }
                7 { $on = ((((($x + $y) % 2) + (($x * $y) % 3)) % 2) -eq 0) }
            }
            if ($on) { $qr.Modules[$y, $x] = -not $qr.Modules[$y, $x] }
        }
    }
}

function Add-QrRunHistory($hist, [int]$run, [int]$size) {
    if ($hist[0] -eq 0) { $run += $size }
    for ($i = 6; $i -ge 1; $i--) { $hist[$i] = $hist[$i - 1] }
    $hist[0] = $run
}

function Get-QrFinderPatterns($hist) {
    $n = $hist[1]
    $core = ($n -gt 0) -and ($hist[2] -eq $n) -and ($hist[4] -eq $n) -and ($hist[5] -eq $n) -and ($hist[3] -eq ($n * 3))
    $count = 0
    if ($core -and ($hist[0] -ge ($n * 4)) -and ($hist[6] -ge $n)) { $count += 1 }
    if ($core -and ($hist[6] -ge ($n * 4)) -and ($hist[0] -ge $n)) { $count += 1 }
    return $count
}

function Get-QrLinePenaltyEnd($hist, [bool]$color, [int]$run, [int]$size) {
    if ($color) {
        Add-QrRunHistory $hist $run $size
        $run = 0
    }
    $run += $size
    Add-QrRunHistory $hist $run $size
    return (Get-QrFinderPatterns $hist) * 40
}

function Get-QrPenalty([hashtable]$qr) {
    $result = 0
    $size = $qr.Size
    for ($y = 0; $y -lt $size; $y++) {
        $color = $false
        $run = 0
        $hist = @(0, 0, 0, 0, 0, 0, 0)
        for ($x = 0; $x -lt $size; $x++) {
            if ($qr.Modules[$y, $x] -eq $color) {
                $run += 1
                if ($run -eq 5) { $result += 3 }
                elseif ($run -gt 5) { $result += 1 }
            } else {
                Add-QrRunHistory $hist $run $size
                if (-not $color) { $result += (Get-QrFinderPatterns $hist) * 40 }
                $color = $qr.Modules[$y, $x]
                $run = 1
            }
        }
        $result += Get-QrLinePenaltyEnd $hist $color $run $size
    }
    for ($x = 0; $x -lt $size; $x++) {
        $color = $false
        $run = 0
        $hist = @(0, 0, 0, 0, 0, 0, 0)
        for ($y = 0; $y -lt $size; $y++) {
            if ($qr.Modules[$y, $x] -eq $color) {
                $run += 1
                if ($run -eq 5) { $result += 3 }
                elseif ($run -gt 5) { $result += 1 }
            } else {
                Add-QrRunHistory $hist $run $size
                if (-not $color) { $result += (Get-QrFinderPatterns $hist) * 40 }
                $color = $qr.Modules[$y, $x]
                $run = 1
            }
        }
        $result += Get-QrLinePenaltyEnd $hist $color $run $size
    }
    for ($y = 0; $y -lt ($size - 1); $y++) {
        for ($x = 0; $x -lt ($size - 1); $x++) {
            $v = $qr.Modules[$y, $x]
            if (($v -eq $qr.Modules[$y, ($x + 1)]) -and ($v -eq $qr.Modules[($y + 1), $x]) -and ($v -eq $qr.Modules[($y + 1), ($x + 1)])) {
                $result += 3
            }
        }
    }
    $dark = 0
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            if ($qr.Modules[$y, $x]) { $dark += 1 }
        }
    }
    $total = $size * $size
    $k = (Get-QrDiv (([Math]::Abs($dark * 20 - $total * 10) + $total - 1)) $total) - 1
    $result += $k * 10
    return $result
}

function New-EyeGrokQrMatrix {
    param([Parameter(Mandatory = $true)][string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    if ($bytes.Length -gt 2953) { throw "qr text too long" }
    $ecl = 1
    $formatBits = 0
    $version = 0
    $dataBits = 0
    for ($ver = 1; $ver -le 20; $ver++) {
        $ccbits = 8
        if ($ver -ge 10) { $ccbits = 16 }
        $used = 4 + $ccbits + ($bytes.Length * 8)
        $cap = (Get-QrDataCodewords $ver $ecl) * 8
        if ($used -le $cap) {
            $version = $ver
            $dataBits = $used
            break
        }
    }
    if ($version -eq 0) { throw "qr text does not fit version 20" }
    $bb = New-Object System.Collections.Generic.List[int]
    Add-QrBits $bb 0x4 4
    $ccbits = 8
    if ($version -ge 10) { $ccbits = 16 }
    Add-QrBits $bb $bytes.Length $ccbits
    foreach ($b in $bytes) { Add-QrBits $bb ([int]$b) 8 }
    $capBits = (Get-QrDataCodewords $version $ecl) * 8
    $term = $capBits - $bb.Count
    if ($term -gt 4) { $term = 4 }
    Add-QrBits $bb 0 $term
    $pad = (8 - ($bb.Count % 8)) % 8
    Add-QrBits $bb 0 $pad
    $padByte = 0xEC
    while ($bb.Count -lt $capBits) {
        Add-QrBits $bb $padByte 8
        if ($padByte -eq 0xEC) { $padByte = 0x11 } else { $padByte = 0xEC }
    }
    $codewords = New-Object byte[] ($bb.Count / 8)
    for ($i = 0; $i -lt $bb.Count; $i++) {
        if ($bb[$i] -ne 0) {
            $codewords[$i -shr 3] = [byte]($codewords[$i -shr 3] -bor (1 -shl (7 - ($i -band 7))))
        }
    }
    $size = $version * 4 + 17
    $qr = @{
        Version = $version
        Size = $size
        Ecl = $ecl
        FormatBits = $formatBits
        Modules = (New-Object "bool[,]" $size, $size)
        IsFunction = (New-Object "bool[,]" $size, $size)
    }
    Set-QrFunctions $qr
    $all = Get-QrEccInterleaved $qr $codewords
    Set-QrCodewords $qr $all
    $best = 0
    $bestScore = [int]::MaxValue
    for ($m = 0; $m -lt 8; $m++) {
        Use-QrMask $qr $m
        Set-QrFormat $qr $m
        $score = Get-QrPenalty $qr
        if ($score -lt $bestScore) {
            $best = $m
            $bestScore = $score
        }
        Use-QrMask $qr $m
    }
    Use-QrMask $qr $best
    Set-QrFormat $qr $best
    return ,$qr.Modules
}

function ConvertTo-QrAsciiRows {
    param([Parameter(Mandatory = $true)][bool[,]]$Matrix)
    $n = $Matrix.GetLength(0)
    $rows = New-Object System.Collections.Generic.List[string]
    for ($y = 0; $y -lt $n; $y++) {
        $sb = New-Object System.Text.StringBuilder
        for ($x = 0; $x -lt $n; $x++) {
            if ($Matrix[$y, $x]) { [void]$sb.Append("#") } else { [void]$sb.Append(".") }
        }
        $rows.Add($sb.ToString())
    }
    return ,$rows.ToArray()
}
