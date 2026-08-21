# TShockPlrExporter

TShockPlrExporter 是一个用于 TShock 服务器的导出插件。它会读取 TShock 数据库中的服务器端人物存档（SSC）数据，并导出为 Terraria 原生 `.plr` 文件。

## 适用版本

- TShock: `6.1.0`
- Terraria: `1.4.5.6`
- 目标框架: `.NET 9.0`
- 插件版本: `1.1.2`

插件主要面向已开启服务器端人物存档（SSC）的 TShock 服务器。

## 功能

- 按账号名导出单个玩家人物存档。
- 按账号 ID 导出单个玩家人物存档。
- 一次性导出全部已有 SSC 人物存档。
- 输出 Terraria 原生 `.plr` 文件。
- 支持 SQLite 与 MySQL 两种 TShock 存储后端，按 `tshock/config.json` 的配置自动选择。
- 导出前先把在线玩家的 SSC 数据同步落库，避免导出到过期内容。
- 同名文件已存在时，会先改名为带毫秒时间戳的 `.plr.bak` 备份，并只保留最新的 10 份。
- 导出完成后会检查目标文件是否存在且非空，避免误报成功。
- 导出在后台线程执行，不阻塞服务器主循环；同一时间只允许一个导出任务。

## 命令

```text
/exportplr <账号名|账号ID|all>
```

示例：

```text
/exportplr Alice
/exportplr 12
/exportplr all
```

命令会立刻返回「导出任务已开始」，随后在完成时发送一条汇总结果。批量导出不会逐个账号回显，避免把执行者刷下线。

如果目标是纯数字，插件会先按账号 ID 查询；查不到时再按账号名查询，所以纯数字的账号名同样可以导出。

## 权限

权限节点：

```text
plrexporter.export
```

示例，将权限授予 `superadmin` 组：

```text
/group addperm superadmin plrexporter.export
```

如果你的管理员组不是 `superadmin`，请替换为实际组名。

## 输出目录

导出的 `.plr` 文件会写入：

```text
tshock/PlayerExports
```

文件名格式为 `{账号名}-{账号ID}.plr`。账号名中的特殊字符会被替换为下划线，因此带上账号 ID 可以确保两个归一化后同名的账号（例如 `a/b` 与 `a_b`）不会互相覆盖，导出结果也能对回具体账号。

汇总消息里统一显示相对路径 `tshock/PlayerExports`。绝对路径会写入 TShock 日志，需要确认面板服的实际实例目录时去日志里查。

## 安装

1. 编译插件，得到 `TShockPlrExporter.dll`。
2. 将 DLL 放入 TShock 服务器的 `ServerPlugins` 目录。
3. 重启 TShock 服务器。
4. 给管理员组添加 `plrexporter.export` 权限。
5. 在服务器控制台或游戏内执行 `/exportplr` 命令。

启动时插件会在控制台打印一条就绪信息，说明当前使用哪条保存路径（见下文「工作原理」）。

编译产物只有 `TShockPlrExporter.dll` 一个文件。TShock 服务器自带的程序集（Terraria/OTAPI、Microsoft.Data.Sqlite、MySql.Data 等）不会被复制到输出目录，避免和服务器加载的版本冲突。

## 项目结构

```text
TShockPlrExporter/
├── Plugin.cs                    命令注册、任务编排与结果汇报
├── Data/                        数据访问（namespace TShockPlrExporter.Data）
│   ├── CharacterDatabase.cs     连接 SQLite/MySQL，读取 Users 与 tsCharacter
│   ├── CharacterRecord.cs       一行 SSC 人物数据的内存表示
│   └── ExportAccount.cs         待导出的账号（ID + 用户名）
├── Exporting/                   导出实现（namespace TShockPlrExporter.Exporting）
│   ├── PlrExporter.cs           还原 Player 对象、写盘、备份与轮转
│   └── MainThreadQueue.cs       把工作项调度到 Terraria 主线程
├── TShockPlrExporter.csproj
├── .editorconfig
└── README.md
```

除 `Plugin` 之外的类型都是 `internal`：它们是实现细节，只有 `Plugin` 需要被 TShock 反射加载。

## 工作原理

