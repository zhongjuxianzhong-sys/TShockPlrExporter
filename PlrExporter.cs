using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using TShockAPI;

namespace TShockPlrExporter;

internal sealed class PlrExporter
{
    /// <summary>每个账号最多保留的 .plr.bak 备份数量，更旧的会被删除以避免占满磁盘。</summary>
    private const int BackupRetention = 10;

    private const int MaxFileNameLength = 64;

    private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Windows 保留设备名，作为文件名会让写入流向设备而不是磁盘。</summary>
    private static readonly string[] ReservedFileNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly MethodInfo? InternalSavePlayerFile = typeof(Player).GetMethod(
        "InternalSavePlayerFile",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        types: new[] { typeof(PlayerFileData) },
        modifiers: null);

    private static readonly object SaveLock = new();

    private static volatile bool useInternalSave = InternalSavePlayerFile is not null;

    private readonly MainThreadQueue mainThread;

    public PlrExporter(MainThreadQueue mainThread)
    {
        this.mainThread = mainThread;
    }

    /// <summary>是否走「不改写全局 SSC 状态」的内部保存接口。</summary>
    public static bool UsesInternalSave => useInternalSave;

    public string Export(CharacterDatabase database, ExportAccount account, string outputDirectory)
    {
        CharacterRecord record = database.ReadCharacter(account.Id)
            ?? throw new InvalidOperationException("tsCharacter 中没有该账号的人物数据。");

        Player player = BuildPlayer(account, record);
        string path = ResolveOutputPath(outputDirectory, account);

        BackupExisting(path);
        WritePlayerFile(player, path);

        FileInfo info = new(path);
        if (!info.Exists)
        {
            throw new IOException($"Terraria 没有生成人物文件：{Path.GetFileName(path)}");
        }

        if (info.Length == 0)
        {
            throw new IOException($"Terraria 生成的人物文件为空：{Path.GetFileName(path)}");
        }

        return path;
    }

