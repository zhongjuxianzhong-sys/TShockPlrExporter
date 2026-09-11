# TShockPlrExporter 文档索引

本目录存放面向维护者和服务器运维人员的工程文档。根目录 `README.md` 只保留用户安装、命令、目录、限制和 FAQ，深层实现与故障处理应放在本目录。

## 文档分层

| 文档 | 面向对象 | 用途 |
| --- | --- | --- |
| [`../README.md`](../README.md) | 服务器管理员、普通使用者 | 安装、命令、权限、目录、限制和常见问题 |
| [`architecture.md`](architecture.md) | 插件维护者 | 组件边界、数据流、并发模型、兼容性和安全不变量 |
| [`runbooks/`](runbooks/) | 服务器运维人员、值班维护者 | 按风险场景执行诊断、恢复和验证 |
| [`decisions/`](decisions/) | 插件维护者 | 记录关键架构取舍及其后果 |

## 运行手册路由

| 场景 | 文档 |
| --- | --- |
| SSC 人物存档导入、导出和结果校验 | [`runbooks/ssc-operations.md`](runbooks/ssc-operations.md) |
| 在线玩家导致数据过期、被踢出或导入被中止 | [`runbooks/online-players.md`](runbooks/online-players.md) |
| SQLite/MySQL 配置、连接失败和超时 | [`runbooks/database-backends.md`](runbooks/database-backends.md) |
| 导出备份、导入前备份和误覆盖恢复 | [`runbooks/backup-recovery.md`](runbooks/backup-recovery.md) |
| 主线程队列停摆、任务卡住、玩家收不到结果 | [`runbooks/main-thread-scheduling.md`](runbooks/main-thread-scheduling.md) |

## 维护规则

- 源码和测试是行为事实来源。行为变化时，在同一个变更中更新对应架构、运行手册和决策记录。
- 只有会改变安装、命令、权限、玩家可见文案或兼容范围的内容才更新根 README。
- 新的排障流程优先补进现有 runbook；不要继续把阶段性排障细节堆进 README。
- 运行手册至少包含适用范围、前置条件、操作步骤、安全边界、验证方式和失败后的下一步。
- 已接受的决策记录原则上不改写历史。需要调整时新增一条 ADR，并写明它取代了哪一条。
