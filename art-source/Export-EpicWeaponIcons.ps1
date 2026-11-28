param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/EpicWeaponIcons'),
    [string]$PreviewFile
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WandererPixelTools.ps1')
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$palette += @{ badge = '221C34'; rarity = 'CF9EFA'; electric = 'A78AF1'; flash = 'F1E9FF'; vortex = '7161AA'; darkHead = '593E75'; cut = 'DEA6F4'; sun = 'E79646'; flame = 'FFEAA0'; branch = '8A5741' }

$icons = [ordered]@{
    'StormSword' = {
        Paint-Polygon $canvas vortex @(3,18, 6,9, 14,4, 24,3, 27,4, 18,5, 10,9, 5,17, 7,20, 5,23)
        Paint-Polygon $canvas electric @(4,17, 7,11, 12,7, 19,5, 23,5, 18,7, 11,10, 6,15, 6,18)
        Paint-Polygon $canvas ink @(4,24, 9,19, 8,16, 12,14, 15,15, 22,7, 29,3, 29,10, 24,17, 20,21, 21,24, 17,27, 13,25, 8,30, 4,29)
        Paint-Polygon $canvas electric @(13,18, 23,8, 28,5, 27,11, 22,16, 18,21)
        Paint-Polygon $canvas flash @(15,17, 23,9, 28,5, 25,9, 21,14, 17,18)
        Paint-Polygon $canvas sun @(10,16, 12,15, 19,22, 18,25, 15,23)
        Paint-Polygon $canvas branch @(11,21, 14,24, 8,29, 6,27)
    }
    'AbyssScythe' = {
        Paint-Polygon $canvas ink @(3,28, 3,25, 18,9, 13,6, 8,9, 5,13, 5,7, 10,3, 18,2, 25,4, 29,8, 28,13, 25,16, 24,12, 22,11, 7,29, 4,30)
        Paint-Polygon $canvas darkHead @(4,26, 19,10, 22,11, 7,28, 4,29)
        Paint-Polygon $canvas vortex @(6,8, 11,4, 18,3, 24,5, 28,8, 27,13, 25,14, 25,11, 21,9, 17,8, 12,6, 9,8, 6,11)
        Paint-Polygon $canvas cut @(7,8, 12,4, 18,3, 24,5, 27,8, 26,9, 23,6, 17,5, 12,5)
        Paint-Block $canvas electric 22 8 2 2
        Paint-Block $canvas cut 18 12 2 1
        Paint-Block $canvas darkHead 26 17 1 2
    }
    'SolarBow' = {
        Paint-Polygon $canvas ink @(6,5, 14,2, 20,4, 26,8, 29,13, 29,19, 25,25, 20,28, 13,29, 6,30, 4,27, 8,23, 12,24, 17,22, 21,18, 22,13, 19,9, 15,7, 9,8)
        Paint-Polygon $canvas branch @(7,5, 14,3, 20,5, 25,9, 28,13, 28,18, 24,24, 20,27, 12,28, 6,29, 6,27, 9,24, 13,25, 18,23, 22,20, 24,16, 23,12, 20,8, 15,6, 10,7)
        Paint-Polygon $canvas flame @(9,5, 14,3, 20,5, 24,8, 28,14, 27,14, 23,9, 19,6, 15,5, 10,6)
        Paint-Polygon $canvas flame @(27,17, 27,18, 24,23, 20,26, 12,28, 10,27, 18,25, 23,22)
        Paint-Polygon $canvas flame @(9,5, 10,6, 8,26, 7,27)
        Paint-Polygon $canvas sun @(24,9, 26,12, 30,12, 28,16, 30,19, 26,19, 24,23, 22,19, 18,19, 20,16, 18,12, 22,12)
        Paint-Polygon $canvas flame @(24,12, 27,15, 25,19, 22,18, 21,15)
    }
}

foreach ($entry in $icons.GetEnumerator()) {
    $icon = New-Canvas 32 32
    $canvas = [Drawing.Graphics]::FromImage($icon)
    try {
        $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
        & $entry.Value
        Paint-Block $canvas badge 24 24 7 7
        Paint-Block $canvas rarity 25 25 1 1
        Paint-Block $canvas rarity 29 25 1 1
        Paint-Block $canvas rarity 26 26 1 1
        Paint-Block $canvas rarity 28 26 1 1
        Paint-Block $canvas rarity 27 27 1 1
        Paint-Block $canvas rarity 26 28 1 1
        Paint-Block $canvas rarity 28 28 1 1
        Paint-Block $canvas rarity 25 29 1 1
        Paint-Block $canvas rarity 29 29 1 1
        $icon.Save((Join-Path $OutputDirectory ($entry.Key + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $canvas.Dispose()
        $icon.Dispose()
    }
}

if ($PreviewFile) {
    $sheet = New-Canvas 120 64
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
