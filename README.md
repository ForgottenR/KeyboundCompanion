# 键影随行

一键随行，键影从容。

适用于《最终幻想 XIV》国服「魔光键影」的 Dalamud 高分助手，使用简体中文界面，面向 Dalamud API 15。

## 功能

- **高分目标策略**：依次考虑屏障防御、紧急读条、魔法罐、接近屏障的敌人、首领和死亡词条补分。目标选择使用游戏内角色类型、血量、屏幕位置和读条状态，不再固定首领优先。
- **提交效果闭环**：每次原生提交后等待血量下降、词条变化、读条变化、目标消失或分数增长。调用次数只计为尝试，不再把“返回匹配”直接当作攻击成功。
- **停顿保护与自动诊断**：超过时限未观察到效果会退避，连续三次未确认后暂停自动执行。提交、效果、未匹配、停顿、安全门控和异常会写入 JSONL 诊断文件。
- **机制安全门控**：识别停止输入技能及其读条进度，在落地前和效果期间停止提交与防闲置。前置阈值和读条后等待提供独立毫秒滑条，默认 1800 / 3500 ms，修改立即生效并保存。游戏自身的输入许可、锁定秒数、确认框、输入焦点和禁止输入状态拥有最高否决权。
- **手动单次提交**：一次点击只产生一次请求；在途提交、冷却或场景变化时不会排队补发。
- **自动高分提交**：首次使用默认关闭，之后保存选择。基础延迟可在保护开启时设为 1000～10000 毫秒、关闭时设为 100～10000 毫秒，每轮保留 ±20% 随机，但只有确认上次提交后才开始下一轮计时。
- **1 秒提交保护**：默认开启，手动、自动和可选的错误防闲置提交共用冷却；未匹配也计算冷却。关闭后取消该限制，重新开启时恢复保护。
- **窗口随副本显示**：启动或重载时不主动弹窗，之后进本自动显示、出本隐藏。副本外可用 `/ktgclick` 或插件设置入口打开设置；实际提交只在副本内且窗口允许执行时进行。
- **可选前台防闲置**：新配置默认关闭，旧配置中的“错误输入 1 + Enter”会迁移为不影响词条的“功能键与方向键”。只有自动模式运行、游戏前台且空闲计时达到阈值时才启动；在途提交、用户输入、其他输入焦点、确认框和停止输入机制都会暂停它。
- **自动出发和结算退出**：两个独立开关默认开启并保存，关闭插件窗口仍生效。只在匹配到魔光键影时点击“出发”，只在魔光键影结算窗口点击“退出”。按钮连续可用 1 秒后执行，同一个弹窗只点击一次。排队仍由你发起。

原生结构、动作 ID 和函数签名可能随客户端更新变化。当前源码已完成本地静态核对、离线测试和 Release 构建；整局实机稳定性、防闲置效果及高分收益仍以验收结果为准。

## 安装与使用

公开插件库和 [Releases](https://github.com/ForgottenR/KeyboundCompanion/releases) 中的安装包以实际发布版本为准。当前源码构建版请使用开发模式，不要与插件库版本或另一份开发版同时加载。

1. 构建插件，或取得构建产物。
2. 在 Dalamud 设置的开发插件位置中添加 `KeyboundClick.dll` 的完整路径。
3. 在插件安装器的 Dev Tools > Installed Dev Plugins 中启用 **键影随行**。
4. 进入魔光键影，确认窗口中的状态与候选目标，再点击“提交一次”或勾选“自动高分提交”。

任何地点都可用 `/ktgclick` 手动打开设置。插件加载或重载时不主动弹窗。

## 构建与测试

需要 .NET 10 SDK 和国服 Dalamud API 15 对应的开发程序集。项目使用 `Dalamud.NET.Sdk/15.0.0`，宿主 DLL 不包含在仓库中。

默认读取 `%APPDATA%\XIVLauncherCN\addon\Hooks\dev`，也可用 `DALAMUD_HOME` 指向其他 Dalamud 开发目录。

在仓库根目录运行：

```powershell
dotnet restore KeyboundCompanion.slnx --locked-mode
dotnet build KeyboundCompanion.slnx -c Release --no-restore
dotnet run --project KeyboundClick.Tests/KeyboundClick.Tests.csproj -c Release --no-build
```

主要产物：

```text
KeyboundClick/bin/x64/Release/KeyboundClick.dll
KeyboundClick/bin/x64/Release/KeyboundClick.json
KeyboundClick/bin/x64/Release/KeyboundClick/latest.zip
```

开发模式使用根输出目录中的 DLL 和自动生成的 JSON。`latest.zip` 内直接包含 DLL、JSON、依赖元数据和第三方许可说明，没有多套一层文件夹。

## 自动诊断

诊断文件默认位于：

```text
%APPDATA%\XIVLauncherCN\pluginConfigs\KeyboundClick\diagnostics
```

每次插件会话生成一个 JSONL 文件，记录单调时钟、提交目标、匹配返回、血量、分数、效果确认、停顿和安全状态变化。实机出现异常时保留发生问题当次的文件即可，也可以点击插件窗口中的“打开诊断目录”。

## 兼容性与实现

当前面向国服客户端与 Dalamud API 15。提交、实体收集、读条更新和分数更新四组特征码已在版本标记为 `2026.10.06.0000.0000` 的本地国服客户端中唯一匹配，并已进行反汇编核对；插件启动时会再次检查这些签名，任一不匹配都会禁用提交。

插件直接读取 KTG HUD Agent、输入组件和对象表，原生读写只发生在 Framework 主线程。`SubmissionCoordinator`、目标策略和安全门控为独立纯逻辑，可在没有游戏进程时运行回归测试。

原生结构参考 [FFXIVClientStructs.Expansion](https://github.com/AtmoOmen/FFXIVClientStructs.Expansion/tree/22b9e96bca56aab9d18a60956dfb71c14d3c50c2)，相关 MIT 许可保留在 [第三方许可说明](KeyboundClick/THIRD-PARTY-NOTICES.txt)。

前台防闲置按键方案参考 [SiGeoMa/ff14-keybound-brawler](https://github.com/SiGeoMa/ff14-keybound-brawler/blob/a20c4687f029049c9bd4bf90120c8a5fa66a2324/dalamud-plugin/KeyboundBrawler/Core/AutoTyper.cs)。按键调度、前台检查与释放处理为独立实现。
