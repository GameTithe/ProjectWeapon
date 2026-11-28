param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/LegendaryWeaponIcons'),
    [string]$PreviewFile
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WandererPixelTools.ps1')
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$palette += @{ badge = '221C34'; rarity = 'FFD273'; goldDeep = 'A96E57'; goldRim = 'FFD47B'; radiant = 'FFF5CC'; celestial = '79D5EB'; celestialDark = '3C708A'; blood = 'A94058'; ember = 'FF9D87'; smoke = '574267'; bone = 'E3CDDD' }

$icons = [ordered]@{
    'HeavensWrathSword' = {
        Paint-Block $canvas goldRim 25 2 1 5
        Paint-Block $canvas goldRim 23 4 5 1
        Paint-Block $canvas radiant 25 4 1 1
        Paint-Polygon $canvas ink @(4,26, 10,20, 6,17, 7,13, 13,16, 17,13, 22,6, 30,5, 28,13, 20,22, 23,26, 18,26, 14,23, 8,30, 4,29)
        Paint-Polygon $canvas goldDeep @(5,27, 12,20, 15,22, 8,29, 5,28)
        Paint-Polygon $canvas goldRim @(7,15, 14,18, 16,17, 19,22, 21,25, 17,24, 13,21, 10,20)
        Paint-Polygon $canvas goldRim @(14,20, 22,8, 29,6, 27,12, 20,20, 17,22)
        Paint-Polygon $canvas radiant @(16,20, 23,8, 28,7, 25,11, 19,19)
        Paint-Block $canvas celestial 16 19 2 1
        Paint-Block $canvas goldRim 16 9 2 1
    }
    'DoomScythe' = {
        Paint-Polygon $canvas ink @(3,27, 19,11, 14,7, 9,8, 6,12, 6,5, 11,2, 20,2, 26,4, 30,9, 29,15, 26,19, 25,14, 23,13, 20,15, 8,29, 4,30)
        Paint-Polygon $canvas smoke @(4,27, 19,12, 22,13, 7,28, 4,29)
        Paint-Polygon $canvas blood @(7,7, 12,3, 20,3, 25,5, 29,9, 28,14, 26,17, 26,13, 24,10, 20,8, 15,6, 10,7, 7,10)
        Paint-Polygon $canvas ember @(8,6, 12,3, 20,3, 25,5, 29,9, 28,11, 25,7, 19,5, 12,5)
        Paint-Polygon $canvas bone @(15,8, 20,8, 25,11, 24,15, 22,13, 19,11)
        Paint-Block $canvas blood 21 12 2 1
        Paint-Block $canvas ember 28 19 1 2
        Paint-Block $canvas ember 24 17 1 1
    }
    'GenesisBow' = {
        Paint-Polygon $canvas ink @(5,6, 8,2, 14,3, 18,4, 21,6, 26,7, 29,11, 29,20, 26,25, 20,28, 13,29, 9,30, 5,27, 8,22, 13,23, 18,21, 22,17, 22,13, 18,9, 13,8, 9,9)
        Paint-Polygon $canvas celestialDark @(8,5, 14,4, 20,6, 26,9, 28,12, 28,19, 25,24, 20,27, 13,28, 9,28, 7,26, 9,24, 14,25, 20,23, 24,19, 24,13, 20,9, 14,7, 9,8)
        Paint-Polygon $canvas celestial @(10,5, 14,5, 20,7, 26,10, 28,13, 27,14, 24,11, 19,8, 13,6)
        Paint-Polygon $canvas celestial @(27,18, 27,20, 24,24, 20,26, 14,28, 10,27, 18,25, 23,22)
        Paint-Polygon $canvas radiant @(9,5, 10,6, 9,26, 8,28)
        Paint-Polygon $canvas goldRim @(23,13, 26,10, 29,14, 28,17, 25,20, 22,18, 21,15)
        Paint-Polygon $canvas radiant @(23,15, 26,12, 28,15, 25,18)
        Paint-Block $canvas goldRim 15 3 1 4
        Paint-Block $canvas goldRim 14 4 3 1
        Paint-Block $canvas goldRim 16 25 3 1
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
        Paint-Block $canvas rarity 27 25 1 1
        Paint-Block $canvas rarity 29 25 1 1
        Paint-Block $canvas rarity 25 26 5 1
        Paint-Block $canvas rarity 26 27 3 2
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
