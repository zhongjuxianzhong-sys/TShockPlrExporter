# Contributing to TShockPlrExporter

感谢参与 TShockPlrExporter。提交代码前，请先确认改动范围、兼容版本和安全边界。

## 适用范围

本项目面向：

- TShock `6.1.0`
- Terraria `1.4.5.6`
- .NET `9.0`
- SQLite 和 MySQL 存储后端

不接受未经说明的大规模重构、无关依赖升级或破坏旧配置兼容性的改动。

## 开始之前

- 先搜索现有 Issue 和 Pull Request，避免重复工作。
- 大幅修改架构、数据库字段、导入导出行为或版本兼容性前，请先创建 Issue 讨论方案。
- 安全问题不要公开提交 Issue，请按照 [SECURITY.md](SECURITY.md) 私下报告。
- AI 编码工具应同时阅读 [AGENTS.md](AGENTS.md)。

## 开发环境

- Windows 11
- PowerShell
- .NET 9 SDK
- 可访问 NuGet
- 单元测试不要求启动 TShock 服务器，但涉及 TShock/Terraria 运行时行为时，需要在 TShock 6.1.0 环境验证

## 构建与测试

在仓库根目录执行：

```powershell
dotnet restore TShockPlrExporter.csproj
dotnet build TShockPlrExporter.csproj -c Release --no-restore --nologo
dotnet test tests/TShockPlrExporter.Tests/TShockPlrExporter.Tests.csproj -c Release --nologo
