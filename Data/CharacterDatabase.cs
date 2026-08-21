using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Xna.Framework;
using MySql.Data.MySqlClient;
using Terraria;
using TShockAPI;

namespace TShockPlrExporter.Data;

/// <summary>
/// 读取 TShock 的账号表与 SSC 人物表。
/// 有意不复用 <see cref="TShock.DB"/>：ADO.NET 连接不是线程安全的，导出运行在
/// 后台线程，共用那一个连接会与服务器自身的查询相互踩踏。这里按 TShock 的配置
/// 另开一个专用连接，并且只发 SELECT。
/// </summary>
internal sealed class CharacterDatabase : IDisposable
{
    private const string AccountSelect =
        "SELECT u.ID, u.Username FROM Users u INNER JOIN tsCharacter c ON c.Account = u.ID";

    private static bool schemaWarningLogged;

    private readonly IDbConnection connection;

    private CharacterDatabase(IDbConnection connection)
    {
        this.connection = connection;
    }

    public static CharacterDatabase Open()
    {
        string storageType = TShock.Config.Settings.StorageType?.Trim() ?? string.Empty;

        if (storageType.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            return new CharacterDatabase(OpenMySql());
        }

        if (storageType.Length != 0 && !storageType.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"不支持的存储类型“{storageType}”，插件目前只支持 sqlite 与 mysql。");
        }

