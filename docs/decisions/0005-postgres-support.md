# 0005 支持 PostgreSQL 存储后端

- 状态：已接受
- 日期：2026-10-01
- 取代：无

## 背景

TShock `6.2` 起在 SQLite 与 MySQL 之外新增 PostgreSQL 后端（`StorageType` 为 `postgres`，配置项为 `PostgresConnectionString` 或 `PostgresHost`、`PostgresDbName`、`PostgresUsername`、`PostgresPassword`）。插件原先只识别 sqlite 与 mysql，在 PostgreSQL 服务器上会直接以「不支持的存储类型」失败，导入导出完全不可用。

PostgreSQL 与另外两个后端有两处关键差异：

- TShock 建表时表名带双引号（`"Users"`、`"tsCharacter"`），保留大小写；未加引号的同名标识符会被折叠成小写，从而找不到表。
- 列名建表时未加引号，双方都折叠成小写后匹配。

## 决策

- 新增 `StorageBackend` 枚举，插件按 `StorageType` 选择 SQLite、MySQL 或 PostgreSQL。
- 连接使用 Npgsql，与 MySql.Data 一致地以 `ExcludeAssets="runtime;contentFiles"`、`PrivateAssets="all"` 引用，运行库由 TShock 服务器提供，不复制进插件输出目录。
- 连接字符串优先使用 `PostgresConnectionString`，否则由 `PostgresHost`（可带端口，默认 5432）与库名、账号、密码拼装，行为与 TShock 自身的 `DbBuilder` 保持一致。
- SQL 中的表名按后端引用：PostgreSQL 用双引号，SQLite 与 MySQL 保持原有写法。列名与参数名不做转换。
- 仍然使用独立 ADO.NET 连接，不复用 `TShock.DB`。

## 后果

- PostgreSQL 服务器上导入导出可用，行为与另外两个后端一致。
- 插件多一个编译期依赖（Npgsql）；服务器必须自带该程序集（TShock 6.2 的发布包已包含）。
- 表名引用逻辑必须与 TShock 的建表方式保持同步：TShock 若改变表名大小写策略，插件的查询会失败，需要同步调整 `CharacterDatabase.QuoteIdentifier`。
