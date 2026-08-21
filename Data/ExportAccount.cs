namespace TShockPlrExporter.Data;

/// <summary>一个待导出的账号：Users 表里的 ID 与用户名。</summary>
internal sealed record ExportAccount(int Id, string Name);
