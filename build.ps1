# 构建 DeepSeekWidget（.NET 8 + Windows App SDK / WinUI 3）
#
# 托盘菜单由 Microsoft.UI.Xaml 的 MenuFlyout 渲染，所以构建需要：
#   - .NET 8 SDK（用户级安装即可，例如 %USERPROFILE%\.dotnet）
#   - 目标机器需有 .NET 8 Desktop Runtime 与 Windows App Runtime；
#     安装器：winget install Microsoft.DotNet.DesktopRuntime.8
#             winget install Microsoft.WindowsAppRuntime.1.7   （或与包版本匹配的版本）
#
# 用法：仓库根目录执行 .\build.ps1
# 产物：dist\DeepSeekWidget.exe（框架依赖，非单文件）
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'DeepSeekWidget.csproj'
$dist = Join-Path $root 'dist'
$rid = 'win-x64'

function Get-Sdks([string]$exe) {
    try { return @(& $exe --list-sdks 2>$null) } catch { return @() }
}

# 机器上可能同时存在“只有运行时、没有 SDK”的系统 dotnet 与用户级 SDK，
# 这里逐个探测，挑第一个真正带 SDK 的
function Find-Dotnet {
    $candidates = New-Object System.Collections.Generic.List[string]
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates.Add($onPath.Source) }
    $local = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $local) { $candidates.Add($local) }
    foreach ($c in $candidates) {
        if (-not (Test-Path $c)) { continue }
        $sdks = Get-Sdks $c
        if ($sdks.Count -gt 0) { return @{ Exe = $c; Sdks = $sdks } }
    }
    throw '未找到带 SDK 的 dotnet。请安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0'
}

$found = Find-Dotnet
$dotnet = $found.Exe
Write-Host ("dotnet: " + $dotnet)
foreach ($s in $found.Sdks) { Write-Host ("SDK: " + $s) }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host '发布中 ...'
& $dotnet publish $proj -c Release -r $rid --self-contained false `
    -p:DebugType=none -p:GenerateDocumentationFile=false `
    -o $dist
if ($LASTEXITCODE -ne 0) { throw '发布失败' }

$exe = Join-Path $dist 'DeepSeekWidget.exe'
if (-not (Test-Path $exe)) { throw ('未生成 ' + $exe) }
$size = (Get-Item $exe).Length
Write-Host ("OK -> " + $exe + "  (" + $size + " bytes)")
Write-Host ('输出目录: ' + $dist + '（含依赖 DLL，需连同目录一起分发）')
