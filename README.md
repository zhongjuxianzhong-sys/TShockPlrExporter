# TShockPlrExporter

TShockPlrExporter 是一个用于 TShock 服务器的人物存档导入导出插件。它可以在 TShock 数据库中的服务器端人物存档（SSC）数据和 Terraria 原生 `.plr` 文件之间转换。

## 本人使用的版本

- TShock: `6.1.0`
- Terraria: `1.4.5.6` 
- 目标框架: `.NET 9.0`
- 插件版本: `1.3.2`

插件主要面向已开启服务器端人物存档（SSC）的 TShock 服务器。

## 功能

- 按账号名导出单个玩家人物存档。
- 按账号 ID 导出单个玩家人物存档。
- 一次性导出全部已有 SSC 人物存档。
- 从 `tshock/PlayerImports` 导入 `.plr` 存档并覆盖指定账号的 SSC 数据。
- 插件加载时会自动创建 `PlayerExports`、`PlayerImports`、`PlayerSscBackups` 目录。
- 导入覆盖前自动把目标账号当前的 SSC 数据备份到 `tshock/PlayerSscBackups`。
- 目标账号在线时，导入前会自动踢出该账号的在线会话，确认离线后再写入 SSC。
- 输出 Terraria 原生 `.plr` 文件。
- 支持 SQLite 与 MySQL 两种 TShock 存储后端，按 `tshock/config.json` 的配置自动选择。
- 导出前先把在线玩家的 SSC 数据同步落库，避免导出到过期内容。
- 同名文件已存在时，会先改名为带毫秒时间戳的 `.plr.bak` 备份，并只保留最新的 10 份。
- 导出完成后会检查目标文件是否存在且非空，避免误报成功。
- 导出和导入在后台线程执行，不阻塞服务器主循环；同一时间只允许一个玩家存档任务。

## 命令

```text
/player <账号名|账号ID|all>
/playerimport <账号名|账号ID> <文件名>
```

示例：

```text
/player Alice
/player 12
/player all
/playerimport Alice Alice.plr
/playerimport 12 Alice.plr
```

导出命令会立刻返回「导出任务已开始」，随后在完成时发送一条汇总结果。批量导出不会逐个账号回显，避免把执行者刷下线。

导入前需要把 `.plr` 文件放进 `tshock/PlayerImports`。导入参数只接受文件名，不接受子目录、绝对路径或路径穿越。目标账号在线时会先被踢出，导入成功后该账号重新登录即可加载新存档。

`/exportplr` 仍保留为导出的兼容别名，`/importplr` 是导入的兼容别名。

如果目标是纯数字，插件会先按账号 ID 查询；查不到时再按账号名查询，所以纯数字的账号名同样可以导出。

## 权限

权限节点：

```text
plrexporter.export
plrexporter.import
```

示例，将权限授予 `superadmin` 组：

```text
/group addperm superadmin plrexporter.export
/group addperm superadmin plrexporter.import
```

如果你的管理员组不是 `superadmin`，请替换为实际组名。

## 存档目录

导出的 `.plr` 文件会写入：

```text
tshock/PlayerExports
```

文件名格式为 `{账号名}-{账号ID}.plr`。账号名中的特殊字符会被替换为下划线，因此带上账号 ID 可以确保两个归一化后同名的账号（例如 `a/b` 与 `a_b`）不会互相覆盖，导出结果也能对回具体账号。

汇总消息里统一显示相对路径 `tshock/PlayerExports`。绝对路径会写入 TShock 日志，需要确认面板服的实际实例目录时去日志里查。

插件加载时会自动创建以下目录；如果目录不存在，也可以手动创建：

```text
tshock/PlayerExports
tshock/PlayerImports
tshock/PlayerSscBackups
```

待导入的 `.plr` 文件放在：

```text
tshock/PlayerImports
```

导入覆盖前，目标账号已有的 SSC 数据会备份为 `.plr` 文件：

```text
tshock/PlayerSscBackups
```

成功导入后，源文件仍保留在 `PlayerImports`，插件不会自动删除或移动它。

## 安装

