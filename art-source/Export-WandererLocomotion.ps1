param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/Wanderer')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WandererPixelTools.ps1')
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function New-LocomotionPose([string] $Facing, [string] $Action, [int] $Frame) {
    $mirrored = $Facing -eq 'west'
    if ($mirrored) { $Facing = 'east' }

    $image = New-Canvas 32 48
    $canvas = [Drawing.Graphics]::FromImage($image)
    $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
    try {
        $lean = 0
        $bob = 0
        $crouch = 0
        $leftX = 9
        $rightX = 17
        $leftY = 37
        $rightY = 37
        $leftHandY = 29
        $rightHandY = 29
        $capeTip = 6

        switch ($Action) {
            'idle' {
                $bob = -$Frame
                $capeTip = 7 - $Frame
            }
            'walk' {
                $bob = @(0,-1,-2,0,-1,-2)[$Frame]
                $lean = if ($Facing -eq 'east') { 1 } else { 0 }
                $leftX = @(8,9,10,11,10,9)[$Frame]
                $rightX = @(18,17,16,15,16,17)[$Frame]
                $leftY = @(37,36,35,37,37,37)[$Frame]
                $rightY = @(37,37,37,37,36,35)[$Frame]
                $leftHandY = @(28,29,30,30,29,28)[$Frame]
                $rightHandY = @(30,29,28,28,29,30)[$Frame]
                $capeTip = @(5,4,3,4,5,3)[$Frame]
            }
            'dodge' {
                $crouch = @(3,6,5,2)[$Frame]
                $bob = @(2,3,3,1)[$Frame]
                $lean = if ($Facing -eq 'east') { @(2,4,3,1)[$Frame] } else { @(0,1,0,0)[$Frame] }
                $leftX = @(8,8,10,10)[$Frame]
                $rightX = @(18,17,19,18)[$Frame]
                $leftY = @(37,34,36,37)[$Frame]
                $rightY = @(36,37,35,37)[$Frame]
                $leftHandY = @(29,30,30,29)[$Frame]
                $rightHandY = @(28,30,29,29)[$Frame]
                $capeTip = @(4,2,3,5)[$Frame]
            }
        }

        if ($Facing -eq 'east') { $capeTip = [Math]::Max(2, $capeTip - 2) }
        $shoulderY = 23 + $bob + $crouch
        $headX = 6 + $lean
        $headY = 5 + $bob + $crouch
        $waistY = [Math]::Min(34, 29 + $bob + $crouch)
        $capeTop = $shoulderY - 3

        Paint-Polygon $canvas ink @(10,$capeTop, 21,$capeTop, 23,26, 23,35,
            18,39, 15,36, 10,40, 7,38, $capeTip,39, ($capeTip + 1),34, 8,30)
        Paint-Polygon $canvas shadow @(11,($capeTop + 1), 20,($capeTop + 1), 21,27, 21,34,
            17,37, 15,35, 9,38, ($capeTip + 1),38, ($capeTip + 2),34)
        Paint-Polygon $canvas red @(11,($capeTop + 2), 16,($capeTop + 2), 15,31, 11,35,
            ($capeTip + 2),37, ($capeTip + 3),34)
        Paint-Polygon $canvas scarlet @(10,($capeTop + 4), 13,($capeTop + 3), 12,31, 9,33,
            ($capeTip + 3),35, ($capeTip + 4),32)

        Paint-Polygon $canvas ink @(11,$waistY, 20,$waistY, 24,34, 23,39, 17,39,
            15,35, 13,39, 8,39, 8,34)
        Paint-Polygon $canvas leather @(12,($waistY + 1), 19,($waistY + 1), 22,34,
            21,38, 18,38, 15,34, 12,38, 9,38, 10,34)
        Paint-Boot $canvas $leftX $leftY
        Paint-Boot $canvas $rightX $rightY
        Paint-Hand $canvas 11 $shoulderY 9 ($leftHandY + $bob)

        Paint-Polygon $canvas ink @((10 + $lean),($shoulderY - 2), (19 + $lean),($shoulderY - 3),
            (23 + $lean),($shoulderY + 1), 22,($waistY + 3), 18,($waistY + 5), 11,($waistY + 4), 9,($waistY + 1))
        Paint-Polygon $canvas leather @((11 + $lean),($shoulderY - 1), (19 + $lean),($shoulderY - 2),
            (21 + $lean),($shoulderY + 2), 20,($waistY + 3), 18,($waistY + 4), 12,($waistY + 3), 10,$waistY)
        Paint-Block $canvas bootDark 11 $waistY 10 3
        Paint-Block $canvas bootLight 12 $waistY 8 1
        Paint-Block $canvas gold 15 $waistY 3 3
        Paint-Block $canvas ink 16 ($waistY + 1) 1 1

        Paint-Head $canvas $Facing $headX $headY
        $scarfY = $headY + 15
        Paint-Polygon $canvas ink @((9 + $lean),$scarfY, (14 + $lean),($scarfY + 1),
            (20 + $lean),($scarfY - 1), (23 + $lean),($scarfY + 2), (20 + $lean),($scarfY + 5),
            (16 + $lean),($scarfY + 6), (12 + $lean),($scarfY + 5))
        Paint-Polygon $canvas red @((10 + $lean),($scarfY + 1), (14 + $lean),($scarfY + 2),
            (20 + $lean),$scarfY, (22 + $lean),($scarfY + 2), (18 + $lean),($scarfY + 5),
            (13 + $lean),($scarfY + 4))
        Paint-Block $canvas scarlet (12 + $lean) ($scarfY + 2) 7 1

        if ($Facing -eq 'north') {
            Paint-Polygon $canvas red @(11,($scarfY + 4), 16,($scarfY + 5), 20,($scarfY + 3),
                20,33, 16,36, 13,34, 9,37, 9,34)
            Paint-Polygon $canvas scarlet @(11,($scarfY + 5), 14,($scarfY + 6), 13,33, 10,35)
        }
        $rightHandX = if ($Facing -eq 'east' -and $Action -eq 'dodge') { 25 } else {22 + $lean}
        Paint-Hand $canvas (20 + $lean) $shoulderY $rightHandX ($rightHandY + $bob)
    } finally {
        $canvas.Dispose()
    }

    if ($mirrored) { $image.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipX) }
    return $image
}

$directions = 'south', 'east', 'north', 'west'
$clips = @(
    @{ Action = 'idle'; FrameCount = 2; Name = 'WandererIdle' }
    @{ Action = 'walk'; FrameCount = 6; Name = 'WandererWalk' }
    @{ Action = 'dodge'; FrameCount = 4; Name = 'WandererDodge' }
)
foreach ($clip in $clips) {
    $sheet = New-Canvas (32 * $clip.FrameCount) (48 * $directions.Length)
    try {
        for ($row = 0; $row -lt $directions.Length; $row++) {
            for ($frame = 0; $frame -lt $clip.FrameCount; $frame++) {
                $pose = New-LocomotionPose $directions[$row] $clip.Action $frame
                try {
                    for ($y = 0; $y -lt 48; $y++) {
                        for ($x = 0; $x -lt 32; $x++) {
                            $sheet.SetPixel($frame * 32 + $x, $row * 48 + $y, $pose.GetPixel($x, $y))
                        }
                    }
                } finally { $pose.Dispose() }
            }
        }
        $sheet.Save((Join-Path $OutputDirectory ($clip.Name + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $sheet.Dispose() }
}
