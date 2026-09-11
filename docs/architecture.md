# 架构

本文描述 TShockPlrExporter 的组件边界、主要数据流和维护时必须保持的不变量。用户使用方法见根目录 [`README.md`](../README.md)，具体故障操作见 [`runbooks/`](runbooks/)。

## 运行环境

- TShock `6.1.0`
- Terraria `1.4.5.6`
- .NET `9.0`
- TShock 后端：SQLite 或 MySQL

插件通过 TShock 反射加载 `TShockPlrExporter.Plugin`。除 `Plugin` 外，其余类型均为 `internal`，并通过 `InternalsVisibleTo` 提供给单元测试项目。

## 组件边界

| 组件 | 职责 | 不应承担 |
| --- | --- | --- |
| `Plugin.cs` | 注册命令和权限、维护插件生命周期、限制并发任务、编排导入导出、汇总结果 | 直接实现数据库字段映射和 `.plr` 文件编解码 |
| `Data/CharacterDatabase.cs` | 解析 SQLite/MySQL 配置、查询账号、读取 `tsCharacter`、事务写入 SSC | 依赖 TShock 的共享 ADO.NET 连接 |
| `Data/CharacterRecord.cs`、`Data/ExportAccount.cs` | 数据库行与账号的内存表示 | 处理进程级并发和文件系统操作 |
| `Exporting/PlrExporter.cs` | 把 SSC 记录还原为 `Player`、安全命名、写 `.plr`、导出备份与轮转 | 决定命令权限或向玩家发送消息 |
| `Exporting/MainThreadQueue.cs` | 把必须访问 Terraria/TShock 主线程状态的工作排队执行 | 执行批量数据库查询或长时间阻塞工作 |
| `Importing/PlrImporter.cs` | 校验导入路径、读取 `.plr`、规范化危险字段、构建 `PlayerData` 并写回 SSC | 查询并踢出在线玩家 |
| `tests/` | 覆盖纯逻辑、路径边界、数据编解码和主线程队列 | 代替真实 TShock 服务器上的端到端验收 |

## 数据流

### 导出

```text
/player
  -> Plugin 在后台线程启动任务
  -> CharacterDatabase 读取 Users 与 tsCharacter
  -> 同步在线目标玩家的 SSC 数据
  -> PlrExporter 构建 Terraria.Player
  -> 备份同名 .plr 并写出新文件
  -> 校验文件存在且非空
  -> Plugin 汇总结果并写日志
```

导出在后台线程执行，避免批量查询、玩家构造和文件写盘阻塞 TShock 主循环。写盘使用 Terraria 内部保存入口；只有内部入口不可用时，才在主线程短临界区内临时关闭 `Main.ServerSideCharacter`，并在 `finally` 中恢复。

### 导入

```text
/playerimport
  -> Plugin 在后台线程启动任务
  -> ResolveInputPath 校验 PlayerImports 下的普通 .plr 文件名
  -> PlrImporter 读取 .plr 并规范化危险字段
  -> 确保目标账号离线
  -> 若已有 SSC，先导出导入前备份
  -> CharacterDatabase 在事务中 upsert tsCharacter
  -> Plugin 汇总结果并写日志
```

导入只在确认文件可读且目标账号离线后才覆盖 SSC。备份失败、等待离线超时或玩家重新上线时，不继续写入。

## 数据模型与兼容性

TShock SSC 数据主要位于：

- `Users`：账号 ID 与用户名。
- `tsCharacter`：生命、魔力、外观、库存、Loadout、进度和死亡次数等角色数据。

数据库读取按列名查找，而不是依赖固定列序号。新版本缺少旧字段时使用默认值，并只记录一次缺列警告。SQL 必须同时兼容 SQLite 与 MySQL，所有用户输入通过参数传入。

读取连接从 TShock 配置单独创建。SQLite 读取使用只读模式；MySQL 使用单独连接。不要把 `TShock.DB` 的共享连接传给后台任务。

## 并发模型

1. 命令入口只做参数校验、任务互斥和后台调度，不在命令线程执行重活。
2. 导出和导入共享一个运行时互斥，同一时间只允许一个玩家存档任务。
3. 后台任务通过 `MainThreadQueue` 执行必须回到 Terraria/TShock 主线程的动作：
   - 向在线玩家发送网络消息。
   - 同步在线玩家 SSC 数据。
   - 导入前踢出在线玩家。
   - 导出回退方案中临时切换 `Main.ServerSideCharacter`。
4. `MainThreadQueue.Invoke` 会阻塞等待主线程抽取队列，只能从后台线程调用。
5. TShock 控制台输出可直接写日志，不应为了控制台结果依赖主线程队列。

## 必须保持的不变量

- 导出不能长时间改写 `Main.ServerSideCharacter`，也不能把回退值与失败路径留成全局状态。
- 导出在线玩家前必须先尝试把当前角色状态同步到 SSC。
- 导入前目标账号必须离线，避免旧在线会话稍后保存并覆盖导入结果。
- 覆盖已有 SSC 前必须先成功导出备份。
- 导入参数只能是 `PlayerImports` 目录下的 `.plr` 文件名，拒绝绝对路径、子目录和路径穿越。
- 导出路径必须经过文件名清洗并限制在目标目录内。
- 用户可见异常只包含短 trace ID，不包含数据库密码、连接字符串、完整内部异常或服务器绝对路径。
- 插件输出只发布 `TShockPlrExporter.dll`，TShock/Terraria/SQLite/MySQL 运行库由服务器提供。

## 变更检查清单

修改数据库字段或后端行为时：

- 更新 `Data/CharacterDatabase.cs` 和对应测试。
- 更新 [`runbooks/database-backends.md`](runbooks/database-backends.md)。
- 若影响 SSC 字段范围，更新根 README 的用户可见限制。

修改导入导出流程时：

- 检查 [`runbooks/ssc-operations.md`](runbooks/ssc-operations.md)、[`runbooks/online-players.md`](runbooks/online-players.md) 和 [`runbooks/backup-recovery.md`](runbooks/backup-recovery.md)。
- 保持路径、备份、离线和事务边界，并补充回归测试。

修改线程模型时：

- 检查 [`runbooks/main-thread-scheduling.md`](runbooks/main-thread-scheduling.md)。
- 说明新的阻塞点、超时策略和关闭/卸载行为。

调整关键取舍时：

- 在 [`decisions/`](decisions/) 新增 ADR，并链接到受影响文档。
