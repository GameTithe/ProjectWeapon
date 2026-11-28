param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Chest/Art'))

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WandererPixelTools.ps1')
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$palette += @{ wood = '794638'; woodLight = 'AD6F49'; brace = '71566A'; braceLight = 'AB7C79'; hinge = 'DBAA66'; dark = '341F32' }

foreach ($opened in @($false, $true)) {
    $bitmap = New-Canvas 32 32
    $canvas = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
        if ($opened) {
            Paint-Polygon $canvas ink @(3,14, 5,8, 10,4, 24,4, 28,8, 29,17, 24,23, 7,23, 3,20)
            Paint-Polygon $canvas brace @(5,14, 7,9, 11,6, 23,6, 26,9, 27,15, 23,19, 8,19)
            Paint-Polygon $canvas dark @(7,14, 10,10, 22,10, 25,13, 24,19, 8,19)
            Paint-Block $canvas woodLight 11 7 12 1
            Paint-Block $canvas hinge 9 11 2 2
            Paint-Block $canvas hinge 23 12 2 2
            Paint-Polygon $canvas ink @(3,18, 7,16, 25,16, 29,19, 29,26, 25,29, 6,29, 3,26)
        } else {
            Paint-Polygon $canvas ink @(3,14, 6,9, 10,7, 23,7, 27,10, 29,15, 29,26, 26,29, 6,29, 3,26)
            Paint-Polygon $canvas wood @(5,14, 8,10, 11,9, 23,9, 26,12, 27,15, 24,17, 7,17)
            Paint-Polygon $canvas woodLight @(7,13, 10,10, 23,10, 25,12, 21,13)
            Paint-Block $canvas dark 6 16 20 2
        }
        Paint-Polygon $canvas wood @(5,19, 9,18, 24,18, 27,20, 27,25, 25,27, 7,27, 5,25)
        Paint-Block $canvas woodLight 8 20 17 1
        Paint-Block $canvas dark 8 24 16 1
        Paint-Block $canvas brace 5 19 3 7
        Paint-Block $canvas brace 24 19 3 7
        Paint-Block $canvas braceLight 6 20 1 4
        Paint-Block $canvas braceLight 25 20 1 4
        Paint-Block $canvas ink 13 18 6 6
        Paint-Block $canvas hinge 14 19 4 4
        Paint-Block $canvas dark 16 20 1 2
        $filename = if ($opened) { 'OpenedChest.png' } else { 'ClosedChest.png' }
        $bitmap.Save((Join-Path $OutputDirectory $filename), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $canvas.Dispose()
        $bitmap.Dispose()
    }
}
