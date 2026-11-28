param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/CommonWeaponIcons'),
    [string]$PreviewFile
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force

function Paint-Shape($Canvas, [string]$HexColor, [int[]]$Coordinates) {
    $points = for ($index = 0; $index -lt $Coordinates.Count; $index += 2) {
        [Drawing.Point]::new($Coordinates[$index], $Coordinates[$index + 1])
    }
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#' + $HexColor))
    try { $Canvas.FillPolygon($brush, [Drawing.Point[]]$points) }
    finally { $brush.Dispose() }
}

function Paint-Pixel($Canvas, [string]$HexColor, [int]$X, [int]$Y, [int]$Width = 1, [int]$Height = 1) {
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#' + $HexColor))
    try { $Canvas.FillRectangle($brush, $X, $Y, $Width, $Height) }
    finally { $brush.Dispose() }
}

$ink = '22212C'
$steel = '9BABB9'
$silver = 'D9DCE2'
$darkMetal = '556779'
$bronze = 'C88E4B'
$wood = '875335'
$lightWood = 'D09353'
$binding = 'E9C48A'

$icons = [ordered]@{
    'WornSword' = {
        Paint-Shape $canvas $ink @(3,26, 9,20, 8,18, 10,16, 14,17, 24,6, 29,3, 29,10, 17,22, 17,25, 15,27, 11,25, 6,29, 3,29)
        Paint-Shape $canvas $darkMetal @(13,19, 25,7, 28,5, 27,10, 17,21, 15,22)
        Paint-Shape $canvas $silver @(14,18, 25,7, 28,5, 26,8, 16,20)
        Paint-Pixel $canvas '776C6F' 20 12
        Paint-Pixel $canvas '776C6F' 23 9
        Paint-Shape $canvas $bronze @(9,18, 10,17, 17,23, 16,25, 14,24, 13,23)
        Paint-Shape $canvas $wood @(11,21, 13,23, 6,28, 4,26)
        Paint-Pixel $canvas $binding 5 26 2 1
        Paint-Pixel $canvas $binding 8 23 2 1
    }
    'WoodenSpear' = {
        Paint-Shape $canvas $ink @(2,27, 24,7, 24,4, 29,2, 29,8, 26,11, 5,30, 2,30)
        Paint-Shape $canvas $wood @(3,27, 24,8, 26,10, 5,29, 3,29)
        Paint-Shape $canvas $lightWood @(3,27, 24,8, 25,9, 5,28)
        Paint-Shape $canvas $darkMetal @(23,8, 24,5, 28,3, 28,8, 25,11)
        Paint-Shape $canvas $silver @(24,6, 28,3, 26,7, 24,8)
        Paint-Pixel $canvas $binding 21 10 2 2
        Paint-Pixel $canvas $binding 19 12 2 2
    }
    'HuntingBow' = {
        Paint-Shape $canvas $ink @(9,3, 14,2, 20,4, 25,8, 28,15, 27,20, 23,25, 17,28, 10,30, 5,28, 7,24, 13,23, 19,20, 22,16, 21,11, 17,8, 11,7, 8,6)
        Paint-Shape $canvas $wood @(11,4, 14,3, 20,6, 24,9, 27,15, 26,19, 22,24, 17,27, 10,29, 7,27, 9,25, 14,25, 20,21, 23,17, 23,13, 21,10, 17,7, 12,6)
        Paint-Shape $canvas $lightWood @(12,4, 15,4, 20,6, 23,8, 26,15, 25,19, 21,23, 16,26, 10,28, 9,27, 15,26, 20,23, 24,19, 25,15, 22,9, 18,6)
        Paint-Shape $canvas $binding @(10,5, 11,5, 8,26, 7,27)
        Paint-Shape $canvas $binding @(22,13, 25,12, 26,15, 23,16)
        Paint-Pixel $canvas $ink 24 14 2 1
    }
    'Mace' = {
        Paint-Shape $canvas $ink @(3,26, 16,14, 15,12, 16,7, 19,7, 20,3, 23,6, 28,4, 28,9, 30,12, 27,14, 26,18, 21,18, 19,17, 7,29, 3,29)
        Paint-Shape $canvas $wood @(4,26, 17,14, 19,16, 7,28, 4,28)
        Paint-Shape $canvas $lightWood @(4,26, 17,14, 18,15, 6,27)
        Paint-Shape $canvas $darkMetal @(16,11, 17,8, 19,8, 21,5, 23,7, 27,6, 27,10, 29,12, 26,13, 25,17, 21,17, 19,15)
        Paint-Shape $canvas $steel @(18,10, 21,7, 25,7, 27,10, 25,13, 22,14, 19,13)
        Paint-Pixel $canvas $silver 20 8 3 1
        Paint-Pixel $canvas $silver 18 11
        Paint-Pixel $canvas $bronze 6 26 2 1
    }
    'Dagger' = {
        Paint-Shape $canvas $ink @(8,25, 12,20, 11,17, 13,15, 16,15, 18,9, 24,4, 25,7, 24,13, 21,18, 22,21, 20,23, 16,22, 13,28, 9,29)
        Paint-Shape $canvas $darkMetal @(16,17, 19,9, 24,6, 23,13, 20,19)
        Paint-Shape $canvas $silver @(17,16, 19,10, 24,6, 21,11, 20,15, 18,17)
        Paint-Shape $canvas $bronze @(12,17, 14,16, 21,20, 20,22, 16,21)
        Paint-Shape $canvas $wood @(13,20, 16,21, 13,27, 10,28, 9,26)
        Paint-Pixel $canvas $binding 11 25 3 1
        Paint-Pixel $canvas $binding 12 23 3 1
    }
}

foreach ($entry in $icons.GetEnumerator()) {
    $bitmap = [Drawing.Bitmap]::new(32, 32, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $canvas = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
        & $entry.Value
        $bitmap.Save((Join-Path $OutputDirectory ($entry.Key + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $canvas.Dispose()
        $bitmap.Dispose()
    }
}

if ($PreviewFile) {
    $sheet = [Drawing.Bitmap]::new(200, 64, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $canvas = [Drawing.Graphics]::FromImage($sheet)
    try {
        $canvas.Clear([Drawing.ColorTranslator]::FromHtml('#303746'))
        $canvas.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $canvas.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
        $index = 0
        foreach ($entry in $icons.GetEnumerator()) {
            $icon = [Drawing.Image]::FromFile((Join-Path $OutputDirectory ($entry.Key + '.png')))
            try {
                $canvas.DrawImage($icon, 4 + 40 * $index, 2, 32, 32)
                $canvas.DrawImage($icon, 8 + 40 * $index, 37, 24, 24)
            }
            finally { $icon.Dispose() }
            $index++
        }
        $sheet.Save($PreviewFile, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $canvas.Dispose()
        $sheet.Dispose()
    }
}
