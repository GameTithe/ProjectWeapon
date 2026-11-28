Add-Type -AssemblyName System.Drawing

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
