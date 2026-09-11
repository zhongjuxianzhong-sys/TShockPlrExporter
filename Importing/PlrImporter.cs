using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using TShockAPI;
using TShockAPI.DB;
using TShockPlrExporter.Data;

namespace TShockPlrExporter.Importing;

/// <summary>
/// 读取原生 .plr 文件，并把其中的角色数据写入 TShock 的 SSC 表。
/// </summary>
internal sealed class PlrImporter
{
    /// <summary>
    /// TShock 的离线玩家需要一个 fake Player 作为 TPlayer。该字段是 TShock 6.1.0 的实现细节，
    /// 但比起在本插件里复制一份 PlayerData.CopyCharacter 的字段映射，它更不容易漏掉 SSC 字段。
    /// </summary>
    private static readonly FieldInfo? FakePlayerField = typeof(TSPlayer).GetField(
        "FakePlayer",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// 从导入目录解析一个安全的文件名，并把它读成 Terraria Player。
    /// </summary>
    public Player Load(string path, ExportAccount account)
    {
        PlayerFileData? fileData = Player.LoadPlayer(path, cloudSave: false);
        Player? player = fileData?.Player;

        if (player is null)
        {
            throw new InvalidDataException("Terraria 没有从人物文件中读出角色数据。");
        }

        if (player.Loadouts is null || player.Loadouts.Length == 0)
        {
            throw new InvalidDataException("人物文件缺少 Loadout 数据，无法导入。");
        }

        IReadOnlyList<string> warnings = NormalizeImportedPlayer(player);
        foreach (string warning in warnings)
        {
            TryLogWarn($"[TShockPlrExporter] 导入账号 {account.Name}（ID {account.Id}）时修正了角色字段：{warning}");
        }

        return player;
    }

    /// <summary>
    /// 把已经读取的 Player 转成 TShock PlayerData，并通过独立数据库连接写入 SSC。
    /// </summary>
    public void WriteToSsc(Player player, ExportAccount account)
    {
        PlayerData data = BuildPlayerData(player, account);

        using CharacterDatabase database = CharacterDatabase.OpenForWrite();
        database.UpsertCharacter(account.Id, data);
    }

    internal static PlayerData BuildPlayerData(Player player, ExportAccount account)
    {
        return BuildPlayerData(player, account, out _);
    }

    private static PlayerData BuildPlayerData(
        Player player,
        ExportAccount account,
        out OfflinePlayer proxy)
    {
        if (FakePlayerField is null)
        {
            throw new NotSupportedException(
                "当前 TShock 版本无法创建离线导入玩家，找不到 TSPlayer.FakePlayer 字段。");
        }

        proxy = new OfflinePlayer(account.Name);
        FakePlayerField.SetValue(proxy, player);
        proxy.Account = new UserAccount
        {
            ID = account.Id,
            Name = account.Name
        };
        proxy.IsLoggedIn = true;

        PlayerData data = new(includingStarterInventory: false);
        data.CopyCharacter(proxy);
        return data;
    }

    internal static string ResolveInputPath(string importDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("导入文件名不能为空。", nameof(fileName));
        }

        if (Path.IsPathRooted(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            || fileName.Contains(':'))
        {
            throw new InvalidOperationException("导入参数只能是 PlayerImports 目录下的文件名，不能包含路径。");
        }

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("导入文件名包含当前系统不允许的字符。");
        }

        if (!string.Equals(Path.GetExtension(fileName), ".plr", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("导入文件必须是 .plr 人物存档。");
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(importDirectory));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, fileName));

        if (!path.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("导入文件路径越出了 PlayerImports 目录。");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到导入文件。", path);
        }

        if (new FileInfo(path).Length == 0)
        {
            throw new InvalidDataException("导入文件为空。");
        }

        return path;
    }

    /// <summary>
    /// 回收会影响客户端读档的异常值；返回被修正的字段，便于调用方统一写日志。
    /// </summary>
    internal static IReadOnlyList<string> NormalizeImportedPlayer(Player player)
    {
        List<string> warnings = new();

        int maxHealth = Math.Max(100, player.statLifeMax);
        int health = Math.Clamp(player.statLife, 1, maxHealth);
        WarnIfChanged(nameof(player.statLifeMax), player.statLifeMax, maxHealth, warnings);
        WarnIfChanged(nameof(player.statLife), player.statLife, health, warnings);
        player.statLifeMax = maxHealth;
        player.statLife = health;

        int maxMana = Math.Max(0, player.statManaMax);
        int mana = Math.Clamp(player.statMana, 0, maxMana);
        WarnIfChanged(nameof(player.statManaMax), player.statManaMax, maxMana, warnings);
        WarnIfChanged(nameof(player.statMana), player.statMana, mana, warnings);
        player.statManaMax = maxMana;
        player.statMana = mana;

        player.skinVariant = ClampForImport(
            nameof(player.skinVariant), player.skinVariant, 0, Math.Max(0, PlayerVariantID.Count - 1), warnings);
        player.hair = ClampForImport(
            nameof(player.hair), player.hair, 0, Math.Max(0, Main.maxHairStyles - 1), warnings);
        player.team = ClampForImport(
            nameof(player.team), player.team, 0, Math.Max(0, Main.teamColor.Length - 1), warnings);
        player.CurrentLoadoutIndex = ClampForImport(
            nameof(player.CurrentLoadoutIndex),
            player.CurrentLoadoutIndex,
            0,
            Math.Max(0, player.Loadouts.Length - 1),
            warnings);

        int questsCompleted = Math.Max(0, player.anglerQuestsFinished);
        WarnIfChanged(nameof(player.anglerQuestsFinished), player.anglerQuestsFinished, questsCompleted, warnings);
        player.anglerQuestsFinished = questsCompleted;

        int deathsPve = Math.Max(0, player.numberOfDeathsPVE);
        WarnIfChanged(nameof(player.numberOfDeathsPVE), player.numberOfDeathsPVE, deathsPve, warnings);
        player.numberOfDeathsPVE = deathsPve;

        int deathsPvp = Math.Max(0, player.numberOfDeathsPVP);
        WarnIfChanged(nameof(player.numberOfDeathsPVP), player.numberOfDeathsPVP, deathsPvp, warnings);
        player.numberOfDeathsPVP = deathsPvp;

        return warnings;
    }

    internal static int ClampForImport(
        string field,
        int value,
        int min,
        int max,
        List<string> warnings)
    {
        int clamped = Math.Clamp(value, min, max);
        WarnIfChanged(field, value, clamped, warnings);
        return clamped;
    }

    private static void WarnIfChanged(string field, int original, int corrected, List<string> warnings)
    {
        if (original != corrected)
        {
            warnings.Add($"{field} 值 {original} 已修正为 {corrected}");
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
            Console.WriteLine(message);
        }
    }

    private sealed class OfflinePlayer : TSPlayer
    {
        public OfflinePlayer(string playerName) : base(playerName)
        {
        }
    }
}
