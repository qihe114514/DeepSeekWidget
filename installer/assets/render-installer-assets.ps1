param(
    [string]$ChromePath = 'C:\Program Files\Google\Chrome\Application\chrome.exe',
    [int]$Scale = 4
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $assetDir 'source'
$previewDir = Join-Path $assetDir 'preview'
New-Item -ItemType Directory -Force -Path $sourceDir, $previewDir | Out-Null

if (-not (Test-Path -LiteralPath $ChromePath)) {
    throw "Chrome not found: $ChromePath"
}

# Keep the HTML source using the same project icon as the application and installer.
$iconPng = Join-Path $sourceDir 'app-icon-256.png'
$icon = [System.Drawing.Bitmap]::new((Join-Path $assetDir 'installer.ico'))
try {
    $icon.Save($iconPng, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $icon.Dispose()
}

function Render-HtmlAsset([string]$htmlName, [int]$width, [int]$height, [string]$outputPng) {
    $htmlPath = Join-Path $sourceDir $htmlName
    if (-not (Test-Path -LiteralPath $htmlPath)) {
        throw "HTML source not found: $htmlPath"
    }

    $uri = [System.Uri]::new($htmlPath).AbsoluteUri
    $profileDir = Join-Path $env:TEMP ('DeepSeekWidgetChrome-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $profileDir | Out-Null

    if (Test-Path -LiteralPath $outputPng) {
        Remove-Item -LiteralPath $outputPng -Force
    }

    $arguments = @(
        '--headless=new',
        '--no-sandbox',
        '--disable-gpu',
        '--disable-gpu-compositing',
        '--disable-software-rasterizer',
        '--hide-scrollbars',
        "--user-data-dir=$profileDir",
        "--force-device-scale-factor=$Scale",
        "--window-size=$width,$height",
        "--screenshot=$outputPng",
        $uri
    )
    & $ChromePath @arguments
    for ($i = 0; $i -lt 20 -and -not (Test-Path -LiteralPath $outputPng); $i++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $outputPng)) {
        throw "Chrome did not produce: $outputPng"
    }

    $image = [System.Drawing.Image]::FromFile($outputPng)
    try {
        $expectedWidth = $width * $Scale
        $expectedHeight = $height * $Scale
        if ($image.Width -ne $expectedWidth -or $image.Height -ne $expectedHeight) {
            throw "Unexpected render size for ${htmlName}: $($image.Width)x$($image.Height), expected ${expectedWidth}x${expectedHeight}"
        }
    } finally {
        $image.Dispose()
    }
}

function Convert-To24BitBmp([string]$sourcePng, [string]$destinationBmp, [int]$width, [int]$height) {
    $source = [System.Drawing.Bitmap]::FromFile($sourcePng)
    try {
        $destination = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($destination)
            try {
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $width, $height)
            } finally {
                $graphics.Dispose()
            }
            $destination.Save($destinationBmp, [System.Drawing.Imaging.ImageFormat]::Bmp)
        } finally {
            $destination.Dispose()
        }
    } finally {
        $source.Dispose()
    }
}

$banner4x = Join-Path $previewDir 'wix-banner@4x.png'
$dialog4x = Join-Path $previewDir 'wix-dialog@4x.png'

Render-HtmlAsset 'wix-banner.html' 493 58 $banner4x
Render-HtmlAsset 'wix-dialog.html' 493 312 $dialog4x

Convert-To24BitBmp $banner4x (Join-Path $assetDir 'wix-banner.bmp') 493 58
Convert-To24BitBmp $dialog4x (Join-Path $assetDir 'wix-dialog.bmp') 493 312

Get-Item (Join-Path $assetDir 'wix-banner.bmp'), (Join-Path $assetDir 'wix-dialog.bmp'), $banner4x, $dialog4x |
    Select-Object FullName, Length





