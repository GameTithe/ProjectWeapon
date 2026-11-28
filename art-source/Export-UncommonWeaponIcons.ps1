param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/UncommonWeaponIcons'),
    [string]$PreviewFile
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WandererPixelTools.ps1')
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$palette += @{
    fireDark = '993942'; fire = 'E85939'; ember = 'FFA45C'; flame = 'FFE3A0'
    steel = '91BCC2'; brightSteel = 'DBF0ED'; tempered = '4B7680'
    wood = '754A3A'; maple = 'C0895C'; orangeMetal = 'C58451'
    uncommon = '75D594'; badge = '172A28'; thread = 'E8DDB6'
}

$icons = [ordered]@{
    'FlameSword' = {
        Paint-Polygon $canvas ink @(3,26, 9,19, 9,16, 12,15, 16,16, 18,11, 19,7, 22,9, 23,5, 28,2, 29,9, 26,13, 26,17, 22,17, 19,21, 19,25, 16,27, 11,25, 7,29, 3,29)
        Paint-Polygon $canvas fireDark @(14,18, 19,12, 20,9, 22,11, 24,6, 28,4, 28,9, 24,14, 25,16, 21,16, 18,21)
        Paint-Polygon $canvas fire @(14,19, 20,13, 21,11, 22,12, 25,7, 27,6, 26,10, 24,13, 24,15, 21,15, 17,21)
        Paint-Polygon $canvas flame @(16,19, 23,12, 25,9, 27,7, 26,10, 21,15, 19,19)
        Paint-Polygon $canvas orangeMetal @(10,17, 12,16, 18,23, 17,25, 15,24, 12,21)
        Paint-Polygon $canvas wood @(11,21, 13,23, 6,28, 4,27)
        Paint-Block $canvas ember 6 25 2 1
    }
    'SteelSpear' = {
        Paint-Polygon $canvas ink @(2,28, 22,8, 21,6, 25,6, 29,2, 28,8, 29,13, 25,12, 6,30, 2,30)
        Paint-Polygon $canvas tempered @(3,28, 23,8, 25,10, 6,29, 3,29)
        Paint-Polygon $canvas steel @(4,28, 23,9, 24,10, 5,29)
        Paint-Polygon $canvas steel @(22,8, 26,7, 28,4, 27,9, 28,11, 25,11, 23,13)
        Paint-Polygon $canvas brightSteel @(23,9, 27,6, 25,10, 24,11)
        Paint-Block $canvas thread 20 12 2 2
        Paint-Block $canvas thread 7 25 2 1
    }
    'RepeaterBow' = {
        Paint-Polygon $canvas ink @(6,4, 14,2, 20,4, 25,7, 29,11, 29,18, 25,24, 20,28, 12,30, 6,28, 9,23, 13,24, 18,22, 22,18, 23,13, 20,9, 15,7, 10,8)
        Paint-Polygon $canvas maple @(7,5, 14,3, 19,5, 24,8, 28,12, 28,18, 24,23, 20,26, 12,29, 8,27, 10,25, 14,26, 19,24, 23,20, 25,16, 24,12, 21,8, 15,6, 10,7)
        Paint-Polygon $canvas tempered @(17,6, 23,7, 26,9, 25,12, 23,12, 21,9)
        Paint-Polygon $canvas tempered @(19,23, 23,19, 25,16, 28,17, 26,21, 22,25)
        Paint-Block $canvas steel 22 8 3 1
        Paint-Block $canvas steel 24 20 2 1
        Paint-Polygon $canvas thread @(9,5, 10,6, 9,26, 8,27)
        Paint-Polygon $canvas thread @(12,6, 13,7, 12,25, 11,26)
        Paint-Block $canvas orangeMetal 24 14 4 3
    }
    'BattleAxe' = {
        Paint-Polygon $canvas ink @(3,27, 16,15, 13,10, 17,7, 18,3, 22,6, 26,4, 30,9, 30,16, 26,20, 21,17, 7,29, 3,29)
        Paint-Polygon $canvas wood @(4,27, 17,15, 19,16, 6,28, 4,28)
        Paint-Polygon $canvas maple @(4,27, 17,15, 18,16, 5,28)
        Paint-Polygon $canvas tempered @(16,10, 18,8, 19,5, 22,8, 27,6, 29,10, 29,15, 26,18, 21,15, 18,15)
        Paint-Polygon $canvas steel @(22,9, 27,6, 29,10, 28,15, 26,18, 23,15)
        Paint-Polygon $canvas brightSteel @(27,7, 29,10, 28,15, 26,17, 27,14, 27,10, 26,9)
        Paint-Block $canvas orangeMetal 18 10 3 3
    }
    'TwinSwords' = {
        Paint-Polygon $canvas ink @(4,25, 9,19, 7,17, 9,15, 12,16, 21,6, 27,3, 27,9, 17,20, 18,23, 15,25, 12,23, 7,29, 4,28)
        Paint-Polygon $canvas tempered @(13,17, 22,7, 26,5, 25,9, 16,19)
        Paint-Polygon $canvas steel @(14,17, 22,8, 25,6, 24,9, 16,18)
        Paint-Polygon $canvas orangeMetal @(8,17, 9,16, 17,22, 15,24, 12,21)
        Paint-Polygon $canvas wood @(10,21, 12,23, 7,28, 5,27)
        Paint-Polygon $canvas ink @(22,28, 17,23, 14,24, 12,22, 13,19, 4,10, 3,4, 9,5, 19,15, 22,14, 24,17, 23,20, 27,25, 27,28, 25,29)
        Paint-Polygon $canvas tempered @(5,6, 9,7, 19,17, 16,19, 6,10)
        Paint-Polygon $canvas brightSteel @(5,6, 9,7, 19,17, 16,17, 7,9)
        Paint-Polygon $canvas orangeMetal @(13,21, 14,19, 22,16, 23,18, 20,20, 16,23)
        Paint-Polygon $canvas wood @(19,21, 21,20, 26,26, 25,28, 23,27)
    }
}

function Paint-UncommonMark($Canvas) {
    Paint-Block $Canvas badge 25 24 6 7
    Paint-Block $Canvas uncommon 27 25 1 1
    Paint-Block $Canvas uncommon 26 26 3 1
    Paint-Block $Canvas uncommon 25 27 2 1
    Paint-Block $Canvas uncommon 28 27 2 1
}

foreach ($entry in $icons.GetEnumerator()) {
    $icon = New-Canvas 32 32
    $canvas = [Drawing.Graphics]::FromImage($icon)
    try {
        $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
        & $entry.Value
        Paint-UncommonMark $canvas
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
