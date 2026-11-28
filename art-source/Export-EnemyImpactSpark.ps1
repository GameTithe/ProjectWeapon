param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/EnemyCues')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$colors = @{
    o = [Drawing.Color]::FromArgb(220, 234, 101, 58)
    g = [Drawing.Color]::FromArgb(250, 255, 171, 91)
    y = [Drawing.Color]::FromArgb(255, 255, 227, 148)
    w = [Drawing.Color]::FromArgb(255, 255, 249, 212)
}

$rows = @(
    '................'
    '.......o........'
    '.......g.....o..'
    '....o..y........'
    '.....g.yy.......'
    '......gyg..o....'
    '....g.ywygo.....'
    '..ogyywwwyyg....'
    '....gyywygo.....'
    '...o..yyg.......'
    '......gy.g......'
    '......g...o.....'
    '.....o.....g....'
    '............o...'
    '................'
    '................'
)
$image = [Drawing.Bitmap]::new(16, 16, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    for ($y = 0; $y -lt 16; $y++) {
        for ($x = 0; $x -lt 16; $x++) {
            $name = [string] $rows[$y][$x]
            if ($colors.ContainsKey($name)) {
                $image.SetPixel($x, $y, $colors[$name])
            }
        }
    }
    $image.Save((Join-Path $OutputDirectory 'EnemyImpactSpark.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $image.Dispose() }
