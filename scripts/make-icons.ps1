param([string]$Source = (Join-Path $PSScriptRoot '..\resources\app.png'))

Add-Type -AssemblyName System.Drawing

function Get-PngBytes([System.Drawing.Image]$SourceImage, [int]$Size) {
    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object System.IO.MemoryStream
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.DrawImage($SourceImage, 0, 0, $Size, $Size)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-Ico([string]$Path, [int[]]$Sizes, [System.Drawing.Image]$SourceImage) {
    $images = @()
    foreach ($size in $Sizes) {
        $images += ,(Get-PngBytes $SourceImage $size)
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    $writer = New-Object System.IO.BinaryWriter -ArgumentList $stream
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$images.Count)
        $offset = 6 + (16 * $images.Count)
        for ($index = 0; $index -lt $images.Count; $index++) {
            $size = $Sizes[$index]
            $dimension = if ($size -ge 256) { 0 } else { $size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$images[$index].Length)
            $writer.Write([UInt32]$offset)
            $offset += $images[$index].Length
        }

        foreach ($image in $images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$sourceImage = [System.Drawing.Image]::FromFile($Source)
try {
    Write-Ico (Join-Path $PSScriptRoot '..\resources\app.ico') @(256, 64, 48, 32, 16) $sourceImage
    Write-Ico (Join-Path $PSScriptRoot '..\resources\small.ico') @(32, 24, 20, 16) $sourceImage
}
finally {
    $sourceImage.Dispose()
}
