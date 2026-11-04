$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$logo = [System.Drawing.Image]::FromFile((Join-Path $root 'src/Hibike.App/Assets/hibike-desktop.png'))
try {
    foreach ($size in @(@(164, 314, 116, 'hibike-wizard.bmp'), @(55, 55, 44, 'hibike-wizard-small.bmp'))) {
        $bitmap = New-Object System.Drawing.Bitmap($size[0], $size[1])
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::FromArgb(24, 24, 24))
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $side = [int]$size[2]
            $graphics.DrawImage($logo, [int](($size[0] - $side) / 2), [int](($size[1] - $side) / 2), $side, $side)
            $bitmap.Save((Join-Path $root ('packaging/windows/' + $size[3])), [System.Drawing.Imaging.ImageFormat]::Bmp)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $logo.Dispose() }
