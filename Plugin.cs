using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace TShockPlrExporter;

[ApiVersion(2, 1)]
public sealed class Plugin : TerrariaPlugin
{
    /// <summary>聊天里最多列出多少个失败账号，其余只写日志，避免批量导出时刷屏。</summary>
    private const int MaxListedFailures = 5;

    private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(15);

    private readonly MainThreadQueue mainThread = new();
    private readonly PlrExporter exporter;
    private readonly CancellationTokenSource shutdown = new();

    private Command? exportCommand;
    private int exportRunning;

    public override string Name => "TShockPlrExporter";
    public override string Author => "TShockPlrExporter Contributors";
    public override string Description => "将 TShock 的 SSC 人物数据导出为 Terraria .plr 文件。";
    public override Version Version => new(1, 1, 0);

    public Plugin(Main game) : base(game)
    {
        exporter = new PlrExporter(mainThread);
    }

    public override void Initialize()
    {
        exportCommand = new Command("plrexporter.export", ExportCommand, "exportplr")
        {
            HelpText = "导出 SSC 人物存档：/exportplr <账号名|账号 ID|all>"
        };

        Commands.ChatCommands.Add(exportCommand);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

        TShock.Log.ConsoleInfo(PlrExporter.UsesInternalSave
            ? "[TShockPlrExporter] 已就绪，导出过程不会改动服务器的 SSC 状态。"
            : "[TShockPlrExporter] 警告：未找到 Terraria 内部保存接口，将回退为临时切换 SSC 模式，" +
              "建议在无人在线时导出。");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            shutdown.Cancel();
            mainThread.Shutdown();
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);

            if (exportCommand is not null)
            {
                Commands.ChatCommands.Remove(exportCommand);
                exportCommand = null;
            }

            shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnGameUpdate(EventArgs args)
    {
        mainThread.Drain();
    }

    private void ExportCommand(CommandArgs args)
    {
        if (args.Parameters.Count != 1)
        {
            args.Player.SendErrorMessage("用法：/exportplr <账号名|账号 ID|all>");
            return;
        }

        // 同一时间只允许一个导出任务：批量导出会构造大量 Player 对象并写盘，
        // 并发跑没有收益，只会放大磁盘压力和备份轮转的竞争。
        if (Interlocked.CompareExchange(ref exportRunning, 1, 0) != 0)
        {
            args.Player.SendErrorMessage("已有导出任务正在执行，请等它结束后再试。");
            return;
        }

        TSPlayer requester = args.Player;
        string target = args.Parameters[0];

        requester.SendInfoMessage("导出任务已开始，完成后会在这里汇总结果。");

        // 放到后台线程：批量导出的 SQL 查询、Player 构造和写盘都是同步操作，
        // 留在命令线程上会卡住服务器（控制台命令线程或客户端封包线程）。
        _ = Task.Run(() => RunExport(requester, target), CancellationToken.None)
            .ContinueWith(_ => Interlocked.Exchange(ref exportRunning, 0), TaskScheduler.Default);
    }

    private void RunExport(TSPlayer requester, string target)
    {
        // 给这次导出一个短编号：聊天里只报编号，详细异常留在日志里，
        // 避免把服务器绝对路径和内部异常信息直接发给玩家。
        string traceId = Guid.NewGuid().ToString("N")[..8];

        try
        {
            string exportRoot = Path.Combine(Path.GetFullPath(TShock.SavePath), "PlayerExports");
            Directory.CreateDirectory(exportRoot);

            using CharacterDatabase database = CharacterDatabase.Open();

            IReadOnlyList<ExportAccount> accounts = target.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? database.GetAllAccounts()
                : database.FindAccounts(target);

            if (accounts.Count == 0)
            {
                Notify(requester, $"未找到与“{target}”匹配且有 SSC 人物数据的账号。", Color.Red);
                return;
            }

            FlushOnlineCharacters(accounts, traceId);
            ExportAll(requester, database, accounts, exportRoot, traceId);
        }
        catch (FileNotFoundException)
        {
            Notify(
                requester,
                "未找到 TShock 的 SQLite 数据库（tshock/tshock.sqlite）。请确认服务器已经启动过并生成了数据库。",
                Color.Red);
        }
        catch (Exception ex)
        {
            TShock.Log.Error($"[TShockPlrExporter][{traceId}] 执行导出命令失败：{ex}");
            Notify(requester, $"导出失败，详情见服务器日志（编号 {traceId}）。", Color.Red);
        }
    }