TShock 的 SSC 人物数据保存在数据库的 `tsCharacter` 表中。插件读取账号表 `Users` 与人物表 `tsCharacter`，将数据库中的生命、魔力、外观、背包、护甲、染料、银行、虚空袋、Loadout 等字段还原到 `Terraria.Player` 对象中，然后调用 Terraria 自带的 `.plr` 保存逻辑生成文件。

### 数据库访问

插件不复用 TShock 自己的数据库连接：ADO.NET 连接不是线程安全的，而导出运行在后台线程，共用连接会与服务器自身的查询相互干扰。插件按 TShock 的配置另开一个专用连接，并且只发 `SELECT`。SQLite 连接以只读模式打开。

`tsCharacter` 的列在 TShock 版本之间会增减，插件按列名而不是固定序号取值，缺列时退化为默认值，并在日志里提示一次缺少哪些列。

### 保存路径

TShock 开启 SSC 时，Terraria 的公开保存入口 `Player.SavePlayer` 会跳过普通玩家文件保存。插件优先通过反射调用 Terraria 的内部写盘方法，绕过这个判断，**完全不改动 `Main.ServerSideCharacter`** —— 那是全服共享状态，导出期间把它置为 `false` 会让服务器其他逻辑误判 SSC 已关闭。

只有在当前 Terraria 版本上找不到该内部方法、或调用后没有产出文件时，插件才会降级为回退方案：在主线程的一个极短临界区内临时关闭 `Main.ServerSideCharacter`，调用公开保存方法后立即恢复原值。降级发生时控制台会打印警告，此时建议在无人在线时导出。

两条路径都只写出 `.plr` 文件，不会修改 TShock 数据库。

### 在线玩家

在线玩家的 SSC 数据只有在特定时机才会落库，直接读 `tsCharacter` 拿到的是上一次保存的旧状态。导出前插件会先把本次涉及的在线玩家数据写一次库，避免命令报「成功」而文件里是过期内容。这一步失败时会记录警告并继续导出。

### 数值收敛

`skinVariant`、`hair`、`team`、`currentLoadoutIndex` 这几个字段会在客户端读档时被当作数组下标使用，数据库里的异常值会直接让客户端崩在加载阶段。插件按运行时的实际上界收敛这些值，并在日志中记录被修正的账号与原值。

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
- 插件不会修改 TShock 数据库，只读数据库并写出 `.plr` 文件。
- 每个账号最多保留 10 份 `.plr.bak` 备份，更旧的会在导出时被删除。如需长期留存，请自行归档。
- 建议导出前备份整个 `tshock` 目录，尤其是数据库文件。

## 常见问题

### 只显示「导出任务已开始」，没有完成提示

1.1.1 起控制台的结果消息直接写控制台和日志，不再依赖游戏主循环，正常情况下必定会出现。如果仍然只
有开始提示，按下面的顺序排查：

- 确认服务器加载的是新版本。启动信息里会打印 `[TShockPlrExporter] v1.1.2 已就绪`，看不到版本号说明
  `ServerPlugins` 里还是旧 DLL。
- 在 TShock 日志里搜索 `[TShockPlrExporter]`。汇总结果无条件写日志，导出成功、失败、异常都能在这里看到。
- 再执行一次 `/exportplr`。如果提示「已有导出任务正在执行（已运行 N 秒）」，说明上一个任务还卡着，
  N 就是它已经卡了多久；数据库查询有 30 秒超时，超过这个时长仍不结束的话请把日志发出来。

### 命令提示成功但找不到文件

在 TShock 日志里搜索 `[TShockPlrExporter]`，导出完成的那条日志带着输出目录的绝对路径。面板服的实际实例目录经常不是你当前看到的目录。

### 提示没有匹配账号

确认账号已经注册，并且该账号存在 SSC 人物数据。可以尝试使用账号 ID：

```text
/exportplr 12
```

### 提示「已有导出任务正在执行」

同一时间只允许一个导出任务。等上一个任务的汇总消息出现后再重试。

### 导出失败，提示查看日志编号

游戏内的错误消息不包含具体异常信息和服务器路径，只给一个编号（例如 `编号 3f2a1b9c`）。在 TShock 日志里搜索该编号即可看到完整异常。

### 批量导出失败一部分账号

命令会继续导出其他账号，并在结果中显示失败数量与前几个失败账号。详细异常会写入 TShock 日志，搜索 `[TShockPlrExporter]` 或本次导出的编号。

