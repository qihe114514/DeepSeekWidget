# DeepSeek 桌面余额小组件

一个免安装的 Windows 桌面小工具：开机自启后常驻桌面右上角，像一个手机小组件，实时显示 DeepSeek API 余额与今日用量。卡片上只有一个可点击的“充值”按钮，其余区域点击穿透到桌面（不影响操作桌面图标/文件）；右下角托盘右键可以管理自启动、登录、移动位置与退出。

## 功能

- 桌面圆角半透明卡片，默认右上角，启动即显示
- 余额：官方接口（`api.deepseek.com/user/balance`），显示充值余额与赠送余额
- 今日用量：平台内部接口（`platform.deepseek.com/api/v0/usage/cost`、`/usage/amount`），显示今日消费与 Token 数
- 每 2 分钟自动刷新（可在配置文件中修改）
- “充值”按钮：弹出内嵌窗口打开充值页，关闭后立即刷新
- 托盘右键菜单：移动位置 / 重置位置 / 开机自启动 / 登录 DeepSeek 账号… / 退出
- 非按钮区域点击穿透；点击“移动位置”后整卡可拖动，松手即保存位置并锁定；“重置位置”回到默认右上角
- 卡片背景使用 Windows 亚克力（毛玻璃）效果（需系统开启“透明效果”）
- 登录与充值窗口为内嵌浏览器（WebView2），登录一次后自动保存登录态

## 使用

1. 双击 `dist\DeepSeekWidget.exe` 运行（可先把它放到任意目录，例如 `D:\Tools\`）。
2. 右键右下角托盘的图标 → **登录 DeepSeek 账号…**
3. 填写 API Key（`platform.deepseek.com` → API Keys），并在下方内嵌页面中登录你的账号。
4. 检测到登录状态后点击“保存登录态”，组件即开始显示余额和今日用量。
5. 如需开机自启：托盘右键 → 勾选“开机自启动”。

## 说明

- “今日用量”来自平台内部接口，属非官方接口，可能随平台改版失效；失效时组件只显示余额并在用量区域提示（接口原始响应会保存到 `%LOCALAPPDATA%\DeepSeekWidget\debug\` 便于排查）。
- 若系统只注册了 32 位 WebView2 运行时（64 位程序默认发现不到），程序会自动使用已安装的运行时目录，无需额外安装。
- 配置与登录态（DPAPI 加密）保存在 `%APPDATA%\DeepSeekWidget\config.json`；刷新间隔可改其中的 `RefreshSeconds`（秒，最小 30）。
- 卸载：托盘取消“开机自启动” → 退出 → 删除 exe 和 `%APPDATA%\DeepSeekWidget` 目录即可。
- 调试参数：`DeepSeekWidget.exe --login` 启动后直接打开登录窗口；`--recharge` 直接打开充值窗口。

## 构建

本机需为 Windows 10/11（自带 .NET Framework 4.8 与编译器）。在仓库目录运行：

```powershell
.\build.ps1
```

首次构建会自动联网下载 WebView2 SDK 到 `vendor\`（之后可离线重新构建），产物为单个 `dist\DeepSeekWidget.exe`，无需安装任何运行时（WebView2 运行时使用系统已装的）。

## 目录结构

```
src\         C# 源码
vendor\      构建期使用的 WebView2 SDK（不随 exe 交付）
dist\        构建产物：DeepSeekWidget.exe（唯一交付文件）
build.ps1    构建脚本
```
