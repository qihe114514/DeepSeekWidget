param(
    [string]$Configuration = 'Release',
    [string]$ProxyUrl = '',

    [switch]$SkipValidation
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$installerDir = Join-Path $root 'installer'
$project = Join-Path $root 'DeepSeekWidget.csproj'
$wixProject = Join-Path $installerDir 'DeepSeekWidget.Installer.wixproj'
$publishDir = Join-Path $installerDir 'publish'
$dist = Join-Path $root 'dist'
$rid = 'win-x64'

function Get-Sdks([string]$exe) {
    try { return @(& $exe --list-sdks 2>$null) } catch { return @() }
}

function Find-Dotnet {
    $candidates = New-Object System.Collections.Generic.List[string]
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates.Add($onPath.Source) }
    $local = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $local) { $candidates.Add($local) }
    foreach ($candidate in $candidates) {
        if (-not (Test-Path $candidate)) { continue }
        if ((Get-Sdks $candidate).Count -gt 0) { return $candidate }
    }
    throw '未找到带 SDK 的 dotnet。请先安装 .NET 8 SDK。'
}

function Remove-GeneratedDirectory([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $resolved = (Resolve-Path -LiteralPath $path).Path
    $rootResolved = (Resolve-Path -LiteralPath $root).Path
    if (-not $resolved.StartsWith($rootResolved + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝删除工作区外目录: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$dotnet = Find-Dotnet
if ($ProxyUrl) {
    $env:HTTP_PROXY = $ProxyUrl
    $env:HTTPS_PROXY = $ProxyUrl
}
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_CERT_REVOCATION_MODE = 'offline'

Write-Host ("dotnet: " + $dotnet)
Write-Host '1/3 发布自包含应用（.NET + Windows App SDK）...'
Remove-GeneratedDirectory $publishDir
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
& $dotnet publish $project -c $Configuration -r $rid --self-contained true `
    -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false `
    -p:DebugType=none -p:GenerateDocumentationFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw '自包含发布失败' }

Write-Host '2/3 构建 MSI 安装向导（WiX v6）...'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$wixArgs = @("build", $wixProject, "-c", $Configuration, "-t:Rebuild", "-o", $dist)
if ($SkipValidation) { $wixArgs += "-p:SuppressValidation=true" }
& $dotnet @wixArgs
if ($LASTEXITCODE -ne 0) { throw 'MSI 构建失败' }

$msiName = 'DeepSeekWidget-1.5.0-x64.msi'
$built = Get-ChildItem -LiteralPath $dist -Recurse -File -Filter $msiName |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -eq $built) { throw ('未生成 ' + $msiName) }
$msi = Join-Path $dist $msiName
if ($built.FullName -ne $msi) { Copy-Item -LiteralPath $built.FullName -Destination $msi -Force }
$hash = Get-FileHash -LiteralPath $msi -Algorithm SHA256
$sizeMb = [Math]::Round((Get-Item -LiteralPath $msi).Length / 1MB, 1)

Write-Host '3/3 完成'
Write-Host ("MSI: " + $msi)
Write-Host ("大小: " + $sizeMb + " MB")
Write-Host ("SHA256: " + $hash.Hash)