    private void ExportAll(
        TSPlayer requester,
        CharacterDatabase database,
        IReadOnlyList<ExportAccount> accounts,
        string exportRoot,
        string traceId)
    {
        int success = 0;
        List<string> failures = new();

        foreach (ExportAccount account in accounts)
        {
            if (shutdown.IsCancellationRequested)
            {
                TShock.Log.Warn($"[TShockPlrExporter][{traceId}] 插件正在卸载，导出提前结束。");
                break;
            }

            try
            {
                string path = exporter.Export(database, account, exportRoot);
                success++;
                TShock.Log.Info(
                    $"[TShockPlrExporter][{traceId}] 已导出账号 {account.Id}（{account.Name}）→ {path}");
            }
            catch (Exception ex)
            {
                failures.Add($"{account.Name}（ID {account.Id}）");
                TShock.Log.Error(
                    $"[TShockPlrExporter][{traceId}] 导出账号 {account.Id}（{account.Name}）失败：{ex}");
            }
        }

        // 结果只发两条消息，而不是每个账号一条：批量导出时逐条回显会把执行者刷下线。
        // 绝对路径只给控制台和日志，游戏内玩家看到的是相对路径。
        string displayPath = requester == TSPlayer.Server ? exportRoot : "tshock/PlayerExports";
        Color color = failures.Count == 0 ? Color.Green : Color.Yellow;

        Notify(
            requester,
            $"导出完成：成功 {success} 个，失败 {failures.Count} 个。目录：{displayPath}（编号 {traceId}）",
            color);

        if (failures.Count != 0)
        {
            string listed = string.Join("、", failures.Take(MaxListedFailures));
            string more = failures.Count > MaxListedFailures ? $" 等共 {failures.Count} 个" : string.Empty;
            Notify(requester, $"失败账号：{listed}{more}。详情见服务器日志（编号 {traceId}）。", Color.Red);
        }
    }

    /// <summary>
    /// 在线玩家的 SSC 数据只有在特定时机才会落库，直接读 tsCharacter 拿到的是上一次保存的旧状态。
    /// 导出前先把这些玩家的数据写一次，否则命令会报「成功」，而文件里是过期内容。
    /// </summary>
    private void FlushOnlineCharacters(IReadOnlyList<ExportAccount> accounts, string traceId)
    {
        HashSet<int> targetIds = accounts.Select(account => account.Id).ToHashSet();

        List<TSPlayer> online = TShock.Players
            .Where(player => player is { Active: true, IsLoggedIn: true }
                && player.Account is not null
                && targetIds.Contains(player.Account.ID))
            .ToList();

        if (online.Count == 0)
        {
            return;
        }

        try
        {
            mainThread.Invoke(
                () =>
                {
                    foreach (TSPlayer player in online)
                    {
                        if (player.Active && player.Account is not null)
                        {
                            TShock.CharacterDB.InsertPlayerData(player);
                        }
                    }
                },
                MainThreadTimeout);

            TShock.Log.Info(
                $"[TShockPlrExporter][{traceId}] 已先同步 {online.Count} 名在线玩家的 SSC 数据：" +
                string.Join("、", online.Select(player => player.Name)));
        }
        catch (Exception ex)
        {
            TShock.Log.Warn(
                $"[TShockPlrExporter][{traceId}] 同步在线玩家 SSC 数据失败，导出的可能是较旧的数据：{ex.Message}");
        }
    }

    /// <summary>把消息排到主线程发送，并跳过已经下线的玩家。</summary>
    private void Notify(TSPlayer requester, string message, Color color)
    {
        mainThread.Enqueue(() =>
        {
            if (requester != TSPlayer.Server && !requester.Active)
            {
                return;
            }

            requester.SendMessage(message, color);
        });
    }
}
