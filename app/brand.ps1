Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot 'Assets'

function Draw-Icon($g, [single]$x, [single]$y, [single]$size) {
    $k = $size / 800
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), (50 * $k)
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round

    $l = $x + 235 * $k; $t = $y + 65 * $k; $w = 330 * $k; $h = 670 * $k; $d = 150 * $k
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($l, $t, $d, $d, 180, 90)
    $path.AddArc($l + $w - $d, $t, $d, $d, 270, 90)
    $path.AddArc($l + $w - $d, $t + $h - $d, $d, $d, 0, 90)
    $path.AddArc($l, $t + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.DrawPath($pen, $path)

    $thin = New-Object Drawing.Pen ([Drawing.Color]::White), (36 * $k)
    $thin.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.DrawRectangle($thin, ($x + 315 * $k), ($y + 160 * $k), (170 * $k), (190 * $k))

    $r = 27 * $k
    foreach ($row in 0..2) {
        foreach ($col in 0..2) {
            $cx = $x + (330 + $col * 70) * $k; $cy = $y + (445 + $row * 82) * $k
            $g.FillEllipse([Drawing.Brushes]::White, ($cx - $r), ($cy - $r), (2 * $r), (2 * $r))
        }
    }
    $pen.Dispose(); $thin.Dispose(); $path.Dispose()
}

function Draw-Word($g, [single]$x, [single]$y, [single]$em) {
    $fmt = [Drawing.StringFormat]::GenericTypographic
    $fmt.FormatFlags = $fmt.FormatFlags -bor [Drawing.StringFormatFlags]::MeasureTrailingSpaces
    $bold = New-Object Drawing.Font 'Segoe UI', $em, ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
    $light = New-Object Drawing.Font 'Segoe UI Semilight', $em, ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
    $g.DrawString('Lu', $bold, [Drawing.Brushes]::White, $x, $y, $fmt)
    $lw = $g.MeasureString('Lu', $bold, 4000, $fmt).Width
    $g.DrawString('midlet', $light, [Drawing.Brushes]::White, ($x + $lw), $y, $fmt)
    $w = $lw + $g.MeasureString('midlet', $light, 4000, $fmt).Width
    $bold.Dispose(); $light.Dispose()
    return $w
}

function New-Canvas([int]$w, [int]$h) {
    $b = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return @{ b = $b; g = $g }
}

function Save-Icon([string]$name, [double]$rw, [double]$rh, [double]$fill) {
    $w = [int][Math]::Ceiling($rw); $h = [int][Math]::Ceiling($rh)
    $ss = 4
    $c = New-Canvas ($w * $ss) ($h * $ss)
    $size = [Math]::Min($w, $h) * $ss * $fill
    Draw-Icon $c.g ((($w * $ss) - $size) / 2) ((($h * $ss) - $size) / 2) $size
    $o = New-Canvas $w $h
    $o.g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $o.g.DrawImage($c.b, 0, 0, $w, $h)
    $o.b.Save((Join-Path $assets $name), [Drawing.Imaging.ImageFormat]::Png)
    $c.g.Dispose(); $c.b.Dispose(); $o.g.Dispose(); $o.b.Dispose()
}

Get-ChildItem $assets -Filter *.png | Remove-Item
$scales = 100, 125, 150, 200, 400
foreach ($s in $scales) {
    $f = $s / 100
    Save-Icon "Square150x150Logo.scale-$s.png" (150 * $f) (150 * $f) 0.62
    Save-Icon "Wide310x150Logo.scale-$s.png" (310 * $f) (150 * $f) 0.62
    Save-Icon "SmallTile.scale-$s.png" (71 * $f) (71 * $f) 0.72
    Save-Icon "LargeTile.scale-$s.png" (310 * $f) (310 * $f) 0.5
    Save-Icon "Square44x44Logo.scale-$s.png" (44 * $f) (44 * $f) 0.9
    Save-Icon "StoreLogo.scale-$s.png" (50 * $f) (50 * $f) 0.9

    $w = [int](620 * $f); $h = [int](300 * $f)
    $c = New-Canvas $w $h
    $icon = 112 * $f; $em = 62 * $f
    $probe = Draw-Word $c.g -4000 0 $em
    $total = $icon + (10 * $f) + $probe
    $left = ($w - $total) / 2
    Draw-Icon $c.g $left (($h - $icon) / 2) $icon
    [void](Draw-Word $c.g ($left + $icon + 10 * $f) (($h - $em * 1.33) / 2) $em)
    $c.b.Save((Join-Path $assets "SplashScreen.scale-$s.png"), [Drawing.Imaging.ImageFormat]::Png)
    $c.g.Dispose(); $c.b.Dispose()
}
foreach ($t in 16, 24, 32, 48, 256) {
    Save-Icon "Square44x44Logo.targetsize-$t.png" $t $t 0.94
    Save-Icon "Square44x44Logo.targetsize-${t}_altform-unplated.png" $t $t 0.94
}
