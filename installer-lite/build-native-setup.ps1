param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root 'DeepSeekWidget.Lite.csproj'
$payload = Join-Path $root 'installer-lite\payload'
$objLite = Join-Path $root 'obj\lite'
$dist = Join-Path $root 'dist'
$iss = Join-Path $root 'installer-lite\setup.iss'
$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
)

function Find-Dotnet {
    $local = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if ((Test-Path -LiteralPath $local) -and ((& $local --list-sdks 2>$null).Count -gt 0)) { return $local }
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath -and (& $onPath.Source --list-sdks 2>$null).Count -gt 0) { return $onPath.Source }
    throw '未找到带 SDK 的 dotnet。'
}

$dotnet = Find-Dotnet
if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload, $objLite, $dist | Out-Null

$assets = @{
    version = 3
    targets = @{ 'net8.0-windows' = @{} }
    libraries = @{}
    projectFileDependencyGroups = @{ 'net8.0-windows' = @() }
    packageFolders = @{ (Join-Path $env:USERPROFILE '.nuget\packages') = @{} }
    project = @{
        version = '1.6.0'
        restore = @{
            projectUniqueName = $project
            projectName = 'DeepSeekWidget.Lite'
            projectPath = $project
            packagesPath = (Join-Path $env:USERPROFILE '.nuget\packages')
            outputPath = $objLite
            projectStyle = 'PackageReference'
            crossTargeting = $false
            configFilePaths = @()
            originalTargetFrameworks = @('net8.0-windows')
            sources = @{}
            frameworks = @{ 'net8.0-windows' = @{ targetAlias = 'net8.0-windows'; projectReferences = @{} } }
        }
        frameworks = @{
            'net8.0-windows' = @{
                targetAlias = 'net8.0-windows'
                dependencies = @{}
                imports = @()
                assetTargetFallback = $true
                warn = $true
                frameworkReferences = @{
                    'Microsoft.NETCore.App' = @{ privateAssets = 'all' }
                    'Microsoft.WindowsDesktop.App.WPF' = @{ privateAssets = 'all' }
                    'Microsoft.WindowsDesktop.App.WindowsForms' = @{ privateAssets = 'all' }
                }
                runtimeIdentifierGraphPath = (Join-Path $env:USERPROFILE '.dotnet\sdk\8.0.425\PortableRuntimeIdentifierGraph.json')
            }
        }
    }
}
$assets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $objLite 'project.assets.json') -Encoding UTF8

& $dotnet publish $project -c $Configuration --no-restore -p:MSBuildProjectExtensionsPath=$objLite -o $payload
if ($LASTEXITCODE -ne 0) { throw '小体积应用发布失败' }
Get-ChildItem -LiteralPath $payload -Filter '*.xml' -File | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $root 'installer-lite\uninstall.ps1') -Destination (Join-Path $payload 'uninstall.ps1') -Force

$iscc = $null
foreach ($candidate in $isccCandidates) { if (Test-Path -LiteralPath $candidate) { $iscc = $candidate; break } }
if (-not $iscc) { throw '未找到 Inno Setup 编译器 ISCC.exe。' }

& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw '原生安装器构建失败' }

$output = Join-Path $dist 'DeepSeekWidget-Setup-1.6.0.exe'
if (-not (Test-Path -LiteralPath $output)) { throw ('未生成 ' + $output) }
$hash = Get-FileHash -LiteralPath $output -Algorithm SHA256
Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
$hash | Format-List Algorithm, Hash



