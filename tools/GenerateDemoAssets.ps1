$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$target = Join-Path $PSScriptRoot '..\source\assets'
New-Item -ItemType Directory -Path $target -Force | Out-Null

function Save-Png($name, $width, $height, $draw) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)
        & $draw $g
        $bmp.Save((Join-Path $target $name), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $g.Dispose(); $bmp.Dispose() }
}

Save-Png 'pet.png' 600 900 {
    param($g)
    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 77, 151, 207))
    $light = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 135, 205, 238))
    $white = [System.Drawing.Brushes]::White
    $ink = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 61, 90))
    try {
        $g.FillEllipse($blue, 135, 150, 330, 350)
        $g.FillEllipse($light, 170, 425, 260, 290)
        $g.FillEllipse($white, 205, 282, 65, 80)
        $g.FillEllipse($white, 330, 282, 65, 80)
        $g.FillEllipse($ink, 229, 317, 22, 34)
        $g.FillEllipse($ink, 354, 317, 22, 34)
        $g.FillEllipse($blue, 185, 665, 80, 90)
        $g.FillEllipse($blue, 335, 665, 80, 90)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 30, 61, 90), 8)
        try { $g.DrawArc($pen, 257, 365, 86, 65, 15, 150) } finally { $pen.Dispose() }
    } finally { $blue.Dispose(); $light.Dispose(); $ink.Dispose() }
}

Save-Png 'dressing-room.png' 600 900 {
    param($g)
    $wall = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(240, 242, 232, 213))
    $door = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 116, 166, 196))
    try {
        $g.FillRectangle($wall, 35, 45, 530, 810)
        $g.FillRectangle($door, 75, 85, 450, 725)
        $g.FillEllipse([System.Drawing.Brushes]::Gold, 450, 450, 24, 24)
    } finally { $wall.Dispose(); $door.Dispose() }
}

Save-Png 'chat-background.png' 1200 800 {
    param($g)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 229, 242, 250))
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(80, 116, 186, 228))
    try { $g.FillEllipse($brush, 760, -180, 580, 580); $g.FillEllipse($brush, -210, 460, 550, 550) }
    finally { $brush.Dispose() }
}
