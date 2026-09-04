using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockPlrExporter.Data;
using TShockPlrExporter.Exporting;

namespace TShockPlrExporter;

[ApiVersion(2, 1)]
public sealed class Plugin : TerrariaPlugin
{
    /// <summary>聊天里最多列出多少个失败账号，其余只写日志，避免批量导出时刷屏。</summary>
    private const int MaxListedFailures = 5;

    private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>主线程队列停摆多久就认为 GameUpdate 钩子出了问题，并在控制台提示。</summary>
    private static readonly TimeSpan DrainStallThreshold = TimeSpan.FromSeconds(5);

    private readonly MainThreadQueue mainThread = new();
    private readonly PlrExporter exporter;
    private readonly CancellationTokenSource shutdown = new();

    private Command? exportCommand;
    private int exportRunning;
    private long exportStartedTicks;

    public override string Name => "TShockPlrExporter";
    public override string Author => "TShockPlrExporter Contributors";
    public override string Description => "将 TShock 的 SSC 人物数据导出为 Terraria .plr 文件。";
    public override Version Version => new(1, 1, 2);

    /// <summary>消息级别。只用 TShock 的字符串接口，不碰 Color，控制台与游戏内都能正常显示。</summary>
    private enum Level
    {
        Info,
        Success,
        Warning,
        Error
    }

    public Plugin(Main game) : base(game)
    {
        exporter = new PlrExporter(mainThread);
    }

