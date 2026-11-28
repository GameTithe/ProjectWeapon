param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Assets/ProjectWeapon/Art/WeaponRarity'),
    [string]$PreviewFile
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force

$grades = @(
    @{ Name='01-Common'; Accent='C0C6CE'; Mark=@('.###.', '.###.', '.###.', '.....', '.....') },
    @{ Name='02-Uncommon'; Accent='75D594'; Mark=@('..#..', '.###.', '##.##', '.....', '.....') },
    @{ Name='03-Rare'; Accent='7ABFFF'; Mark=@('..#..', '.#.#.', '#...#', '.#.#.', '..#..') },
    @{ Name='04-Epic'; Accent='CF9EFA'; Mark=@('#...#', '.#.#.', '..#..', '.#.#.', '#...#') },
    @{ Name='05-Legendary'; Accent='FFD273'; Mark=@('#.#.#', '#####', '.###.', '.###.', '.....') }
)
$background = [System.Drawing.ColorTranslator]::FromHtml('#202630')
$shadow = [System.Drawing.ColorTranslator]::FromHtml('#10151D')

for ($index = 0; $index -lt $grades.Count; $index++) {
    $grade = $grades[$index]
    $accent = [System.Drawing.ColorTranslator]::FromHtml('#' + $grade.Accent)
    $bitmap = [System.Drawing.Bitmap]::new(16, 16, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($y = 1; $y -le 14; $y++) {
            for ($x = 1; $x -le 14; $x++) {
                if (($x -eq 1 -or $x -eq 14) -and ($y -eq 1 -or $y -eq 14)) { continue }
                $pixel = $background
                if ($x -le 2 -or $x -ge 13 -or $y -le 2 -or $y -ge 13) { $pixel = $shadow }
                $bitmap.SetPixel($x, $y, $pixel)
            }
        }
        for ($offset = 0; $offset -lt 10; $offset++) {
            $bitmap.SetPixel(3 + $offset, 2, $accent)
            $bitmap.SetPixel(3 + $offset, 13, $accent)
        }
        for ($y = 0; $y -lt 5; $y++) {
            for ($x = 0; $x -lt 5; $x++) {
                if ($grade.Mark[$y][$x] -eq '#') { $bitmap.SetPixel(6 + $x, 5 + $y, $accent) }
            }
        }
        $bitmap.Save((Join-Path $OutputDirectory ($grade.Name + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

if ($PreviewFile) {
    $preview = [System.Drawing.Bitmap]::new(100, 40, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($index = 0; $index -lt $grades.Count; $index++) {
            $sprite = [System.Drawing.Bitmap]::new((Join-Path $OutputDirectory ($grades[$index].Name + '.png')))
            try {
                for ($y = 0; $y -lt 16; $y++) {
                    for ($x = 0; $x -lt 16; $x++) {
                        $color = $sprite.GetPixel($x, $y)
                        $preview.SetPixel(2 + 20 * $index + $x, 2 + $y, $color)
                        $gray = [int][Math]::Round(0.2126 * $color.R + 0.7152 * $color.G + 0.0722 * $color.B)
                        $preview.SetPixel(2 + 20 * $index + $x, 22 + $y, [System.Drawing.Color]::FromArgb($color.A, $gray, $gray, $gray))
                    }
                }
            }
            finally { $sprite.Dispose() }
        }
        $preview.Save($PreviewFile, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $preview.Dispose() }
}
