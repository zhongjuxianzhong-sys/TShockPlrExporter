# SSC 导入导出 Runbook

## 适用范围

用于执行、检查或排障 TShock SSC 人物存档的 `/player` 和 `/playerimport` 操作。

## 前置条件

- 服务器已加载正确的 `TShockPlrExporter.dll`。
- 启动日志包含 `[TShockPlrExporter] v1.3.2 已就绪`，或与当前发布版本一致的就绪信息。
- 执行者拥有 `plrexporter.export` 或 `plrexporter.import` 权限。
- 大批量操作前已备份整个 `tshock` 目录，尤其是数据库文件。

## 正常导出

1. 执行 `/player <账号名|账号ID|all>`。
2. 确认收到“导出任务已开始”。
3. 等待完成汇总；批量导出只发送开始、汇总和失败列表，不会逐个账号回显。
4. 从汇总中的相对目录 `tshock/PlayerExports` 检查 `.plr` 文件。
5. 在 TShock 日志中按 `[TShockPlrExporter]` 或本次 trace ID 查询绝对路径和详细异常。

导出文件名格式为 `{账号名}-{账号ID}.plr`。账号名会被清洗，ID 用于区分归一化后同名的账号。

## 正常导入

1. 把源文件放入 `tshock/PlayerImports`。
2. 执行 `/playerimport <账号名|账号ID> <文件名>`，文件名不能包含目录。
3. 如果目标账号在线，确认其被有提示地踢出；不要阻止或立即重连。
4. 等待导入完成汇总。
5. 检查 `tshock/PlayerSscBackups` 中是否生成导入前备份。
6. 让玩家重新登录并确认角色、库存、Loadout 和进度正确。

导入成功后源文件不会自动移动或删除。

## 检查表

| 检查项 | 正常结果 | 异常处理 |
| --- | --- | --- |
| 插件版本 | 日志版本与发布包一致 | 替换 `ServerPlugins` 中的旧 DLL 并重启 |
| 任务状态 | 同一时间只有一个导入或导出任务 | 等上一个任务结束；若长期不动，查看主线程调度 runbook |
| 目标账号 | 导出需要已有 `tsCharacter`；导入需要已有 `Users` 记录 | 使用账号 ID 重试，或确认账号与 SSC 状态 |
| 导入文件名 | 只有文件名，扩展名为 `.plr`，位于 `PlayerImports` | 禁止传入绝对路径、子目录、`..` 或其他扩展名 |
| 目标在线状态 | 导入前目标账号离线 | 让玩家稍后重连；若超时，检查在线玩家 runbook |
| 备份 | 覆盖已有 SSC 前存在备份 | 备份失败时不得继续覆盖，先修复目录权限或磁盘空间 |
| 输出文件 | `.plr` 存在且非空 | 查日志中的 trace ID 和绝对输出路径 |

## 失败后的下一步

- 找不到账号：确认账号存在；导出还要求 `tsCharacter` 中存在 SSC 数据。
- 找不到 SQLite 数据库：确认服务器已启动并生成数据库，检查 [`database-backends.md`](database-backends.md)。
- 导入被踢出或中止：检查 [`online-players.md`](online-players.md)。
- 原角色需要恢复：使用 [`backup-recovery.md`](backup-recovery.md) 中的流程。
- 只收到“已开始”没有结果，或任务长时间占用：检查 [`main-thread-scheduling.md`](main-thread-scheduling.md)。

## 禁止操作

- 不要在未备份数据库的情况下直接编辑 `tsCharacter`。
- 不要让在线玩家在导入完成前重新登录。
- 不要绕过 `PlayerImports` 路径校验，手工把任意路径传给插件。
- 不要把游戏内错误消息当作完整异常；完整信息只在服务器日志中。