1. 编译插件，得到 `TShockPlrExporter.dll`。
2. 将 DLL 放入 TShock 服务器的 `ServerPlugins` 目录。
3. 重启 TShock 服务器。
4. 按需给管理员组添加 `plrexporter.export`、`plrexporter.import` 权限。
5. 在服务器控制台或游戏内执行 `/player` 或 `/playerimport` 命令。

启动时插件会在控制台打印一条就绪信息，说明当前使用哪条保存路径。实现细节见 [`docs/architecture.md`](docs/architecture.md)。

编译产物只有 `TShockPlrExporter.dll` 一个文件。TShock 服务器自带的程序集（Terraria/OTAPI、Microsoft.Data.Sqlite、MySql.Data 等）不会被复制到输出目录，避免和服务器加载的版本冲突。

## 项目结构

```text
TShockPlrExporter/
├── .github/
│   └── workflows/
│       └── build.yml            GitHub Actions 构建工作流
├── docs/
│   ├── README.md                工程文档入口与路由
│   ├── architecture.md          组件边界、数据流与并发模型
│   ├── runbooks/
│   │   ├── ssc-operations.md    SSC 导入导出 Runbook
│   │   ├── online-players.md    在线玩家 Runbook
│   │   ├── database-backends.md SQLite/MySQL Runbook
│   │   ├── backup-recovery.md   备份与恢复 Runbook
│   │   └── main-thread-scheduling.md 主线程调度 Runbook
│   └── decisions/               架构决策记录（ADR）
├── Properties/
│   └── AssemblyInfo.cs          测试程序集访问内部类型的声明
├── Data/                        数据访问（namespace TShockPlrExporter.Data）
│   ├── CharacterDatabase.cs     连接 SQLite/MySQL，读取 Users 与 tsCharacter
│   ├── CharacterRecord.cs       一行 SSC 人物数据的内存表示
│   └── ExportAccount.cs         待导出的账号（ID + 用户名）
├── Exporting/                   导出实现（namespace TShockPlrExporter.Exporting）
│   ├── PlrExporter.cs           还原 Player 对象、写盘、备份与轮转
│   └── MainThreadQueue.cs       把工作项调度到 Terraria 主线程
├── Importing/                   导入实现（namespace TShockPlrExporter.Importing）
│   └── PlrImporter.cs           读取 .plr、修正危险字段并写入 SSC
├── tests/
│   └── TShockPlrExporter.Tests/ 单元测试项目
│       ├── DataCodecTests.cs    颜色、布尔数组与库存解码测试
│       ├── FileNameTests.cs     安全文件名测试
│       ├── ImportConversionTests.cs 导入数值收敛测试
│       ├── ImportPathTests.cs   导入路径安全测试
│       ├── MainThreadQueueTests.cs 主线程队列测试
│       └── TShockPlrExporter.Tests.csproj
├── Plugin.cs                    命令注册、任务编排与结果汇报
├── TShockPlrExporter.csproj     项目文件与 NuGet 依赖
├── README.md                    使用说明
├── LICENSE                      开源许可证
├── .editorconfig                代码风格配置
├── .gitattributes               Git 属性配置
└── .gitignore                   Git 忽略规则
```

除 `Plugin` 之外的类型都是 `internal`：它们是实现细节，只有 `Plugin` 需要被 TShock 反射加载。

本地构建产物位于 `bin/Release/net9.0/TShockPlrExporter.dll`；GitHub Actions 构建后会上传同名产物 `TShockPlrExporter-Release`。

## 开发者文档

维护者入口：

- [`docs/README.md`](docs/README.md)：文档分层与运行手册路由。
- [`docs/architecture.md`](docs/architecture.md)：组件边界、数据流、数据库访问、保存路径、在线玩家和并发不变量。
- [`docs/runbooks/`](docs/runbooks/)：SSC、在线玩家、SQLite/MySQL、备份恢复和主线程调度排障流程。
- [`docs/decisions/`](docs/decisions/)：关键架构取舍记录。

### 技术摘要

TShock 的 SSC 人物数据保存在 `tsCharacter`。插件在后台线程读取或写入角色数据，只在发送在线玩家消息、同步在线 SSC、踢出导入目标或执行保存回退时回到 Terraria 主线程。覆盖已有 SSC 前会先备份；导入要求目标账号离线；导出结果会校验文件存在且非空。完整实现约束与排障流程见上面的开发者文档入口。