        return new CharacterDatabase(OpenSqlite());
    }

    public void Dispose()
    {
        connection.Dispose();
    }

    private static IDbConnection OpenSqlite()
    {
        string path = Path.Combine(Path.GetFullPath(TShock.SavePath), "tshock.sqlite");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到 TShock 的 SQLite 数据库文件。", path);
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    private static IDbConnection OpenMySql()
    {
        string connectionString = BuildMySqlConnectionString();

        // 注意：任何异常信息、日志都不要带上 connectionString，里面含有数据库密码。
        MySqlConnection connection = new(connectionString);
        connection.Open();
        return connection;
    }

    private static string BuildMySqlConnectionString()
    {
        string configured = TShock.Config.Settings.MySqlConnectionString ?? string.Empty;

        if (configured.Trim().Length != 0)
        {
            return configured;
        }

        string[] host = (TShock.Config.Settings.MySqlHost ?? string.Empty).Split(':');

        if (host.Length == 0 || host[0].Trim().Length == 0)
        {
            throw new InvalidOperationException("TShock 配置为 MySQL 存储，但 MySqlHost 为空，无法连接数据库。");
        }

        MySqlConnectionStringBuilder builder = new()
        {
            Server = host[0].Trim(),
            Port = host.Length > 1 && uint.TryParse(host[1], out uint port) ? port : 3306u,
            Database = TShock.Config.Settings.MySqlDbName ?? string.Empty,
            UserID = TShock.Config.Settings.MySqlUsername ?? string.Empty,
            Password = TShock.Config.Settings.MySqlPassword ?? string.Empty
        };

        return builder.ToString();
    }

    private IDbCommand CreateCommand(string sql)
    {
        IDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        IDbDataParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public IReadOnlyList<ExportAccount> GetAllAccounts()
    {
        using IDbCommand command = CreateCommand($"{AccountSelect} ORDER BY u.ID;");
        return ReadAccounts(command);
    }

    public IReadOnlyList<ExportAccount> FindAccounts(string target)
    {
        if (int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            using IDbCommand byId = CreateCommand($"{AccountSelect} WHERE u.ID = @id;");
            AddParameter(byId, "@id", id);

            IReadOnlyList<ExportAccount> matches = ReadAccounts(byId);
            if (matches.Count != 0)
            {
                return matches;
            }

            // 按 ID 查不到就继续按名字查：否则纯数字的账号名永远查不出来。
        }

        // 用 LOWER() 而不是 COLLATE NOCASE：后者是 SQLite 专有语法，在 MySQL 上会直接报错。
        using IDbCommand byName = CreateCommand($"{AccountSelect} WHERE LOWER(u.Username) = LOWER(@name);");
        AddParameter(byName, "@name", target);
        return ReadAccounts(byName);
    }

    private static IReadOnlyList<ExportAccount> ReadAccounts(IDbCommand command)
    {
        List<ExportAccount> accounts = new();

        using IDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            accounts.Add(new ExportAccount(
                Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty));
        }

        return accounts;
    }

    public CharacterRecord? ReadCharacter(int accountId)
    {
        using IDbCommand command = CreateCommand("SELECT * FROM tsCharacter WHERE Account = @account;");
        AddParameter(command, "@account", accountId);

        using IDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        // 按列名建索引而不是逐个 GetOrdinal：TShock 版本之间 tsCharacter 的列会增减，
        // 缺列时应该退化成默认值，而不是抛 IndexOutOfRangeException 让整个账号导出失败。
        Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns[reader.GetName(i)] = i;
        }

        WarnAboutMissingColumns(columns);
        return ReadRecord(reader, columns);
    }

    private static CharacterRecord ReadRecord(IDataReader reader, Dictionary<string, int> columns)
    {
        return new CharacterRecord
        {
            Account = GetInt(reader, columns, "Account"),
            Health = GetInt(reader, columns, "Health"),
            MaxHealth = GetInt(reader, columns, "MaxHealth"),
            Mana = GetInt(reader, columns, "Mana"),
            MaxMana = GetInt(reader, columns, "MaxMana"),
            Inventory = DecodeInventory(GetString(reader, columns, "Inventory")),
            ExtraSlot = GetInt(reader, columns, "extraSlot"),
            SpawnX = GetInt(reader, columns, "spawnX"),
            SpawnY = GetInt(reader, columns, "spawnY"),
            SkinVariant = GetInt(reader, columns, "skinVariant"),
            Hair = GetInt(reader, columns, "hair"),
            HairDye = GetInt(reader, columns, "hairDye"),
            HairColor = DecodeColor(GetNullableInt(reader, columns, "hairColor"), new Color(215, 90, 55)),
            PantsColor = DecodeColor(GetNullableInt(reader, columns, "pantsColor"), new Color(255, 230, 175)),
            ShirtColor = DecodeColor(GetNullableInt(reader, columns, "shirtColor"), new Color(175, 165, 140)),
            UnderShirtColor = DecodeColor(GetNullableInt(reader, columns, "underShirtColor"), new Color(160, 180, 215)),
            ShoeColor = DecodeColor(GetNullableInt(reader, columns, "shoeColor"), new Color(160, 105, 60)),
            HideVisuals = DecodeBoolArray(GetNullableInt(reader, columns, "hideVisuals"), 10),
            SkinColor = DecodeColor(GetNullableInt(reader, columns, "skinColor"), new Color(255, 125, 90)),
            EyeColor = DecodeColor(GetNullableInt(reader, columns, "eyeColor"), new Color(105, 90, 75)),
            QuestsCompleted = GetInt(reader, columns, "questsCompleted"),
            UsingBiomeTorches = GetBool(reader, columns, "usingBiomeTorches"),
            HappyFunTorchTime = GetBool(reader, columns, "happyFunTorchTime"),
            UnlockedBiomeTorches = GetBool(reader, columns, "unlockedBiomeTorches"),
            CurrentLoadoutIndex = GetInt(reader, columns, "currentLoadoutIndex"),
            AteArtisanBread = GetBool(reader, columns, "ateArtisanBread"),
            UsedAegisCrystal = GetBool(reader, columns, "usedAegisCrystal"),
            UsedAegisFruit = GetBool(reader, columns, "usedAegisFruit"),
            UsedArcaneCrystal = GetBool(reader, columns, "usedArcaneCrystal"),
            UsedGalaxyPearl = GetBool(reader, columns, "usedGalaxyPearl"),
            UsedGummyWorm = GetBool(reader, columns, "usedGummyWorm"),
            UsedAmbrosia = GetBool(reader, columns, "usedAmbrosia"),
            UnlockedSuperCart = GetBool(reader, columns, "unlockedSuperCart"),
            EnabledSuperCart = GetBool(reader, columns, "enabledSuperCart"),
            DeathsPve = GetInt(reader, columns, "deathsPVE"),
            DeathsPvp = GetInt(reader, columns, "deathsPVP"),
            VoiceVariant = GetInt(reader, columns, "voiceVariant"),
            VoicePitchOffset = GetFloat(reader, columns, "voicePitchOffset"),
            Team = GetInt(reader, columns, "team")
        };
    }

    private static readonly string[] ExpectedColumns =
    {
        "Account", "Health", "MaxHealth", "Mana", "MaxMana", "Inventory", "extraSlot", "spawnX", "spawnY",
        "skinVariant", "hair", "hairDye", "hairColor", "pantsColor", "shirtColor", "underShirtColor",
        "shoeColor", "hideVisuals", "skinColor", "eyeColor", "questsCompleted", "usingBiomeTorches",
        "happyFunTorchTime", "unlockedBiomeTorches", "currentLoadoutIndex", "ateArtisanBread",
        "usedAegisCrystal", "usedAegisFruit", "usedArcaneCrystal", "usedGalaxyPearl", "usedGummyWorm",
        "usedAmbrosia", "unlockedSuperCart", "enabledSuperCart", "deathsPVE", "deathsPVP",
        "voiceVariant", "voicePitchOffset", "team"
    };

    /// <summary>只在一次服务器生命周期里提示一次缺列，避免批量导出时刷满日志。</summary>
    private static void WarnAboutMissingColumns(Dictionary<string, int> columns)
    {
        if (schemaWarningLogged)
        {
            return;
        }

        schemaWarningLogged = true;

        List<string> missing = ExpectedColumns.Where(column => !columns.ContainsKey(column)).ToList();
        if (missing.Count != 0)
        {
            TShock.Log.Warn(
                $"[TShockPlrExporter] tsCharacter 缺少 {missing.Count} 个预期列，这些字段将使用默认值：" +
                string.Join("、", missing));
        }
    }

    private static object? GetValue(IDataReader reader, Dictionary<string, int> columns, string name)
    {
        if (!columns.TryGetValue(name, out int ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal);
    }

    // 统一走 Convert：SQLite 与 MySQL 对同一列返回的 CLR 类型不一定相同
    // （例如 long 与 int、decimal 与 double），直接 GetInt32 会在某一侧抛 InvalidCastException。
    private static int GetInt(IDataReader reader, Dictionary<string, int> columns, string name)
    {
        object? value = GetValue(reader, columns, name);
        return value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static int? GetNullableInt(IDataReader reader, Dictionary<string, int> columns, string name)
    {
        object? value = GetValue(reader, columns, name);
        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static float GetFloat(IDataReader reader, Dictionary<string, int> columns, string name)
    {
        object? value = GetValue(reader, columns, name);
        return value is null ? 0f : Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    private static bool GetBool(IDataReader reader, Dictionary<string, int> columns, string name)
    {
        return GetInt(reader, columns, name) != 0;
    }

    private static string GetString(IDataReader reader, Dictionary<string, int> columns, string name)
    {
        object? value = GetValue(reader, columns, name);
        return value is null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static Item[] DecodeInventory(string encoded)
    {
        Item[] items = new Item[NetItem.MaxInventory];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = new Item();
        }

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return items;
        }

        string[] parts = encoded.Split('~');
        int count = Math.Min(parts.Length, items.Length);
        for (int i = 0; i < count; i++)
        {
            NetItem netItem = NetItem.Parse(parts[i]);
            items[i] = netItem.NetId == 0 || netItem.Stack <= 0
                ? new Item()
                : NetItemToItem(netItem);
        }

        return items;
    }

    private static Item NetItemToItem(NetItem netItem)
    {
        Item item = new();
        item.netDefaults(netItem.NetId);
        item.stack = Math.Max(0, netItem.Stack);
        item.prefix = netItem.PrefixId;
        item.favorited = netItem.Favorited;
        return item;
    }

    private static Color DecodeColor(int? encodedColor, Color fallback)
    {
        if (!encodedColor.HasValue)
        {
            return fallback;
        }

        int value = encodedColor.Value;
        byte r = (byte)((value >> 16) & 0xff);
        byte g = (byte)((value >> 8) & 0xff);
        byte b = (byte)(value & 0xff);
        return new Color(r, g, b);
    }

    private static bool[] DecodeBoolArray(int? encodedBools, int minimumLength)
    {
        int value = encodedBools ?? 0;
        bool[] result = new bool[minimumLength];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (value & (1 << i)) != 0;
        }

        return result;
    }
}
