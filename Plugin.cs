using Microsoft.Data.Sqlite;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockPlrExporter;

[ApiVersion(2, 1)]
public sealed class Plugin : TerrariaPlugin
{
    private readonly PlrExporter exporter = new();
    private Command? exportCommand;

    public override string Name => "TShockPlrExporter";
    public override string Author => "TShockPlrExporter Contributors";
    public override string Description => "将 TShock 的 SSC 人物数据导出为 Terraria .plr 文件。";
    public override Version Version => new(1, 0, 0);

    public Plugin(Main game) : base(game)
    {
    }

    public override void Initialize()
    {
        exportCommand = new Command("plrexporter.export", ExportCommand, "exportplr")
        {
            HelpText = "导出 SSC 人物存档：/exportplr <账号名|账号 ID|all>"
        };

        Commands.ChatCommands.Add(exportCommand);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (exportCommand is not null)
            {
                Commands.ChatCommands.Remove(exportCommand);
                exportCommand = null;
            }
        }

        base.Dispose(disposing);
    }

    private void ExportCommand(CommandArgs args)
    {
        if (args.Parameters.Count != 1)
        {
            args.Player.SendErrorMessage("用法：/exportplr <账号名|账号 ID|all>");
            return;
        }

        try
        {
            string target = args.Parameters[0];
            string savePath = Path.GetFullPath(TShock.SavePath);
            string databasePath = Path.Combine(savePath, "tshock.sqlite");
            string exportPath = Path.Combine(savePath, "PlayerExports");

            if (!File.Exists(databasePath))
            {
                args.Player.SendErrorMessage($"未找到数据库文件：{databasePath}");
                return;
            }

            Directory.CreateDirectory(exportPath);
            using SqliteConnection connection = OpenReadOnly(databasePath);

            IReadOnlyList<ExportAccount> accounts = target.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? exporter.GetAllAccounts(connection)
                : exporter.FindAccounts(connection, target);

            if (accounts.Count == 0)
            {
                args.Player.SendErrorMessage($"未找到与“{target}”匹配且有 SSC 人物数据的账号。");
                return;
            }

            int success = 0;
            List<string> failures = new();
            foreach (ExportAccount account in accounts)
            {
                try
                {
                    string path = exporter.Export(connection, account, exportPath);
                    success++;
                    args.Player.SendSuccessMessage($"已导出 {account.Name}：{Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{account.Name}: {ex.Message}");
                    TShock.Log.Error($"[TShockPlrExporter] 导出账号 {account.Id}（{account.Name}）失败：{ex}");
                }
            }

            Color color = failures.Count == 0 ? Color.Green : Color.Yellow;
            args.Player.SendMessage($"导出完成：成功 {success} 个，失败 {failures.Count} 个。目录：{exportPath}", color);

            foreach (string failure in failures.Take(5))
            {
                args.Player.SendErrorMessage(failure);
            }
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[TShockPlrExporter] 执行导出命令失败：{ex}");
            args.Player.SendErrorMessage($"导出失败：{ex.Message}");
        }
    }

    private static SqliteConnection OpenReadOnly(string databasePath)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }
}