    public override void Initialize()
    {
        exportCommand = new Command("plrexporter.export", ExportCommand, "player", "exportplr")
        {
            HelpText = "导出 SSC 人物存档：/player <账号名|账号 ID|all>"
        };

        Commands.ChatCommands.Add(exportCommand);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

        // 版本号打在启动信息里：换过 DLL 之后可以一眼确认服务器实际加载的是哪一版。
        TShock.Log.ConsoleInfo(PlrExporter.UsesInternalSave
            ? $"[TShockPlrExporter] v{Version} 已就绪，导出过程不会改动服务器的 SSC 状态。"
            : $"[TShockPlrExporter] v{Version} 警告：未找到 Terraria 内部保存接口，将回退为临时切换 SSC 模式，" +
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
            args.Player.SendErrorMessage("用法：/player <账号名|账号 ID|all>");
            return;
        }

        // 同一时间只允许一个导出任务：批量导出会构造大量 Player 对象并写盘，
        // 并发跑没有收益，只会放大磁盘压力和备份轮转的竞争。
        if (Interlocked.CompareExchange(ref exportRunning, 1, 0) != 0)
        {
            TimeSpan elapsed = new(Math.Max(0, DateTime.UtcNow.Ticks - Interlocked.Read(ref exportStartedTicks)));
            args.Player.SendErrorMessage(
                $"已有导出任务正在执行（已运行 {elapsed.TotalSeconds:F0} 秒），请等它结束后再试。");
            return;
        }

        Interlocked.Exchange(ref exportStartedTicks, DateTime.UtcNow.Ticks);

        TSPlayer requester = args.Player;
        string target = args.Parameters[0];

        requester.SendInfoMessage("导出任务已开始，完成后会在这里汇总结果。");

        // 放到后台线程：批量导出的 SQL 查询、Player 构造和写盘都是同步操作，
        // 留在命令线程上会卡住服务器（控制台命令线程或客户端封包线程）。
        _ = Task.Run(() => RunExport(requester, target), CancellationToken.None)
            .ContinueWith(
                task =>
                {
                    Interlocked.Exchange(ref exportRunning, 0);

                    // RunExport 内部已经全包了 try/catch，但如果连 catch 里的日志调用都抛了，
                    // 异常会落到这里。不观测的话任务就彻底无声无息地消失。
                    if (task.IsFaulted)
                    {
                        SafeLogError($"[TShockPlrExporter] 导出任务异常终止：{task.Exception}");
                        Notify(requester, "导出任务异常终止，详情见服务器日志。", Level.Error);
                    }
                },
                TaskScheduler.Default);
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
                Notify(requester, $"未找到与“{target}”匹配且有 SSC 人物数据的账号。", Level.Error);
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
                Level.Error);
        }
        catch (Exception ex)
        {
            SafeLogError($"[TShockPlrExporter][{traceId}] 执行导出命令失败：{ex}");
            Notify(requester, $"导出失败，详情见服务器日志（编号 {traceId}）。", Level.Error);
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
                SafeLogWarn($"[TShockPlrExporter][{traceId}] 插件正在卸载，导出提前结束。");
                break;
            }

            try
            {
                string path = exporter.Export(database, account, exportRoot);
                success++;
                SafeLogInfo(
                    $"[TShockPlrExporter][{traceId}] 已导出账号 {account.Id}（{account.Name}）→ {path}");
            }
            catch (Exception ex)
            {
                failures.Add($"{account.Name}（ID {account.Id}）");
                SafeLogError(
                    $"[TShockPlrExporter][{traceId}] 导出账号 {account.Id}（{account.Name}）失败：{ex}");
            }
        }

        // 结果只发两条消息，而不是每个账号一条：批量导出时逐条回显会把执行者刷下线。
        // 消息里统一显示相对路径：绝对路径又长又跟面板服的实例目录绑死，读起来没有帮助，
        // 真正需要它的时候去日志里看（下面那条日志无条件带绝对路径）。
        string summary = $"导出完成：成功 {success} 个，失败 {failures.Count} 个。目录：tshock/PlayerExports（编号 {traceId}）";

        // 汇总总是写一份进日志：即使消息投递环节出问题，也还有可查的记录。
        SafeLogInfo($"[TShockPlrExporter][{traceId}] 导出完成：成功 {success} 个，失败 {failures.Count} 个，" +
            $"目录 {exportRoot}");

        Notify(requester, summary, failures.Count == 0 ? Level.Success : Level.Warning);

        if (failures.Count != 0)
        {
            string listed = string.Join("、", failures.Take(MaxListedFailures));
            string more = failures.Count > MaxListedFailures ? $" 等共 {failures.Count} 个" : string.Empty;
            Notify(requester, $"失败账号：{listed}{more}。详情见服务器日志（编号 {traceId}）。", Level.Error);
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

            SafeLogInfo(
                $"[TShockPlrExporter][{traceId}] 已先同步 {online.Count} 名在线玩家的 SSC 数据：" +
                string.Join("、", online.Select(player => player.Name)));
        }
        catch (Exception ex)
        {
            SafeLogWarn(
                $"[TShockPlrExporter][{traceId}] 同步在线玩家 SSC 数据失败，导出的可能是较旧的数据：{ex.Message}");
        }
    }

    /// <summary>
    /// 把结果消息发给命令执行者。
    ///
    /// 控制台和真实玩家走两条完全不同的路：控制台输出本质上就是 <c>Console.Write</c>，
    /// 线程安全且与游戏循环无关，没有任何理由绕主线程队列——绕了反而让「导出完成」这条消息
    /// 依赖 GameUpdate 钩子，一旦钩子没跑起来，命令就只有「已开始」而永远没有结果。
    /// 真实玩家的消息要发网络封包，必须回到主线程。
    /// </summary>
    private void Notify(TSPlayer requester, string message, Level level)
    {
        try
        {
            if (requester == TSPlayer.Server)
            {
                // ConsoleInfo/ConsoleError 会同时写控制台和日志文件，结果不会只存在于某一处。
                if (level == Level.Error)
                {
                    TShock.Log.ConsoleError($"[TShockPlrExporter] {message}");
                }
                else
                {
                    TShock.Log.ConsoleInfo($"[TShockPlrExporter] {message}");
                }

                return;
            }

            WarnIfQueueStalled();

            mainThread.Enqueue(() =>
            {
                if (!requester.Active)
                {
                    return;
                }

                switch (level)
                {
                    case Level.Success:
                        requester.SendSuccessMessage(message);
                        break;
                    case Level.Warning:
                        requester.SendWarningMessage(message);
                        break;
                    case Level.Error:
                        requester.SendErrorMessage(message);
                        break;
                    default:
                        requester.SendInfoMessage(message);
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            // 通知失败不该反过来把导出结果吞掉，兜底打到标准输出。
            Console.WriteLine($"[TShockPlrExporter] 发送结果消息失败：{ex.Message}｜原消息：{message}");
        }
    }

    /// <summary>
    /// 主线程队列长时间没被抽取，说明 GameUpdate 钩子没有运行，排进去的消息不会被发出。
    /// 这种故障如果不主动报出来，表现就是「命令说开始了，然后什么都没有」，极难排查。
    /// </summary>
    private void WarnIfQueueStalled()
    {
        TimeSpan idle = mainThread.SinceLastDrain;

        if (idle > DrainStallThreshold)
        {
            SafeLogError(
                $"[TShockPlrExporter] 主线程队列已有 {idle.TotalSeconds:F1} 秒没有被抽取，" +
                "GameUpdate 钩子可能没有运行，发给玩家的消息将无法送出。");
        }
    }

    private static void SafeLogInfo(string message)
    {
        try
        {
            TShock.Log.Info(message);
        }
        catch
        {
            Console.WriteLine(message);
        }
    }

    /// <summary>写错误日志，并保证这个动作本身不会再抛异常把调用方的 catch 块炸掉。</summary>
    private static void SafeLogError(string message)
    {
        try
        {
            TShock.Log.ConsoleError(message);
        }
        catch
        {
            Console.WriteLine(message);
        }
    }

    private static void SafeLogWarn(string message)
    {
        try
        {
            TShock.Log.Warn(message);
        }
        catch
        {
            Console.WriteLine(message);
        }
    }
}
