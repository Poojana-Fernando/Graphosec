param(
    [string]$SourceLogo = (Join-Path $PSScriptRoot "logo-exe.png"),
    [string]$OutputIcon = (Join-Path $PSScriptRoot "app.ico"),
    [double]$FillRatio = 1.0,
    [ValidateSet("Full", "Content")]
    [string]$TrimMode = "Full",
    [int]$AlphaThreshold = 16,
    [int]$LuminanceThreshold = 40
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Test-HasTransparentPixels {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [int]$Threshold
    )

    for ($y = 0; $y -lt $Bitmap.Height; $y += [Math]::Max(1, [int]($Bitmap.Height / 32))) {
        for ($x = 0; $x -lt $Bitmap.Width; $x += [Math]::Max(1, [int]($Bitmap.Width / 32))) {
            if ($Bitmap.GetPixel($x, $y).A -le $Threshold) {
                return $true
            }
        }
    }

    return $false
}

function Get-TrimmedBitmap {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [int]$AlphaThreshold,
        [int]$LuminanceThreshold,
        [switch]$UseLuminance
    )

    $minX = $Bitmap.Width
    $minY = $Bitmap.Height
    $maxX = -1
    $maxY = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            $pixel = $Bitmap.GetPixel($x, $y)
            $isVisible = if ($UseLuminance) {
                (0.299 * $pixel.R + 0.587 * $pixel.G + 0.114 * $pixel.B) -gt $LuminanceThreshold
            } else {
                $pixel.A -gt $AlphaThreshold
            }

            if ($isVisible) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }

    if ($maxX -lt $minX -or $maxY -lt $minY) {
        throw "No visible logo pixels found."
    }

    $width = $maxX - $minX + 1
    $height = $maxY - $minY + 1
    $trimmed = $Bitmap.Clone(
        [System.Drawing.Rectangle]::new($minX, $minY, $width, $height),
        $Bitmap.PixelFormat)

    Write-Host "Trimmed logo bounds: ${width}x${height} (from $($Bitmap.Width)x$($Bitmap.Height))"
    return $trimmed
}

$loaded = [System.Drawing.Bitmap]::FromFile($SourceLogo)

if ($TrimMode -eq "Full") {
    $source = $loaded
    Write-Host "Using full logo frame: $($source.Width)x$($source.Height)"
} else {
    $useLuminance = -not (Test-HasTransparentPixels -Bitmap $loaded -Threshold $AlphaThreshold)
    if ($useLuminance) {
        Write-Host "Using luminance trim for opaque logo."
    }

    $source = Get-TrimmedBitmap -Bitmap $loaded -AlphaThreshold $AlphaThreshold -LuminanceThreshold $LuminanceThreshold -UseLuminance:$useLuminance
    $loaded.Dispose()
}

$sizes = @(16, 32, 48, 64, 128, 256)
$memStream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($memStream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
$imageData = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $canvas = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $maxDim = [int]($size * $FillRatio)
    $scale = [Math]::Min($maxDim / $source.Width, $maxDim / $source.Height)
    $newW = [Math]::Max(1, [int][Math]::Round($source.Width * $scale))
    $newH = [Math]::Max(1, [int][Math]::Round($source.Height * $scale))
    $x = [int][Math]::Round(($size - $newW) / 2.0)
    $y = [int][Math]::Round(($size - $newH) / 2.0)
    $graphics.DrawImage($source, $x, $y, $newW, $newH)
    $graphics.Dispose()

    $pngStream = New-Object System.IO.MemoryStream
    $canvas.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageData.Add($pngStream.ToArray())
    $pngStream.Dispose()
    $canvas.Dispose()

    $writer.Write([byte]($size -band 0xFF))
    $writer.Write([byte](($size -shr 8) -band 0xFF))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$imageData[$imageData.Count - 1].Length)
    $writer.Write([uint32]$offset)
    $offset += $imageData[$imageData.Count - 1].Length
}

foreach ($data in $imageData) {
    $writer.Write($data)
}

[System.IO.File]::WriteAllBytes($OutputIcon, $memStream.ToArray())
$writer.Dispose()
$memStream.Dispose()
$source.Dispose()

Write-Host "Generated $OutputIcon from $SourceLogo (trim mode $TrimMode, fill ratio $FillRatio, sizes: $($sizes -join ', '))"
