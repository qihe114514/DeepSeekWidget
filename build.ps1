# 构建 DeepSeekWidget.exe（自包含单文件，内嵌 WebView2 SDK DLL）
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$pkg = Join-Path $root 'vendor\pkg'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw '未找到 .NET Framework 编译器 csc.exe'
}

# 首次构建：vendor 缺失时从 NuGet 下载一次 WebView2 SDK
$core = Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.Core.dll'
if (-not (Test-Path $core)) {
    Write-Host '首次构建：下载 WebView2 SDK ...'
    $tmp = Join-Path $env:TEMP 'microsoft.web.webview2.nupkg'
    Invoke-WebRequest -Uri 'https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2' -OutFile $tmp -UseBasicParsing
    New-Item -ItemType Directory -Force -Path $pkg | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($tmp, $pkg)
}

function Resolve-Gac([string]$name) {
    $dirs = @(
        (Join-Path $env:WINDIR ("Microsoft.NET\assembly\GAC_MSIL\" + $name)),
        (Join-Path $fw64 'WPF'),
        (Join-Path $fw32 'WPF'),
        $fw64,
        $fw32
    )
    $file = $null
    foreach ($dir in $dirs) {
        if (-not (Test-Path $dir)) { continue }
        $file = Get-ChildItem $dir -Recurse -Filter ($name + '.dll') -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($file) { break }
    }
    if (-not $file) { throw "GAC 中未找到 $name" }
    return $file.FullName
}

$fw64 = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$fw32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'

$refs = @(
    (Resolve-Gac 'WindowsBase'),
    (Resolve-Gac 'PresentationCore'),
    (Resolve-Gac 'PresentationFramework'),
    (Resolve-Gac 'System.Xaml'),
    (Resolve-Gac 'System.Net.Http'),
    (Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.Wpf.dll')
) | ForEach-Object { '/r:' + $_ }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$out = Join-Path $dist 'DeepSeekWidget.exe'

$resourcePairs = @(
    @((Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.Core.dll'), 'DeepSeekWidget.Bin.WebView2.Core.dll'),
    @((Join-Path $pkg 'lib\net462\Microsoft.Web.WebView2.Wpf.dll'), 'DeepSeekWidget.Bin.WebView2.Wpf.dll'),
    @((Join-Path $pkg 'runtimes\win-x64\native\WebView2Loader.dll'), 'DeepSeekWidget.Bin.WebView2Loader.x64.dll'),
    @((Join-Path $pkg 'runtimes\win-x86\native\WebView2Loader.dll'), 'DeepSeekWidget.Bin.WebView2Loader.x86.dll')
)
$resArgs = @()
foreach ($pair in $resourcePairs) {
    if (-not (Test-Path $pair[0])) { throw '缺少 WebView2 SDK 文件：' + $pair[0] }
    $resArgs += '/resource:' + $pair[0] + ',' + $pair[1]
}

$sources = @(Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName })
$manifest = Join-Path $src 'app.manifest'
$icon = Join-Path $src 'app.ico'

$outArg = '/out:' + $out
$manifestArg = '/win32manifest:' + $manifest
$iconArg = '/win32icon:' + $icon

$args = @(
    '/nologo', '/target:winexe', '/optimize+', '/codepage:65001',
    $outArg
) + $refs + $resArgs
if (Test-Path $manifest) { $args += $manifestArg }
if (Test-Path $icon) { $args += $iconArg }
$args += $sources

Write-Host '编译中 ...'
& $csc @args
if ($LASTEXITCODE -ne 0) { throw '编译失败' }

$size = (Get-Item $out).Length
Write-Host ("OK -> " + $out + "  (" + $size + " bytes)")