## 导出内容范围

插件会尽量导出 TShock 6.1.0 在 `tsCharacter` 中保存的内容，包括：

- 生命、最大生命、魔力、最大魔力
- 背包、钱币、弹药、装备、染料、饰品
- 猪猪储蓄罐、保险箱、护卫熔炉、虚空袋
- 垃圾槽
- 三套 Loadout
- 外观颜色、发型、发色、皮肤、声音参数
- 饰品隐藏状态
- 渔夫任务次数
- 部分永久增益状态
- PVE/PVP 死亡次数

## 限制

- 只能导出 TShock 已保存到 `tsCharacter` 的服务器端人物数据。
- 如果某些客户端本地状态从未被 TShock 保存，插件无法凭空还原。
- 导入只会读取 `tshock/PlayerImports` 下的文件名，不提供网页上传或任意服务器路径导入。
- 导入不会创建 TShock 账号，目标账号必须已经存在于 `Users` 表中。
- 导出和导入只会处理人物数据及必要的在线会话；插件不会改动账号密码、权限、UUID、区域等其他内容。
- 每个账号最多保留 10 份 `.plr.bak` 备份，更旧的会在导出时被删除。如需长期留存，请自行归档。
- 建议执行批量导出或导入前备份整个 `tshock` 目录，尤其是数据库文件。

## 常见问题

### 只显示「导出任务已开始」，没有完成提示

1.1.1 起控制台的结果消息直接写控制台和日志，不再依赖游戏主循环，正常情况下必定会出现。如果仍然只
有开始提示，按下面的顺序排查：

- 确认服务器加载的是新版本。启动信息里会打印 `[TShockPlrExporter] v1.3.2 已就绪`，看不到版本号说明
  `ServerPlugins` 里还是旧 DLL。
- 在 TShock 日志里搜索 `[TShockPlrExporter]`。汇总结果无条件写日志，导出成功、失败、异常都能在这里看到。
- 再执行一次 `/player` 或 `/playerimport`。如果提示「已有玩家存档任务正在执行（已运行 N 秒）」，说明上一个任务还卡着，
  N 就是它已经卡了多久；数据库查询有 30 秒超时，超过这个时长仍不结束的话请把日志发出来。

### 命令提示成功但找不到文件

在 TShock 日志里搜索 `[TShockPlrExporter]`，导出完成的那条日志带着输出目录的绝对路径。面板服的实际实例目录经常不是你当前看到的目录。

### 提示没有匹配账号

确认账号已经注册，并且该账号存在 SSC 人物数据。可以尝试使用账号 ID：

```text
/player 12
```

### 提示「已有导出任务正在执行」

同一时间只允许一个导出或导入任务。等上一个任务的汇总消息出现后再重试。

### 导出失败，提示查看日志编号

游戏内的错误消息不包含具体异常信息和服务器路径，只给一个编号（例如 `编号 3f2a1b9c`）。在 TShock 日志里搜索该编号即可看到完整异常。

### 批量导出失败一部分账号

命令会继续导出其他账号，并在结果中显示失败数量与前几个失败账号。详细异常会写入 TShock 日志，搜索 `[TShockPlrExporter]` 或本次导出的编号。

### 导入时报找不到文件

确认 `.plr` 文件已经放在 `tshock/PlayerImports`，并且导入参数只写文件名。例如：

```text
/playerimport Alice Alice.plr
```

不要传 `PlayerImports/Alice.plr`、绝对路径或 `..`。

### 导入时目标玩家被踢出

这是预期行为。在线玩家的旧会话可能覆盖新导入的 SSC，因此插件会先踢出目标账号，确认离线后才会写库。目标玩家重新登录后会加载导入后的角色。

### 导入失败后原角色是否还在

只要目标账号已有 SSC 数据，覆盖前会先备份到 `tshock/PlayerSscBackups`。如果导入过程失败，原数据库数据不会被修改；如需恢复，可将备份 `.plr` 重新放回 `PlayerImports` 后再导入。
