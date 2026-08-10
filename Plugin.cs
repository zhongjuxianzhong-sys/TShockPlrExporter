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
    public override string Author => "Codex";
    public override string Description => "Exports TShock server-side character data to Terraria .plr files.";
    public override Version Version => new(1, 0, 0);

    public Plugin(Main game) : base(game)
    {
    }

    public override void Initialize()
    {
        exportCommand = new Command("plrexporter.export", ExportCommand, "exportplr")
        {
            HelpText = "Exports TShock server-side characters to .plr files. Usage: /exportplr <accountName|accountId|all>"
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
            args.Player.SendErrorMessage("Usage: /exportplr <accountName|accountId|all>");
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
                args.Player.SendErrorMessage($"Database not found: {databasePath}");
                return;
            }

            Directory.CreateDirectory(exportPath);
            using SqliteConnection connection = OpenReadOnly(databasePath);

            IReadOnlyList<ExportAccount> accounts = target.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? exporter.GetAllAccounts(connection)
                : exporter.FindAccounts(connection, target);

            if (accounts.Count == 0)
            {
                args.Player.SendErrorMessage($"No account with character data matched '{target}'.");
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
                    args.Player.SendSuccessMessage($"Exported {account.Name} -> {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{account.Name}: {ex.Message}");
                    TShock.Log.Error($"[TShockPlrExporter] Failed to export account {account.Id} ({account.Name}): {ex}");
                }
            }

            Color color = failures.Count == 0 ? Color.Green : Color.Yellow;
            args.Player.SendMessage($"Export complete: {success} succeeded, {failures.Count} failed. Output: {exportPath}", color);

            foreach (string failure in failures.Take(5))
            {
                args.Player.SendErrorMessage(failure);
            }
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[TShockPlrExporter] Export command failed: {ex}");
            args.Player.SendErrorMessage($"Export failed: {ex.Message}");
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