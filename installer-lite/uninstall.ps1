$ErrorActionPreference = 'SilentlyContinue'
$folder = $PSScriptRoot
$startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\DeepSeek 余额小组件.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DeepSeek 余额小组件.lnk'
Remove-Item -LiteralPath $startMenu -Force
Remove-Item -LiteralPath $desktop -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DeepSeekWidget'
Remove-Item -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepSeekWidget' -Recurse -Force
$escaped = $folder.Replace('"','\"')
Start-Process -FilePath 'cmd.exe' -ArgumentList ('/c ping 127.0.0.1 -n 2 >nul & rmdir /s /q "' + $escaped + '"') -WindowStyle Hidden
