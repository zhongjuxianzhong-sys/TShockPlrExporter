# TShockPlrExporter

TShockPlrExporter 是一个用于 TShock 服务器的导出插件。它会读取 `tshock.sqlite` 中的服务器端人物存档数据，并导出为 Terraria 原生 `.plr` 文件。

## 适用版本

- TShock: `6.1.0`
- Terraria: `1.4.5.6`
- 目标框架: `.NET 9.0`

插件主要面向已开启服务器端人物存档（SSC）的 TShock 服务器。

## 功能

- 按账号名导出单个玩家人物存档。
- 按账号 ID 导出单个玩家人物存档。
- 一次性导出全部已有 SSC 人物存档。
- 输出 Terraria 原生 `.plr` 文件。
- 同名文件已存在时，会先创建带时间戳的 `.plr.bak` 备份。
- 导出完成后会检查目标文件是否存在且非空，避免误报成功。

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

插件运行时会将该目录转换为绝对路径，并在命令结果中显示，例如：

```text
Export complete: 1 succeeded, 0 failed. Output: /path/to/server/tshock/PlayerExports
```

在 Linux 面板服或 MCSManager 环境中，请以命令输出的绝对路径为准。

## 安装

1. 编译插件，得到 `TShockPlrExporter.dll`。
2. 将 DLL 放入 TShock 服务器的 `ServerPlugins` 目录。
3. 重启 TShock 服务器。
4. 给管理员组添加 `plrexporter.export` 权限。
5. 在服务器控制台或游戏内执行 `/exportplr` 命令。

## 编译

需要安装 .NET 9 SDK。

在项目目录执行：

```powershell
dotnet build -c Release
```

编译产物位于：

```text
bin/Release/net9.0/TShockPlrExporter.dll
```

## 工作原理

TShock 的 SSC 人物数据保存在 `tshock.sqlite` 的 `tsCharacter` 表中。插件会读取账号表 `Users` 与人物表 `tsCharacter`，将数据库中的生命、魔力、外观、背包、护甲、染料、银行、虚空袋、Loadout 等字段还原到 `Terraria.Player` 对象中，然后调用 Terraria 自带的 `.plr` 保存逻辑生成文件。

由于 TShock 开启 SSC 时，Terraria 默认会跳过普通玩家文件保存，插件会在保存单个 `.plr` 的短暂临界区内临时关闭 `Main.ServerSideCharacter`，调用保存方法后立即恢复原值。该操作只用于导出文件，不会修改 `tshock.sqlite`。

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
- 插件不会修改 `tshock.sqlite`，只读数据库并写出 `.plr` 文件。
- 建议导出前备份整个 `tshock` 目录，尤其是 `tshock.sqlite`。

## 常见问题

### 命令提示成功但找不到文件

请查看命令最后输出的 `Output:` 绝对路径。面板服的实际服务器目录经常不是你当前看到的目录。

### 提示没有匹配账号

确认账号已经注册，并且该账号存在 SSC 人物数据。可以尝试使用账号 ID：

```text
/exportplr 12
```

### 导出的文件无法在客户端看到

确认文件扩展名是 `.plr`，并将它放到 Terraria 客户端的 Players 目录。不同平台的 Players 目录位置不同。

### 批量导出失败一部分账号

命令会继续导出其他账号，并在结果中显示失败数量。详细异常会写入 TShock 日志，搜索 `[TShockPlrExporter]`。