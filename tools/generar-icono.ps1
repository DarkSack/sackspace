# Genera src\SackSpace\Assets\sackspace.ico: un cuadrado redondeado con degradado
# y cuatro "paneles" de cristal (los espacios de trabajo). Varios tamaños en PNG.
#   pwsh tools\generar-icono.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$salida = Join-Path $PSScriptRoot '..\src\SackSpace\Assets\sackspace.ico'

function Redondeado([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Png([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [float]($s * 0.04)
    $fondo = Redondeado $m $m ($s - 2 * $m) ($s - 2 * $m) ([float]($s * 0.24))
    $deg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(255, 124, 156, 255)), ([System.Drawing.Color]::FromArgb(255, 180, 108, 255))
    $g.FillPath($deg, $fondo)

    # Cuatro paneles: tres de cristal y uno sólido (el espacio activo).
    $pad = [float]($s * 0.22); $gap = [float]($s * 0.07)
    $t = [float](($s - 2 * $pad - $gap) / 2)
    $r = [float]([Math]::Max(1.5, $s * 0.07))
    $cristal = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 255, 255, 255))
    $solido = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $pos = @(@($pad, $pad), @(($pad + $t + $gap), $pad), @($pad, ($pad + $t + $gap)), @(($pad + $t + $gap), ($pad + $t + $gap)))
    for ($i = 0; $i -lt 4; $i++) {
        $b = if ($i -eq 0) { $solido } else { $cristal }
        $g.FillPath($b, (Redondeado $pos[$i][0] $pos[$i][1] $t $t $r))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$tamanos = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $tamanos) { , (Png $s) }

# Formato ICO: cabecera, una entrada por tamaño y luego las imágenes PNG.
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$tamanos.Count)
$offset = 6 + 16 * $tamanos.Count
for ($i = 0; $i -lt $tamanos.Count; $i++) {
    $s = $tamanos[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
New-Item -ItemType Directory -Force (Split-Path $salida) | Out-Null
[IO.File]::WriteAllBytes($salida, $ms.ToArray())
"Icono: $salida ($($ms.Length) bytes)"