    /// <summary>
    /// 拼出导出路径，并断言它仍落在目标目录内。
    /// 文件名带上账号 ID：不同账号名归一化后可能撞成同一个文件（例如 <c>a/b</c> 与 <c>a_b</c>），
    /// 带上 ID 就不会互相覆盖，同时导出结果也能对回具体账号。
    /// </summary>
    private static string ResolveOutputPath(string outputDirectory, ExportAccount account)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        string fileName = $"{SanitizeFileName(account.Name)}-{account.Id}.plr";
        string path = Path.GetFullPath(Path.Combine(root, fileName));

        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"账号 {account.Id} 的导出路径越出了目标目录。");
        }

        return path;
    }

    /// <summary>
    /// 把账号名压成安全文件名。
    /// 这里用字符白名单而不是 <see cref="Path.GetInvalidFileNameChars"/>：后者在 Linux 上
    /// 只返回 <c>\0</c> 和 <c>/</c>，反斜杠、冒号、前后点号等都会被放过。
    /// <see cref="char.IsLetterOrDigit(char)"/> 对中日韩字符返回 true，所以中文账号名不会被打成下划线。
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        StringBuilder builder = new(name.Length);

        foreach (char character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        string sanitized = builder.ToString().Trim('.', ' ', '_');

        if (sanitized.Length > MaxFileNameLength)
        {
            sanitized = sanitized[..MaxFileNameLength];
        }

        if (sanitized.Length == 0)
        {
            return "player";
        }

        return ReservedFileNames.Contains(sanitized, StringComparer.OrdinalIgnoreCase)
            ? "_" + sanitized
            : sanitized;
    }

    /// <summary>
    /// 把已存在的同名文件改名为带时间戳的备份。时间戳精确到毫秒并在冲突时追加序号，
    /// 避免同一秒内重复导出时 <see cref="File.Move(string, string)"/> 因目标已存在而抛异常。
    /// </summary>
    private static void BackupExisting(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        string directory = Path.GetDirectoryName(path)!;
        string baseName = Path.GetFileNameWithoutExtension(path);
        string backup;
        int suffix = 0;

        do
        {
            string tag = suffix == 0 ? string.Empty : $"-{suffix}";
            backup = Path.Combine(directory, $"{baseName}.{DateTime.Now:yyyyMMddHHmmssfff}{tag}.plr.bak");
            suffix++;
        }
        while (File.Exists(backup) && suffix < 1000);

        File.Move(path, backup);
        PruneBackups(directory, baseName);
    }

    /// <summary>
    /// 只删除本插件自己生成的、超出保留数量的旧备份。
    /// 这是附加动作：无论清理还是写日志出问题，都不该让一次已经成功的导出变成失败，
    /// 所以先把删除全部做完，最后才写一条汇总日志。
    /// </summary>
    private static void PruneBackups(string directory, string baseName)
    {
        try
        {
            FileInfo[] stale = new DirectoryInfo(directory)
                .GetFiles($"{baseName}.*.plr.bak")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(BackupRetention)
                .ToArray();

            if (stale.Length == 0)
            {
                return;
            }

            List<string> deleted = new(stale.Length);
            foreach (FileInfo file in stale)
            {
                string name = file.Name;
                file.Delete();
                deleted.Add(name);
            }

            TShock.Log.Info(
                $"[TShockPlrExporter] 已删除 {deleted.Count} 个超出保留数量（{BackupRetention}）的旧备份：" +
                string.Join("、", deleted));
        }
        catch (Exception ex)
        {
            TryLogWarn($"[TShockPlrExporter] 清理旧备份失败：{ex.Message}");
        }
    }

    private static void TryLogWarn(string message)
    {
        try
        {
            TShock.Log.Warn(message);
        }
        catch
        {
            // 日志通道本身不可用时静默忽略，不要因此掩盖或推翻导出结果。
        }
    }

    private void WritePlayerFile(Player player, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        PlayerFileData data = new(path, cloudSave: false)
        {
            Metadata = FileMetadata.FromCurrentSettings(FileType.Player),
            Player = player
        };

        if (useInternalSave && InternalSavePlayerFile is not null)
        {
            // 直接调用 Terraria 的内部写盘方法。SSC 拦截在公开的 Player.SavePlayer 入口，
            // 走内部方法就不需要改写 Main.ServerSideCharacter —— 那是全服共享状态，
            // 在导出期间置为 false 会让服务器其他逻辑误判 SSC 已关闭，可能跳过玩家存档保存。
            InvokeInternalSave(data);

            if (IsWritten(path))
            {
                return;
            }

            // 方法存在但没产出文件，说明这个 Terraria 版本的内部实现不同，永久降级。
            useInternalSave = false;
            TShock.Log.Warn(
                "[TShockPlrExporter] 内部保存接口没有生成文件，后续导出改用临时切换 SSC 模式的回退方案。");
        }

        SaveByTogglingServerSideCharacter(data);

        if (!IsWritten(path))
        {
            throw new IOException("回退保存方案也没有生成人物文件。");
        }
    }

    private static void InvokeInternalSave(PlayerFileData data)
    {
        try
        {
            InternalSavePlayerFile!.Invoke(null, new object[] { data });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    /// <summary>
    /// 回退方案：临时关闭 SSC 再调用公开保存接口。
    /// 必须在主线程执行 —— <see cref="Main.ServerSideCharacter"/> 是全服共享状态，
    /// 在后台线程改写会与服务器主循环竞争。放到主线程后，至少主循环自身不会观察到这个中间态。
    /// </summary>
    private void SaveByTogglingServerSideCharacter(PlayerFileData data)
    {
        mainThread.Invoke(
            () =>
            {
                lock (SaveLock)
                {
                    bool previous = Main.ServerSideCharacter;
                    try
                    {
                        Main.ServerSideCharacter = false;
                        Player.SavePlayer(data, skipMapSave: true);
                    }
                    finally
                    {
                        Main.ServerSideCharacter = previous;
                    }
                }
            },
            MainThreadTimeout);
    }

    private static bool IsWritten(string path)
    {
        FileInfo info = new(path);
        return info.Exists && info.Length != 0;
    }

    private static Player BuildPlayer(ExportAccount account, CharacterRecord record)
    {
        Player player = new()
        {
            name = account.Name,
            statLife = Math.Max(1, record.Health),
            statLifeMax = Math.Max(100, record.MaxHealth),
            statMana = Math.Max(0, record.Mana),
            statManaMax = Math.Max(0, record.MaxMana),
            SpawnX = record.SpawnX,
            SpawnY = record.SpawnY,
            hairDye = ClampByte(record.HairDye),
            hairColor = record.HairColor,
            pantsColor = record.PantsColor,
            shirtColor = record.ShirtColor,
            underShirtColor = record.UnderShirtColor,
            shoeColor = record.ShoeColor,
            skinColor = record.SkinColor,
            eyeColor = record.EyeColor,
            anglerQuestsFinished = Math.Max(0, record.QuestsCompleted),
            UsingBiomeTorches = record.UsingBiomeTorches,
            happyFunTorchTime = record.HappyFunTorchTime,
            unlockedBiomeTorches = record.UnlockedBiomeTorches,
            ateArtisanBread = record.AteArtisanBread,
            usedAegisCrystal = record.UsedAegisCrystal,
            usedAegisFruit = record.UsedAegisFruit,
            usedArcaneCrystal = record.UsedArcaneCrystal,
            usedGalaxyPearl = record.UsedGalaxyPearl,
            usedGummyWorm = record.UsedGummyWorm,
            usedAmbrosia = record.UsedAmbrosia,
            unlockedSuperCart = record.UnlockedSuperCart,
            enabledSuperCart = record.EnabledSuperCart,
            numberOfDeathsPVE = Math.Max(0, record.DeathsPve),
            numberOfDeathsPVP = Math.Max(0, record.DeathsPvp),
            voiceVariant = record.VoiceVariant,
            voicePitchOffset = record.VoicePitchOffset
        };

        // 下面这几个字段会在客户端加载存档时被当作数组下标使用，
        // 数据库里的异常值会直接让客户端崩在读档阶段，所以按运行时的实际上界收敛。
        player.skinVariant = ClampWithWarning(account, "skinVariant", record.SkinVariant, 0, PlayerVariantID.Count - 1);
        player.hair = ClampWithWarning(account, "hair", record.Hair, 0, Math.Max(0, Main.maxHairStyles - 1));
        player.team = ClampWithWarning(account, "team", record.Team, 0, Math.Max(0, Main.teamColor.Length - 1));
        player.CurrentLoadoutIndex = ClampWithWarning(
            account, "currentLoadoutIndex", record.CurrentLoadoutIndex, 0, player.Loadouts.Length - 1);

        player.extraAccessory = record.ExtraSlot != 0;
        RestoreInventory(record.Inventory, player);
        CopyHideVisuals(record.HideVisuals, player.hideVisibleAccessory);
        return player;
    }

    private static int ClampWithWarning(ExportAccount account, string field, int value, int min, int max)
    {
        int clamped = Math.Clamp(value, min, max);

        if (clamped != value)
        {
            TShock.Log.Warn(
                $"[TShockPlrExporter] 账号 {account.Name}（ID {account.Id}）的 {field} 值 {value} " +
                $"超出有效范围 [{min}, {max}]，已修正为 {clamped}。");
        }

        return clamped;
    }

    private static void RestoreInventory(Item[] source, Player player)
    {
        CopyItemRange(source, NetItem.InventoryIndex, player.inventory);
        CopyItemRange(source, NetItem.ArmorIndex, player.armor);
        CopyItemRange(source, NetItem.DyeIndex, player.dye);
        CopyItemRange(source, NetItem.MiscEquipIndex, player.miscEquips);
        CopyItemRange(source, NetItem.MiscDyeIndex, player.miscDyes);
        CopyItemRange(source, NetItem.PiggyIndex, player.bank.item);
        CopyItemRange(source, NetItem.SafeIndex, player.bank2.item);

        if (NetItem.TrashIndex.Item1 < source.Length)
        {
            player.trashItem = source[NetItem.TrashIndex.Item1].Clone();
        }

        CopyItemRange(source, NetItem.ForgeIndex, player.bank3.item);
        CopyItemRange(source, NetItem.VoidIndex, player.bank4.item);

        CopyItemRange(source, NetItem.Loadout1Armor, player.Loadouts[0].Armor);
        CopyItemRange(source, NetItem.Loadout1Dye, player.Loadouts[0].Dye);
        CopyItemRange(source, NetItem.Loadout2Armor, player.Loadouts[1].Armor);
        CopyItemRange(source, NetItem.Loadout2Dye, player.Loadouts[1].Dye);
        CopyItemRange(source, NetItem.Loadout3Armor, player.Loadouts[2].Armor);
        CopyItemRange(source, NetItem.Loadout3Dye, player.Loadouts[2].Dye);
    }

    private static void CopyItemRange(Item[] source, Tuple<int, int> range, Item[] destination)
    {
        int sourceStart = range.Item1;
        int count = Math.Min(range.Item2 - sourceStart, destination.Length);

        for (int i = 0; i < count && sourceStart + i < source.Length; i++)
        {
            destination[i] = source[sourceStart + i].Clone();
        }

        for (int i = count; i < destination.Length; i++)
        {
            destination[i] = new Item();
        }
    }

    private static void CopyHideVisuals(bool[] source, bool[] destination)
    {
        int count = Math.Min(source.Length, destination.Length);
        Array.Clear(destination);

        for (int i = 0; i < count; i++)
        {
            destination[i] = source[i];
        }
    }

    private static byte ClampByte(int value)
    {
        return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
    }
}
