# Package the image-generated RGBA artwork as Windows icons; preserve its alpha.
param([string]$Source = "$PSScriptRoot/../Data/Branding/microphone-states.png")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/SharpVoice.UI/Assets'
[void][System.IO.Directory]::CreateDirectory((Join-Path $root '.build-cache'))
$sheet = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source).Path)
$sizes = @(16,20,24,32,40,48,64,96,128,256)
$names = @('neutral','recording','error')
$bounds = @()
try {
    if ($sheet.Width % 3 -ne 0) { throw 'Expected three equal icon cells.' }
    $cell = [int]($sheet.Width / 3)
    for ($state = 0; $state -lt 3; $state++) {
        $left=$cell; $top=$sheet.Height; $right=-1; $bottom=-1
        for ($y=0; $y -lt $sheet.Height; $y++) {
            for ($x=0; $x -lt $cell; $x++) {
                if ($sheet.GetPixel($state*$cell+$x,$y).A -ge 128) {
                    $left=[Math]::Min($left,$x); $right=[Math]::Max($right,$x)
                    $top=[Math]::Min($top,$y); $bottom=[Math]::Max($bottom,$y)
                }
            }
        }
        if ($right -lt $left) { throw 'Missing opaque icon content.' }
        $bounds += [System.Drawing.Rectangle]::new($state*$cell+$left,$top,$right-$left+1,$bottom-$top+1)
    }
    # Common scale and baseline prevent size jumps during state changes.
    $extent = ($bounds | ForEach-Object { [Math]::Max($_.Width,$_.Height) } | Measure-Object -Maximum).Maximum
    $preview = [System.Drawing.Bitmap]::new(720,340)
    $pg = [System.Drawing.Graphics]::FromImage($preview)
    try {
        $pg.Clear([System.Drawing.Color]::FromArgb(245,246,248))
        $dark=[System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(24,27,32))
        try { $pg.FillRectangle($dark,0,170,720,170) } finally { $dark.Dispose() }
        for ($state=0; $state -lt 3; $state++) {
            $payloads=@(); $entries=@(); $offset=6+16*$sizes.Count
            foreach ($size in $sizes) {
                $bitmap=[System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                $g=[System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $g.Clear([System.Drawing.Color]::Transparent)
                    $g.CompositingMode=[System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $scale=($size*0.94)/$extent
                    $b=$bounds[$state]
                    $w=[single]($b.Width*$scale); $h=[single]($b.Height*$scale)
                    $dest=[System.Drawing.RectangleF]::new([single](($size-$w)/2),[single]($size*0.97-$h),$w,$h)
                    $g.DrawImage($sheet,$dest,$b,[System.Drawing.GraphicsUnit]::Pixel)
                } finally { $g.Dispose() }
                try {
                    if ($bitmap.GetPixel(0,0).A -ne 0 -or $bitmap.GetPixel($size-1,$size-1).A -ne 0) { throw 'Icon corners must be transparent.' }
                    if ($size -in @(16,32,128)) {
                        $column = @{16=0;32=1;128=2}[$size]
                        foreach ($background in @(0,170)) { $pg.DrawImageUnscaled($bitmap,($state*240)+($column*52)+8,($background+15)) }
                    }
                    if ($size -eq 256) { $bitmap.Save((Join-Path $assets "microphone-$($names[$state]).png"),[System.Drawing.Imaging.ImageFormat]::Png) }
                    $rect=[System.Drawing.Rectangle]::new(0,0,$size,$size)
                    $locked=$bitmap.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                    try {
                        $pixels=[byte[]]::new($size*$size*4)
                        [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0,$pixels,0,$pixels.Length)
                    } finally { $bitmap.UnlockBits($locked) }
                    $stream=[System.IO.MemoryStream]::new(); $writer=[System.IO.BinaryWriter]::new($stream)
                    try {
                        $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size*2))
                        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0); $writer.Write([uint32]$pixels.Length)
                        1..4 | ForEach-Object { $writer.Write([uint32]0) }
                        for ($y=$size-1; $y -ge 0; $y--) { $writer.Write($pixels,$y*$size*4,$size*4) }
                        $stride=[int]([Math]::Ceiling($size/32)*4)
                        for ($y=$size-1; $y -ge 0; $y--) {
                            $mask=[byte[]]::new($stride)
                            for ($x=0; $x -lt $size; $x++) { if ($pixels[($y*$size+$x)*4+3] -eq 0) { $mask[[int][Math]::Floor($x/8)] = $mask[[int][Math]::Floor($x/8)] -bor (128 -shr ($x%8)) } }
                            $writer.Write($mask)
                        }
                        $payload=$stream.ToArray()
                    } finally { $writer.Dispose(); $stream.Dispose() }
                    $entries += ,@($size,$payload.Length,$offset)
                    $payloads += ,$payload
                    $offset += $payload.Length
                } finally { $bitmap.Dispose() }
            }
            $target=[System.IO.File]::Create((Join-Path $assets "microphone-$($names[$state]).ico")); $writer=[System.IO.BinaryWriter]::new($target)
            try {
                $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
                foreach ($entry in $entries) {
                    $writer.Write([byte]($entry[0]%256)); $writer.Write([byte]($entry[0]%256)); $writer.Write([byte]0); $writer.Write([byte]0)
                    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$entry[1]); $writer.Write([uint32]$entry[2])
                }
                foreach ($payload in $payloads) { $writer.Write([byte[]]$payload) }
            } finally { $writer.Dispose(); $target.Dispose() }
            Write-Output "Packaged $($names[$state]): $($sizes -join ', ') px; source bounds $($bounds[$state])"
        }
        $preview.Save((Join-Path $root '.build-cache/branded-icons-preview.png'),[System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $pg.Dispose(); $preview.Dispose() }
} finally { $sheet.Dispose() }
