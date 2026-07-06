# Genera src/UI/Recursos/app.ico (multi-tamaño, entradas PNG) con la identidad de la app:
# cuadrado redondeado azul "Plano técnico" (#1D4E89) con monograma SW.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$raiz   = Split-Path -Parent $PSScriptRoot
$salida = Join-Path $raiz 'src\UI\Recursos\app.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $salida) | Out-Null

function Crear-PngIcono([int]$tam) {
    $bmp = New-Object System.Drawing.Bitmap $tam, $tam
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $radio = [Math]::Max(2, [int]($tam * 0.18))
    $d = $radio * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($tam - 1 - $d, 0, $d, $d, 270, 90)
    $path.AddArc($tam - 1 - $d, $tam - 1 - $d, $d, $d, 0, 90)
    $path.AddArc(0, $tam - 1 - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brocha = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 29, 78, 137))
    $g.FillPath($brocha, $path)

    $fuente  = New-Object System.Drawing.Font('Segoe UI', [Math]::Max(6.0, $tam * 0.34), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $formato = New-Object System.Drawing.StringFormat
    $formato.Alignment = 'Center'
    $formato.LineAlignment = 'Center'
    $rectF = New-Object System.Drawing.RectangleF 0, ([single]($tam * 0.02)), $tam, $tam
    $g.DrawString('SW', $fuente, [System.Drawing.Brushes]::White, $rectF, $formato)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $g.Dispose(); $bmp.Dispose(); $fuente.Dispose(); $brocha.Dispose(); $ms.Dispose()
    # Coma para que PowerShell no "desenrolle" el byte[] en el pipeline
    return ,$bytes
}

$tamanos = @(16, 32, 48, 256)
$pngs = foreach ($t in $tamanos) { Crear-PngIcono $t }

$fs = [System.IO.File]::Create($salida)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)               # reservado
$bw.Write([UInt16]1)               # tipo: icono
$bw.Write([UInt16]$tamanos.Count)
$offset = 6 + 16 * $tamanos.Count
for ($i = 0; $i -lt $tamanos.Count; $i++) {
    $t = $tamanos[$i]
    $bw.Write([Byte]($t -band 0xFF))   # 256 -> 0 por convención ICO
    $bw.Write([Byte]($t -band 0xFF))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Dispose(); $fs.Dispose()

Write-Host "Icono creado: $salida ($((Get-Item $salida).Length) bytes)"
