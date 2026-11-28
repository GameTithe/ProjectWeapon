param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/Wanderer')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$palette = @{
    ink = '1B1722'; deep = '272438'; leather = '393148'; fold = '50465B'
    shadow = '57263F'; red = '923442'; scarlet = 'C35154'; rim = 'E27465'
    metalDark = '464658'; metal = '828298'; edge = 'B9BDCE'; light = 'EBE4F0'
    hairDark = '706680'; hair = 'B1A4BE'; silver = 'D7CBDC'; white = 'F4EAF0'
    skinDark = '99646F'; skin = 'D59787'; face = 'F0BEA0'; gold = 'D3A568'
    bootDark = '30242D'; boot = '60424A'; bootLight = '986758'
}

function New-Canvas($Width, $Height) {
    return [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

function Paint-Polygon($Canvas, $Color, [int[]] $Coordinates) {
    $points = for ($i = 0; $i -lt $Coordinates.Length; $i += 2) {
        [Drawing.Point]::new($Coordinates[$i], $Coordinates[$i + 1])
    }
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#' + $palette[$Color]))
    try { $Canvas.FillPolygon($brush, [Drawing.Point[]] $points) }
    finally { $brush.Dispose() }
}

function Paint-Block($Canvas, $Color, $X, $Y, $Width, $Height) {
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#' + $palette[$Color]))
    try { $Canvas.FillRectangle($brush, $X, $Y, $Width, $Height) }
    finally { $brush.Dispose() }
}

function Paint-Boot($Canvas, $X, $Y) {
    Paint-Block $Canvas ink ($X - 1) $Y 7 5
    Paint-Block $Canvas boot $X $Y 5 3
    Paint-Block $Canvas bootLight ($X + 1) ($Y + 1) 3 1
    Paint-Block $Canvas bootDark $X ($Y + 3) 5 1
}

function Paint-Hand($Canvas, $ShoulderX, $ShoulderY, $HandX, $HandY) {
    Paint-Polygon $Canvas ink @(($ShoulderX - 3), $ShoulderY, ($ShoulderX + 3), ($ShoulderY - 1),
        ($HandX + 3), ($HandY - 2), ($HandX + 3), ($HandY + 3), ($HandX - 3), ($HandY + 3))
    Paint-Polygon $Canvas metalDark @(($ShoulderX - 2), $ShoulderY, ($ShoulderX + 2), $ShoulderY,
        ($HandX + 2), ($HandY - 2), ($HandX - 2), $HandY)
    Paint-Block $Canvas metal ($ShoulderX - 2) $ShoulderY 4 2
    Paint-Block $Canvas edge ($ShoulderX - 1) $ShoulderY 2 1
    Paint-Block $Canvas skinDark ($HandX - 2) ($HandY - 1) 4 3
    Paint-Block $Canvas face ($HandX - 1) ($HandY - 1) 2 2
}

function Paint-Head($Canvas, [string] $Facing, $X, $Y) {
    $Canvas.TranslateTransform($X, $Y)
    Paint-Polygon $Canvas ink @(1,9, 2,5, 5,3, 8,3, 9,1, 12,3, 15,2, 18,5, 19,8,
        18,13, 16,16, 13,17, 6,16, 3,14, 2,12, 0,12)
    Paint-Polygon $Canvas hairDark @(2,9, 4,5, 8,4, 10,2, 13,4, 15,3, 18,7, 17,13,
        15,15, 7,15, 4,13)
    if ($Facing -eq 'north') {
        Paint-Polygon $Canvas hair @(3,8, 6,5, 10,4, 15,5, 17,8, 16,11, 13,13, 7,12, 4,11)
        Paint-Polygon $Canvas silver @(5,7, 8,5, 12,5, 15,7, 15,9, 12,10, 8,9, 5,10)
    } else {
        if ($Facing -eq 'east') {
            Paint-Polygon $Canvas skinDark @(8,10, 16,8, 17,10, 17,12, 18,13, 16,14, 14,16, 9,15, 7,13)
            Paint-Polygon $Canvas face @(12,10, 16,10, 16,12, 17,13, 14,15, 10,14)
            Paint-Block $Canvas ink 15 11 1 2
        } else {
            Paint-Polygon $Canvas skinDark @(4,10, 15,9, 16,12, 14,15, 11,16, 7,15, 5,14)
            Paint-Polygon $Canvas face @(6,10, 14,10, 14,13, 12,15, 8,14, 6,13)
            Paint-Block $Canvas ink 7 11 1 2
            Paint-Block $Canvas ink 12 11 1 2
            Paint-Block $Canvas white 8 11 1 1
        }
        Paint-Polygon $Canvas hair @(2,9, 4,6, 8,4, 14,4, 17,6, 18,10, 15,9, 13,11,
            12,8, 9,12, 8,9, 5,12, 4,10)
        Paint-Polygon $Canvas silver @(3,8, 6,5, 10,4, 14,5, 16,4, 17,7, 15,8, 12,6,
            10,8, 8,7, 6,9)
    }
    Paint-Polygon $Canvas white @(6,6, 9,4, 12,5, 11,6, 8,7)
    $Canvas.TranslateTransform(-$X, -$Y)
}

function New-AttackFrame([string] $Facing, [int] $Frame) {
    $mirrored = $Facing -eq 'west'
    if ($mirrored) { $Facing = 'east' }
    $image = New-Canvas 32 48
    $canvas = [Drawing.Graphics]::FromImage($image)
    $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
    try {
        $lean = @(-1, 0, 2, 1)[$Frame]
        $lower = @(1, 0, 2, 1)[$Frame]
        $swing = @(@(8,25), @(9,17), @(26,26), @(23,31))
        if ($Facing -eq 'north') { $swing = @(@(20,28), @(23,17), @(28,14), @(11,22)) }
        if ($Facing -eq 'east') { $swing = @(@(14,24), @(18,17), @(28,22), @(26,28)) }
        $handX, $handY = $swing[$Frame]
        $capeTip = if ($Facing -eq 'east') { @(4,3,1,2)[$Frame] } else { @(9,9,4,6)[$Frame] }
        Paint-Polygon $canvas ink @(10,20, 20,19, 23,24, 23,35, 18,39, 15,36, 10,40, 8,37,
            $capeTip,38, ($capeTip + 2),33, 8,29)
        Paint-Polygon $canvas shadow @(10,21, 20,21, 21,27, 21,34, 17,37, 15,34, 9,38,
            ($capeTip + 1),37, ($capeTip + 3),33)
        Paint-Polygon $canvas red @(10,22, 14,21, 16,26, 14,32, 10,34, ($capeTip + 2),36, 8,31)
        Paint-Polygon $canvas scarlet @(9,25, 13,24, 12,28, 10,29, 8,33, ($capeTip + 3),34)
        Paint-Polygon $canvas ink @(11,30, 21,30, 24,33, 23,39, 17,39, 15,35, 13,39, 8,39, 8,34)
        Paint-Polygon $canvas leather @(12,31, 19,31, 22,34, 21,38, 18,38, 15,34, 12,38, 9,38, 10,34)
        Paint-Boot $canvas (@(9,10,8,9)[$Frame]) 37
        Paint-Boot $canvas (@(17,17,19,18)[$Frame]) 37
        if ($Facing -eq 'north') {
            Paint-Hand $canvas 20 24 $handX $handY
        }
        if ($Facing -ne 'north') {
            Paint-Hand $canvas 12 24 (9 - $lean) (29 + $lower)
        }
        Paint-Polygon $canvas ink @(11,21, 19,21, 23,25, 22,32, 19,34, 11,33, 9,28)
        Paint-Polygon $canvas leather @(12,22, 19,22, 21,25, 20,31, 18,33, 12,32, 10,28)
        Paint-Polygon $canvas fold @(13,23, 17,23, 18,26, 16,29, 13,28, 12,26)
        Paint-Block $canvas bootDark 11 29 10 3
        Paint-Block $canvas bootLight 12 29 8 1
        Paint-Block $canvas gold 15 29 3 3
        Paint-Block $canvas ink 16 30 1 1
        Paint-Head $canvas $Facing (6 + $lean) (5 + $lower)
        Paint-Polygon $canvas ink @(9,20, 13,22, 16,22, 20,20, 23,23, 21,26, 17,27, 12,26, 9,24)
        Paint-Polygon $canvas red @(10,21, 14,23, 18,23, 21,21, 22,23, 20,25, 16,26, 12,25)
        Paint-Polygon $canvas scarlet @(11,22, 14,23, 18,23, 21,22, 19,24, 15,24, 12,24)
        if ($Facing -eq 'north') {
            Paint-Polygon $canvas red @(10,24, 16,25, 20,23, 21,29, 19,33, 16,35, 14,33, 10,36, 8,34)
            Paint-Polygon $canvas scarlet @(10,25, 14,26, 13,30, 10,34, 9,34)
            if ($Frame -eq 2) {
                Paint-Block $canvas face ($handX - 1) ($handY - 1) 2 2
            } else {
                Paint-Hand $canvas 22 24 $handX $handY
            }
        } else {
            Paint-Hand $canvas (20 + $lean) (24 + $lower) $handX $handY
        }
    } finally {
        $canvas.Dispose()
    }
    if ($mirrored) { $image.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipX) }
    return $image
}

$attack = New-Canvas 128 192
$directions = 'south', 'east', 'north', 'west'
try {
    for ($row = 0; $row -lt $directions.Length; $row++) {
        for ($frame = 0; $frame -lt 4; $frame++) {
            $pose = New-AttackFrame $directions[$row] $frame
            try {
                for ($y = 0; $y -lt 48; $y++) {
                    for ($x = 0; $x -lt 32; $x++) {
                        $attack.SetPixel($frame * 32 + $x, $row * 48 + $y, $pose.GetPixel($x, $y))
                    }
                }
            } finally { $pose.Dispose() }
        }
    }
    $attack.Save((Join-Path $OutputDirectory 'WandererAttack.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $attack.Dispose() }

$sword = New-Canvas 32 32
$canvas = [Drawing.Graphics]::FromImage($sword)
$canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
try {
    Paint-Polygon $canvas ink @(4,26, 6,23, 10,21, 9,19, 12,17, 14,19, 23,10, 28,5, 28,11,
        26,14, 18,21, 17,19, 16,23, 18,25, 16,27, 13,25, 9,29, 6,29)
    Paint-Polygon $canvas metalDark @(14,20, 24,10, 27,7, 27,11, 24,15, 18,20, 17,19, 16,22)
    Paint-Polygon $canvas metal @(15,19, 24,10, 27,7, 26,11, 22,15, 20,15, 17,20)
    Paint-Polygon $canvas edge @(16,19, 25,10, 27,7, 27,9, 19,17, 18,19)
    Paint-Block $canvas light 25 9 1 1
    Paint-Polygon $canvas boot @(6,26, 12,21, 14,23, 8,28, 6,28)
    Paint-Polygon $canvas bootLight @(6,26, 10,23, 11,24, 7,27)
    Paint-Polygon $canvas gold @(10,19, 12,18, 17,24, 16,26, 14,24, 12,22)
    Paint-Block $canvas metalDark 5 26 2 2
    Paint-Block $canvas metal 5 26 1 1
    $sword.Save((Join-Path $OutputDirectory 'WornSword.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $canvas.Dispose()
    $sword.Dispose()
}
