param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$iconDir = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $root "ClubTimerXbox\Assets\AppIcon"
} else {
    $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $iconDir | Out-Null

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2

    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    return $path
}

function New-GamepadPath {
    param([float]$S)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath

    function P([float]$x, [float]$y) {
        return [System.Drawing.PointF]::new($x * $S, $y * $S)
    }

    $path.StartFigure()
    $path.AddBezier((P 425 690), (P 450 575), (P 470 505), (P 540 500))
    $path.AddLine((P 540 500), (P 630 500))
    $path.AddBezier((P 630 500), (P 650 500), (P 650 520), (P 670 520))
    $path.AddLine((P 670 520), (P 745 520))
    $path.AddBezier((P 745 520), (P 765 520), (P 765 500), (P 785 500))
    $path.AddLine((P 785 500), (P 875 500))
    $path.AddBezier((P 875 500), (P 945 505), (P 965 575), (P 1000 735))
    $path.AddBezier((P 1000 735), (P 1015 805), (P 975 855), (P 915 845))
    $path.AddBezier((P 915 845), (P 865 836), (P 850 760), (P 820 735))
    $path.AddLine((P 820 735), (P 610 735))
    $path.AddBezier((P 610 735), (P 580 760), (P 565 836), (P 515 845))
    $path.AddBezier((P 515 845), (P 455 855), (P 415 805), (P 425 690))
    $path.CloseFigure()

    return $path
}

function New-AppIconBitmap {
    param([int]$Size)

    $scale = $Size / 1024.0
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 0, 0, 0))

        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $black = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Black)

        try {
            $console = New-RoundedRectanglePath `
                -X (260 * $scale) `
                -Y (200 * $scale) `
                -Width (255 * $scale) `
                -Height (620 * $scale) `
                -Radius (28 * $scale)
            $graphics.FillPath($white, $console)
            $console.Dispose()

            $power = New-Object System.Drawing.RectangleF(
                [float](307 * $scale),
                [float](285 * $scale),
                [float](42 * $scale),
                [float](42 * $scale)
            )
            $graphics.FillEllipse($black, $power)

            $slot = New-RoundedRectanglePath `
                -X (308 * $scale) `
                -Y (465 * $scale) `
                -Width (40 * $scale) `
                -Height (220 * $scale) `
                -Radius (20 * $scale)
            $graphics.FillPath($black, $slot)
            $slot.Dispose()

            $gamepad = New-GamepadPath -S $scale
            $graphics.FillPath($white, $gamepad)
            $gamepad.Dispose()

            $leftStick = New-Object System.Drawing.RectangleF(
                [float](575 * $scale),
                [float](610 * $scale),
                [float](82 * $scale),
                [float](82 * $scale)
            )
            $rightStick = New-Object System.Drawing.RectangleF(
                [float](770 * $scale),
                [float](610 * $scale),
                [float](82 * $scale),
                [float](82 * $scale)
            )
            $graphics.FillEllipse($black, $leftStick)
            $graphics.FillEllipse($black, $rightStick)

            $centerCut = New-Object System.Drawing.Drawing2D.GraphicsPath
            $centerCut.AddPolygon([System.Drawing.PointF[]]@(
                [System.Drawing.PointF]::new(662 * $scale, 702 * $scale),
                [System.Drawing.PointF]::new(760 * $scale, 702 * $scale),
                [System.Drawing.PointF]::new(808 * $scale, 805 * $scale),
                [System.Drawing.PointF]::new(613 * $scale, 805 * $scale)
            ))
            $graphics.FillPath($black, $centerCut)
            $centerCut.Dispose()
        }
        finally {
            $white.Dispose()
            $black.Dispose()
        }

        return $bitmap
    }
    catch {
        $graphics.Dispose()
        $bitmap.Dispose()
        throw
    }
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return $stream.ToArray()
}

function Save-IcoFile {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $entries = @()

    foreach ($size in $Sizes) {
        $bitmap = New-AppIconBitmap -Size $size

        try {
            $pngBytes = Convert-BitmapToPngBytes -Bitmap $bitmap
            $entries += [PSCustomObject]@{
                Size = $size
                Bytes = $pngBytes
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $fileStream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter($fileStream)

    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$entries.Count)

        $offset = 6 + ($entries.Count * 16)

        foreach ($entry in $entries) {
            $sizeByte = if ($entry.Size -ge 256) { 0 } else { $entry.Size }

            $writer.Write([byte]$sizeByte)
            $writer.Write([byte]$sizeByte)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$entry.Bytes.Length)
            $writer.Write([UInt32]$offset)

            $offset += $entry.Bytes.Length
        }

        foreach ($entry in $entries) {
            $writer.Write([byte[]]$entry.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $fileStream.Dispose()
    }
}

$pngPath = Join-Path $iconDir "clubtimer-icon-1024.png"
$icoPath = Join-Path $iconDir "clubtimer.ico"

$preview = New-AppIconBitmap -Size 1024
try {
    $preview.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $preview.Dispose()
}

Save-IcoFile -Path $icoPath -Sizes @(256, 128, 64, 48, 32, 16)

Write-Host "Generated:"
Write-Host $pngPath
Write-Host $icoPath
