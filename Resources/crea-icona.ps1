# Genera Resources\icon.ico (16/24/32/48/64/128/256 px, voci PNG) con System.Drawing.
# Disegno: disco blu con anello, e un filo a piombo bianco che scende (la "sonda").
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot 'icon.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, $s * 0.06)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($s - 2*$pad), ($s - 2*$pad)

    # sfondo: cerchio blu (#2a78d6) con bordo piu' scuro
    $blue = [System.Drawing.Color]::FromArgb(255, 42, 120, 214)
    $dark = [System.Drawing.Color]::FromArgb(255, 24, 79, 149)
    $b = New-Object System.Drawing.SolidBrush $blue
    $g.FillEllipse($b, $rect)
    if ($s -ge 24) {
        $p = New-Object System.Drawing.Pen $dark, ([Math]::Max(1, $s * 0.04))
        $g.DrawEllipse($p, $rect)
        $p.Dispose()
    }

    # anello del disco (piu' chiaro)
    $ringW = [Math]::Max(1, $s * 0.07)
    $inset = $s * 0.22
    $ring = New-Object System.Drawing.RectangleF $inset, $inset, ($s - 2*$inset), ($s - 2*$inset)
    $lightPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), $ringW
    $g.DrawEllipse($lightPen, $ring)
    $lightPen.Dispose()

    # filo a piombo: linea verticale bianca dal bordo alto al centro, con pallina in fondo
    $white = [System.Drawing.Color]::White
    $lw = [Math]::Max(1, $s * 0.075)
    $wp = New-Object System.Drawing.Pen $white, $lw
    $wp.StartCap = 'Round'; $wp.EndCap = 'Round'
    $cx = $s / 2
    $g.DrawLine($wp, [single]$cx, [single]($s * 0.14), [single]$cx, [single]($s * 0.60))
    $wp.Dispose()
    $wb = New-Object System.Drawing.SolidBrush $white
    $r = $s * 0.10
    $g.FillEllipse($wb, [single]($cx - $r), [single]($s * 0.60 - $r*0.2), [single](2*$r), [single](2*$r))
    $wb.Dispose()
    $b.Dispose()
    $g.Dispose()
    return $bmp
}

$pngs = @()
foreach ($s in $sizes) {
    $bmp = Draw $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@($s, $ms.ToArray())
    $bmp.Dispose()
}

# ICO: header (6 byte) + N voci (16 byte) + dati
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $s = $p[0]; $data = $p[1]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0)      # colori
    $bw.Write([byte]0)      # riservato
    $bw.Write([uint16]1)    # piani
    $bw.Write([uint16]32)   # bit per pixel
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($p in $pngs) { $bw.Write($p[1]) }
$bw.Flush(); $fs.Close()
Write-Host "Icona scritta: $out ($($pngs.Count) dimensioni)"
