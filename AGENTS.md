# TShockPlrExporter Agent Guide

## 项目概览

TShockPlrExporter 是 TShock 服务器插件，用于在 TShock 的服务器端人物存档（SSC）数据和 Terraria 原生 `.plr` 文件之间导入、导出角色。主要代码面向：

- TShock `6.1.0`
- Terraria `1.4.5.6`
- .NET `9.0`

核心类型是 `Plugin`，它注册 `/player`、`/playerimport` 命令并编排后台任务。其余类型为 `internal` 实现细节，通过 `InternalsVisibleTo` 暴露给测试项目。

## 研究与网页内容

- 搜索或爬取网页内容时优先使用 `exa_mcp`。
- 只有在 `exa_mcp` 无法使用或无法完成任务时，才改用其他搜索、抓取工具。

## 环境与编码

- 开发环境优先按 Windows 11 + PowerShell 处理命令。
- 源码和文档使用 UTF-8，换行使用 LF；遵守 `.editorconfig` 和 `.gitattributes`。
- 不要提交 `bin/`、`obj/`、DLL 或其他构建产物。

## 构建与测试

在仓库根目录执行：

```powershell
dotnet restore TShockPlrExporter.csproj
dotnet build TShockPlrExporter.csproj -c Release --no-restore --nologo
dotnet test tests/TShockPlrExporter.Tests/TShockPlrExporter.Tests.csproj -c Release --nologo
```

聚焦单个测试项目时，可通过 `--filter` 缩小范围：

```powershell
dotnet test tests/TShockPlrExporter.Tests/TShockPlrExporter.Tests.csproj -c Release --filter FullyQualifiedName~ImportPathTests --nologo
```

提交前至少运行 Release 构建和完整测试。主项目启用了 `TreatWarningsAsErrors`，不得通过抑制警告来绕过问题，除非修改确有必要并解释原因。

## 代码风格

- 遵循 `.editorconfig`：4 空格缩进，C# 文件级 namespace，Allman 大括号，显式类型优先，不使用 `var`。
- 保持 nullable 注解准确；不要用 `!` 掩盖本可通过控制流或参数校验解决的问题。
- `using` 排序以 `System.*` 开头，随后是第三方和本项目命名空间。
- 公开 API 和复杂逻辑使用简洁的 XML 文档或说明性注释；注释解释不变量、并发约束或兼容性原因，不复述代码表面行为。
- 保持现有中文注释和日志风格。用户可见错误应简洁，详细异常与绝对路径只写服务器日志。

## 架构边界

- `Plugin.cs`：TShock 命令、权限、生命周期、任务互斥、结果汇总和在线玩家处理。
- `Data/`：数据库配置解析、账号查询、SSC 行读取与写入、编码/解码。读取应继续使用独立 ADO.NET 连接，不得复用 `TShock.DB` 的共享连接。
- `Exporting/`：构建 `Player`、生成安全文件名、写 `.plr`、备份轮转，以及把必要工作调度回主线程。
- `Importing/`：导入路径校验、读取 `.plr`、收敛危险字段，并通过 TShock 数据结构写入 SSC。
- `tests/`：xUnit 测试。新增纯逻辑、路径安全、数据编解码、线程队列或兼容性边界时，应补充对应测试。

## 文档分层与维护

- `README.md` 面向服务器管理员和普通使用者，只保留安装、命令、权限、目录、限制、导出内容范围和 FAQ；不要把深层实现细节或阶段性排障记录继续堆进 README。
- `docs/README.md` 是工程文档入口，负责路由架构、runbook 和 ADR。
- `docs/architecture.md` 维护组件边界、数据流、并发模型、兼容性和必须保持的安全不变量。
- `docs/runbooks/` 按风险场景维护可执行流程。当前必须保留 SSC 导入导出、在线玩家、SQLite/MySQL、备份恢复和主线程调度这些独立 runbook。
- `docs/decisions/` 记录关键架构决策。已接受的 ADR 不直接改写历史；调整决策时新增 ADR 并写清取代关系。
- 每次修改行为时同步更新对应文档：
  - 用户可见命令、权限、目录、限制或版本变化：更新 `README.md`。
  - 数据库字段、SQLite/MySQL 配置或兼容策略变化：更新 `docs/architecture.md` 和 `docs/runbooks/database-backends.md`。
  - 导入导出、在线玩家、备份或恢复流程变化：更新 `docs/architecture.md` 和受影响 runbook。
  - 后台任务、主线程队列、超时或关闭行为变化：更新 `docs/runbooks/main-thread-scheduling.md`。
  - 新的长期取舍：在 `docs/decisions/` 新增 ADR，并在相关文档中链接。
- runbook 至少包含适用范围、前置条件、操作步骤、安全边界、验证方式和失败后的下一步。

## 并发与运行时约束

- 不要在命令线程或 Terraria 主线程执行批量数据库查询、玩家构造或文件写盘；命令应调度到后台任务。
- 导出和导入共用任务互斥，同一时间只允许一个玩家存档任务。
- 只有后台线程可以调用 `MainThreadQueue.Invoke` 并阻塞等待；从主线程调用会造成死锁。
- 发给在线玩家的网络消息必须回到主线程；控制台输出可直接写 `TShock.Log`。
- 调整 `Main.ServerSideCharacter` 只能作为导出保存的短临界区回退方案，并必须在 `finally` 中恢复原值。不要把它当作常驻开关。
- 导出在线账号前先同步其 SSC 数据；导入前必须确保目标账号离线。在线旧会话可能覆盖导入结果。

## 数据与安全要求

- 所有数据库查询使用参数，不要拼接用户输入。
- 保持 SQLite 与 MySQL 兼容：避免 SQLite 专有 SQL 语法；读取 `tsCharacter` 时按列名取值，允许旧版本缺列后使用默认值。
- 不要在日志、聊天消息或异常中包含数据库连接字符串、密码或完整内部异常；用户消息可含短 trace ID，详细内容写日志。
- 导入参数只能是 `PlayerImports` 目录下的普通 `.plr` 文件名。继续拒绝绝对路径、子目录、路径穿越和其他扩展名。
- 导出文件名必须清洗并限制在目标目录内；防止 Windows 保留设备名、目录穿越和账号名归一化后互相覆盖。
- 覆盖已有 SSC 前必须先成功创建备份；备份失败时不得继续导入。
- 不要把绝对路径发给游戏内玩家，汇总消息统一显示 `tshock/PlayerExports` 等相对路径。

## 兼容性注意事项

- TShock、Terraria 或数据库字段变化时，优先保持旧配置兼容并记录降级行为。
- 插件只输出 `TShockPlrExporter.dll`。不要移除 `ExcludeAssets` / `PrivateAssets`，也不要把 TShock、Terraria、SQLite 或 MySQL 运行库复制到插件输出目录。
- 修改导出保存路径、命令行为、目录、权限或版本号时，同步更新 `README.md` 和对应工程文档。
- 发布新版本时，同时更新 `TShockPlrExporter.csproj` 的 `Version` 和 `Plugin.Version`，并确认启动日志中的版本一致。

## 提交与变更范围

- 提交信息使用简洁中文，例如 `修复导入超时后未释放备份`、`添加批量导出结果汇总测试`。
- 保持变更聚焦；不要顺手重排无关文件、更新无关依赖或引入大规模格式化差异。
- 修复缺陷时优先添加能复现问题的测试，再修改实现。
