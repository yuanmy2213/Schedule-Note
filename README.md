# 今日笺 · Schedule-Note

Windows 每日办公事项便利贴。早上写下重要事项，开始和完成时点击一次，自动保留实际时间，月底导出 Excel。

[下载软件](https://github.com/yuanmy2213/Schedule-Note/releases/latest) · [构建和测试](https://github.com/yuanmy2213/Schedule-Note/actions)

## 使用

1. 在 Releases 下载 `win-x64-portable.zip`，完整解压到固定目录后运行 `JinRiJian.exe`。便携包自带 .NET，无需安装运行时。
2. 底部填写事项名称，按 Enter 或点击“＋”。预计起止时间均可留空。
3. 点击“开始”“完成”，自动记录实际时间。误点完成可在 10 秒内撤销，也可右键事项修正。
4. 默认保留最初的标准界面。标题栏“精简”切到精简模式，精简模式“⋯”可切回。
5. “规划今日”集中录入计划；“历史记录”按月份回看、导出两张工作表。

两种模式共用数据、分别记住窗口大小和位置，支持原生边缘缩放、置顶、托盘、可选开机显示。普通关闭会隐藏到托盘，托盘“退出”才结束程序。“显示桌面 / Win+D”遵循 Windows 系统行为。

## 数据

- 默认位置：`%LOCALAPPDATA%\JinRiJian\daily.db`。无需账号，不上传事项到外部服务。
- 跨日未完成事项以原 ID 和归属日期延续，避免重复记录；跨月完成仍归原月份导出。
- 开始—完成的间隔叫“事项历时”，包含中途打断，不汇总为净工时。
- 每日自动快照保留最近 30 份；设置中可手动备份、恢复，恢复前另留安全备份。
- 删除为软删除，可在历史窗口“显示已删除事项”后右键恢复。

## 构建

Windows 11 x64 为主要使用目标。发布包由 GitHub Windows runner 构建；Windows 10、多显示器及不同缩放比例仍应在目标设备验收。

安装 `.NET 10 SDK` 后：

```powershell
dotnet run --project tests/JinRiJian.Tests -c Release
./scripts/build.ps1 -Version 1.0.0
```

产物在 `artifacts/packages`：

- `portable.zip`：自带运行时，解压即用。
- `framework.zip`：较小，要求先安装 .NET 10 Desktop Runtime x64。
- `SHA256SUMS.txt`：软件包校验值。

程序目前未使用代码签名证书；Windows 首次运行可能显示“未知发布者”。

## 测试

核心测试覆盖时间可空、状态迁移、重复及并发点击、事务回滚、跨日跨月、时区偏移、软删除、SQLite 备份恢复、偏好持久化、OOXML 文本与日期、Unicode 和公式注入处理。openpyxl 作为独立消费者读取并校验实际 Excel 文件。

Windows 自动化使用真实 WPF 窗口和控件事件，检查标准/精简切换、时间保留、两套窗口尺寸、折叠、管理页切换、编辑页、托盘状态、隐藏恢复、置顶、原生可缩放 HWND、截图与窄窗口。每次成功构建的 Release 附带测试报告和原生截图。

物理多屏、硬件 DPI 切换、Windows Explorer 重启、Win+D、真实登录后的启动项以及 Windows SmartScreen 的交互属于目标设备验收范围，CI 不能替代这些检查。详情见 [测试说明](docs/测试说明.md)。

## 结构

- `src/JinRiJian.Core`：业务规则、SQLite 事务、备份、Excel。
- `src/JinRiJian.App`：原生 WPF 双模式窗口、规划/历史、设置、托盘与单实例。
- `tests/JinRiJian.Tests`：跨平台可重复运行的自动化业务测试。
- `scripts/build.ps1`：Windows 原生测试、两种发布包和哈希。

用户数据不会写入程序解压目录，升级时替换软件文件即可，数据目录保持独立。
