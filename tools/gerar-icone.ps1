# Gera assets\pulso.ico (16, 32, 48 e 256 px, PNG dentro do ICO): disco preto com o anel de consumo.
param([string]$Saida = (Join-Path $PSScriptRoot "..\assets\pulso.ico"))
Add-Type -AssemblyName System.Drawing
$tamanhos = 16, 32, 48, 256
$pngs = @()
foreach ($t in $tamanhos) {
    $bmp = New-Object System.Drawing.Bitmap $t, $t
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $m = $t * 0.04; $d = $t - 2 * $m
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 0, 0))), $m, $m, $d, $d)
    $esp = [Math]::Max(2, $t * 0.15); $r0 = $m + $esp / 2 + $t * 0.05; $dr = $t - 2 * $r0
    $trilho = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 48, 48, 48)), $esp
    $g.DrawEllipse($trilho, $r0, $r0, $dr, $dr)
    $arco = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 255, 136)), $esp
    $arco.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $arco.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($arco, $r0, $r0, $dr, $dr, -90, 250)
    $c = $t * 0.13
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 232, 232, 234))), $t / 2 - $c, $t / 2 - $c, 2 * $c, 2 * $c)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}
New-Item -ItemType Directory -Force (Split-Path $Saida) | Out-Null
$fs = [System.IO.File]::Create($Saida)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$tamanhos.Count)
$offset = 6 + 16 * $tamanhos.Count
for ($i = 0; $i -lt $tamanhos.Count; $i++) {
    $t = $tamanhos[$i]; $dado = $pngs[$i]
    $lado = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$lado); $w.Write([byte]$lado); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$dado.Length); $w.Write([UInt32]$offset)
    $offset += $dado.Length
}
foreach ($dado in $pngs) { $w.Write($dado) }
$w.Close()
"icone gerado: $Saida"
