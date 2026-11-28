param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/RareWeaponIcons'),
    [string]$PreviewFile
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WandererPixelTools.ps1')
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$palette += @{
    rare = '7ABFFF'; badge = '172438'; azure = '5E82C0'; cyan = '88DCEA'; frost = 'D8F8FF'
    storm = '6D67BB'; bolt = 'E1D7FF'; tin = '739AA9'; brass = 'D18D4F'; ash = '523F50'
    oak = 'AD6956'; cinder = '993D4A'; magma = 'EB6441'; furnace = 'FFD481'
}

$icons = [ordered]@{
    'FrostSpear' = {
        Paint-Polygon $canvas ink @(2,28, 21,9, 20,6, 24,5, 27,2, 30,3, 29,6, 30,10, 25,11, 6,29, 2,30)
        Paint-Polygon $canvas tin @(3,28, 22,9, 24,10, 6,28, 3,29)
        Paint-Polygon $canvas frost @(5,27, 22,10, 24,10, 6,28)
        Paint-Polygon $canvas azure @(22,7, 25,6, 28,3, 29,4, 27,8, 29,9, 25,10, 23,13)
        Paint-Polygon $canvas cyan @(23,7, 28,4, 26,8, 28,9, 24,9, 23,11)
        Paint-Block $canvas frost 25 6 2 1
        Paint-Block $canvas frost 19 12 2 2
        Paint-Block $canvas cyan 21 4 1 1
        Paint-Block $canvas azure 28 13 1 2
    }
    'LightningSword' = {
        Paint-Polygon $canvas ink @(3,27, 9,20, 8,17, 12,15, 15,17, 20,10, 17,9, 25,4, 29,2, 27,9, 25,13, 27,15, 21,16, 18,22, 18,25, 15,27, 11,24, 7,29, 3,30)
        Paint-Polygon $canvas storm @(14,19, 21,11, 20,9, 28,4, 26,11, 24,13, 25,14, 20,15, 17,21)
        Paint-Polygon $canvas bolt @(16,18, 22,11, 20,10, 28,4, 25,10, 23,12, 24,13, 20,14)
        Paint-Polygon $canvas brass @(10,17, 12,16, 17,21, 17,25, 15,25, 13,21)
        Paint-Polygon $canvas ash @(10,21, 13,23, 7,28, 4,28)
        Paint-Block $canvas azure 23 17 2 1
        Paint-Block $canvas bolt 15 10 1 1
    }
    'BlastBow' = {
        Paint-Polygon $canvas ink @(5,6, 10,3, 17,3, 21,7, 25,8, 28,13, 28,18, 25,22, 20,25, 16,29, 10,30, 5,27, 8,23, 12,23, 16,21, 21,17, 21,12, 16,8, 10,8)
        Paint-Polygon $canvas oak @(7,6, 12,4, 17,5, 20,8, 24,9, 27,14, 27,18, 24,21, 19,24, 15,28, 10,29, 7,27, 9,24, 13,25, 18,22, 22,19, 23,14, 22,11, 17,7, 12,6)
        Paint-Polygon $canvas frost @(9,6, 10,6, 10,24, 9,26)
        Paint-Polygon $canvas cinder @(22,13, 25,9, 28,8, 28,12, 30,14, 27,16, 27,19, 24,18, 22,20, 21,17, 19,15)
        Paint-Polygon $canvas magma @(23,13, 27,10, 27,13, 28,14, 26,16, 26,18, 23,17, 22,19, 22,16, 21,15)
        Paint-Polygon $canvas furnace @(23,15, 26,12, 26,14, 27,15, 25,16, 24,18, 24,16)
    }
    'ShadowDagger' = {
        Paint-Polygon $canvas storm @(5,12, 10,6, 18,4, 22,6, 16,7, 11,10, 10,14, 7,16)
        Paint-Polygon $canvas ink @(8,25, 12,20, 11,18, 13,15, 16,16, 19,11, 27,5, 28,8, 26,13, 23,18, 20,19, 22,22, 20,24, 16,22, 13,28, 9,29)
        Paint-Polygon $canvas deep @(16,17, 20,11, 27,6, 26,10, 23,15, 19,19)
        Paint-Polygon $canvas bolt @(18,16, 21,11, 27,6, 25,9, 22,14, 20,16)
        Paint-Polygon $canvas azure @(12,18, 14,16, 21,21, 20,23, 16,21)
        Paint-Polygon $canvas ash @(13,20, 16,21, 12,27, 10,28, 9,26)
        Paint-Block $canvas storm 9 13 2 1
        Paint-Block $canvas bolt 21 9 1 1
    }
    'MagmaAxe' = {
        Paint-Polygon $canvas ink @(3,26, 15,15, 13,12, 16,9, 17,4, 24,6, 29,3, 30,7, 28,11, 30,14, 28,20, 24,22, 20,18, 8,29, 4,30)
        Paint-Polygon $canvas ash @(5,27, 17,15, 19,17, 7,28, 5,29)
        Paint-Polygon $canvas cinder @(16,12, 18,10, 18,5, 24,8, 29,5, 29,7, 26,12, 29,14, 27,19, 24,20, 20,16, 17,16)
        Paint-Polygon $canvas magma @(19,7, 23,9, 27,7, 25,11, 26,14, 28,14, 26,18, 24,19, 22,16, 23,13, 20,11)
        Paint-Polygon $canvas furnace @(19,8, 23,10, 26,8, 24,11, 24,13, 27,15, 26,16, 23,14, 23,12, 20,11)
        Paint-Block $canvas brass 6 26 3 1
        Paint-Block $canvas brass 9 23 2 1
    }
}

foreach ($entry in $icons.GetEnumerator()) {
    $icon = New-Canvas 32 32
    $canvas = [Drawing.Graphics]::FromImage($icon)
    try {
        $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
        & $entry.Value
        Paint-Block $canvas badge 24 24 7 7
        Paint-Block $canvas rare 27 25 1 1
        Paint-Block $canvas rare 26 26 1 1
        Paint-Block $canvas rare 28 26 1 1
        Paint-Block $canvas rare 25 27 1 1
        Paint-Block $canvas rare 29 27 1 1
        Paint-Block $canvas rare 26 28 1 1
        Paint-Block $canvas rare 28 28 1 1
        Paint-Block $canvas rare 27 29 1 1
        $icon.Save((Join-Path $OutputDirectory ($entry.Key + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $canvas.Dispose()
        $icon.Dispose()
    }
}

if ($PreviewFile) {
    $sheet = New-Canvas 200 64
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
