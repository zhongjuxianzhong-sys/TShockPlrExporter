# Security Policy

## 支持的版本

当前仅维护最新发布版本。旧版本可能不会收到安全修复。

| Version | Supported |
| --- | --- |
| 1.3.x | :white_check_mark: |
| <= 1.2.1 | :x: |

## 报告安全漏洞

请通过 GitHub Private Vulnerability Reporting 私下报告安全问题：

https://github.com/zhongjuxianzhong-sys/TShockPlrExporter/security/advisories/new

不要为安全问题创建公开 Issue、Discussion 或 Pull Request。公开披露前，请先给维护者修复和发布新版本的时间。

如果 GitHub 私密报告不可用，请先联系仓库维护者，并在获得安全渠道后再发送详细信息。

## 报告内容

报告中建议包含：

- 受影响的插件版本和 TShock/Terraria 版本
- 操作系统、数据库后端和部署方式
- 漏洞类型和可能影响
- 最小、可重复的复现步骤
- 相关日志或请求，但必须删除敏感信息
- 如果已有修复建议，可以一并说明

不要在报告中发送：

- 数据库密码、连接字符串或服务器密钥
- 完整 `tshock.sqlite`、MySQL 数据导出或玩家隐私数据
- 未脱敏的服务器绝对路径
- 可被第三方直接利用的完整攻击脚本

## 安全范围

以下问题属于本项目关注范围：

- 任意文件读取、写入或路径穿越
- `PlayerImports` 文件名校验绕过
- 导出文件越出 `tshock/PlayerExports`
- SQL 注入或数据库参数处理错误
- 数据库密码、连接字符串或敏感配置泄露
- 权限绕过，导致未授权用户执行导入或导出
- 导入或导出流程破坏 SSC 数据完整性
- 在线玩家处理不当，导致旧会话覆盖导入后的 SSC
- 备份创建失败后仍覆盖原人物数据
- 插件卸载、错误和异常路径中的敏感信息泄露

以下问题通常不属于本项目范围：

- TShock、Terraria、SQLite、MySQL 或服务器面板自身漏洞
- 已经取得服务器管理员权限后的本地破坏行为
- 不安装推荐更新、使用已停止维护版本导致的问题
- 由第三方插件、修改版 DLL 或未知来源插件造成的问题
- 未脱敏日志、数据库或备份文件被管理员主动公开

## 安全模型

TShockPlrExporter 是服务器管理员使用的插件：

- 导入和导出命令应由 `plrexporter.import`、`plrexporter.export` 权限保护。
- 插件只应访问 TShock 配置允许的数据库和 `TShock.SavePath` 下的目录。
- 导入文件只能来自 `tshock/PlayerImports`，不能接受任意服务器路径。
- 数据库连接信息来自 TShock 配置，不应写入插件日志或玩家消息。
- `PlayerExports` 和 `PlayerSscBackups` 可能包含完整人物数据，应视为敏感文件。
- 插件不负责保护已经被入侵的服务器、TShock 进程或操作系统账户。

## 处理流程

收到安全报告后，维护者会：

1. 确认问题和影响范围。
2. 在私密渠道中复现并评估风险。
3. 准备修复、测试和发布新版本。
4. 与报告者协调公开披露时间。
5. 在安全公告中说明受影响版本、修复版本和必要的规避措施。

请不要在修复发布前公开完整利用细节。

## 运维建议

- 定期更新到最新版本。
- 执行批量导入、导出或恢复前备份整个 `tshock` 目录。
- 限制 `ServerPlugins`、`PlayerImports`、`PlayerExports`、`PlayerSscBackups` 的文件权限。
- 不要把数据库连接字符串、备份文件或完整日志上传到公开平台。
- 更多运行与恢复流程见 `docs/runbooks/`。
