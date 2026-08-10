using Microsoft.Data.Sqlite;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.IO;
using TShockAPI;

namespace TShockPlrExporter;

public sealed class PlrExporter
{
    private static readonly object SaveLock = new();

    public IReadOnlyList<ExportAccount> GetAllAccounts(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.ID, u.Username
            FROM Users u
            INNER JOIN tsCharacter c ON c.Account = u.ID
            ORDER BY u.ID;
            """;

        return ReadAccounts(command);
    }

    public IReadOnlyList<ExportAccount> FindAccounts(SqliteConnection connection, string target)
    {
        using SqliteCommand command = connection.CreateCommand();
        if (int.TryParse(target, out int id))
        {
            command.CommandText = """
                SELECT u.ID, u.Username
                FROM Users u
                INNER JOIN tsCharacter c ON c.Account = u.ID
                WHERE u.ID = $id;
                """;
            command.Parameters.AddWithValue("$id", id);
        }
        else
        {
            command.CommandText = """
                SELECT u.ID, u.Username
                FROM Users u
                INNER JOIN tsCharacter c ON c.Account = u.ID
                WHERE u.Username = $name COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$name", target);
        }

        return ReadAccounts(command);
    }

    public string Export(SqliteConnection connection, ExportAccount account, string outputDirectory)
    {
        CharacterRecord record = ReadCharacter(connection, account.Id)
            ?? throw new InvalidOperationException("Character row does not exist.");

        Player player = BuildPlayer(account.Name, record);
        string safeName = SanitizeFileName(account.Name);
        string path = Path.GetFullPath(Path.Combine(outputDirectory, $"{safeName}.plr"));

        if (File.Exists(path))
        {
            string backupPath = Path.Combine(outputDirectory, $"{safeName}.{DateTime.Now:yyyyMMddHHmmss}.plr.bak");
            File.Move(path, backupPath);
        }

        SavePlayerFile(player, path);

        if (!File.Exists(path))
        {
            throw new IOException($"Terraria did not create the player file at '{path}'.");
        }

        if (new FileInfo(path).Length == 0)
        {
            throw new IOException($"Terraria created an empty player file at '{path}'.");
        }

        return path;
    }

    private static IReadOnlyList<ExportAccount> ReadAccounts(SqliteCommand command)
    {
        List<ExportAccount> accounts = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            accounts.Add(new ExportAccount(reader.GetInt32(0), reader.GetString(1)));
        }

        return accounts;
    }

    private static CharacterRecord? ReadCharacter(SqliteConnection connection, int accountId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM tsCharacter WHERE Account = $account;";
        command.Parameters.AddWithValue("$account", accountId);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new CharacterRecord
        {
            Account = GetInt(reader, "Account"),
            Health = GetInt(reader, "Health"),
            MaxHealth = GetInt(reader, "MaxHealth"),
            Mana = GetInt(reader, "Mana"),
            MaxMana = GetInt(reader, "MaxMana"),
            Inventory = DecodeInventory(GetString(reader, "Inventory")),
            ExtraSlot = GetInt(reader, "extraSlot"),
            SpawnX = GetInt(reader, "spawnX"),
            SpawnY = GetInt(reader, "spawnY"),
            SkinVariant = GetInt(reader, "skinVariant"),
            Hair = GetInt(reader, "hair"),
            HairDye = GetInt(reader, "hairDye"),
            HairColor = DecodeColor(GetNullableInt(reader, "hairColor"), new Color(215, 90, 55)),
            PantsColor = DecodeColor(GetNullableInt(reader, "pantsColor"), new Color(255, 230, 175)),
            ShirtColor = DecodeColor(GetNullableInt(reader, "shirtColor"), new Color(175, 165, 140)),
            UnderShirtColor = DecodeColor(GetNullableInt(reader, "underShirtColor"), new Color(160, 180, 215)),
            ShoeColor = DecodeColor(GetNullableInt(reader, "shoeColor"), new Color(160, 105, 60)),
            HideVisuals = DecodeBoolArray(GetNullableInt(reader, "hideVisuals"), 10),
            SkinColor = DecodeColor(GetNullableInt(reader, "skinColor"), new Color(255, 125, 90)),
            EyeColor = DecodeColor(GetNullableInt(reader, "eyeColor"), new Color(105, 90, 75)),
            QuestsCompleted = GetInt(reader, "questsCompleted"),
            UsingBiomeTorches = GetBool(reader, "usingBiomeTorches"),
            HappyFunTorchTime = GetBool(reader, "happyFunTorchTime"),
            UnlockedBiomeTorches = GetBool(reader, "unlockedBiomeTorches"),
            CurrentLoadoutIndex = GetInt(reader, "currentLoadoutIndex"),
            AteArtisanBread = GetBool(reader, "ateArtisanBread"),
            UsedAegisCrystal = GetBool(reader, "usedAegisCrystal"),
            UsedAegisFruit = GetBool(reader, "usedAegisFruit"),
            UsedArcaneCrystal = GetBool(reader, "usedArcaneCrystal"),
            UsedGalaxyPearl = GetBool(reader, "usedGalaxyPearl"),
            UsedGummyWorm = GetBool(reader, "usedGummyWorm"),
            UsedAmbrosia = GetBool(reader, "usedAmbrosia"),
            UnlockedSuperCart = GetBool(reader, "unlockedSuperCart"),
            EnabledSuperCart = GetBool(reader, "enabledSuperCart"),
            DeathsPve = GetInt(reader, "deathsPVE"),
            DeathsPvp = GetInt(reader, "deathsPVP"),
            VoiceVariant = GetInt(reader, "voiceVariant"),
            VoicePitchOffset = GetFloat(reader, "voicePitchOffset"),
            Team = GetInt(reader, "team")
        };
    }

    private static Player BuildPlayer(string accountName, CharacterRecord record)
    {
        Player player = new()
        {
            name = accountName,
            statLife = Math.Max(1, record.Health),
            statLifeMax = Math.Max(100, record.MaxHealth),
            statMana = Math.Max(0, record.Mana),
            statManaMax = Math.Max(0, record.MaxMana),
            SpawnX = record.SpawnX,
            SpawnY = record.SpawnY,
            skinVariant = record.SkinVariant,
            hair = record.Hair,
            hairDye = ClampByte(record.HairDye),
            hairColor = record.HairColor,
            pantsColor = record.PantsColor,
            shirtColor = record.ShirtColor,
            underShirtColor = record.UnderShirtColor,
            shoeColor = record.ShoeColor,
            skinColor = record.SkinColor,
            eyeColor = record.EyeColor,
            anglerQuestsFinished = record.QuestsCompleted,
            UsingBiomeTorches = record.UsingBiomeTorches,
            happyFunTorchTime = record.HappyFunTorchTime,
            unlockedBiomeTorches = record.UnlockedBiomeTorches,
            CurrentLoadoutIndex = record.CurrentLoadoutIndex,
            ateArtisanBread = record.AteArtisanBread,
            usedAegisCrystal = record.UsedAegisCrystal,
            usedAegisFruit = record.UsedAegisFruit,
            usedArcaneCrystal = record.UsedArcaneCrystal,
            usedGalaxyPearl = record.UsedGalaxyPearl,
            usedGummyWorm = record.UsedGummyWorm,
            usedAmbrosia = record.UsedAmbrosia,
            unlockedSuperCart = record.UnlockedSuperCart,
            enabledSuperCart = record.EnabledSuperCart,
            numberOfDeathsPVE = record.DeathsPve,
            numberOfDeathsPVP = record.DeathsPvp,
            voiceVariant = record.VoiceVariant,
            voicePitchOffset = record.VoicePitchOffset,
            team = record.Team
        };

        player.extraAccessory = record.ExtraSlot != 0;
        RestoreInventory(record.Inventory, player);
        CopyHideVisuals(record.HideVisuals, player.hideVisibleAccessory);
        return player;
    }

    private static void SavePlayerFile(Player player, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        PlayerFileData data = new(path, cloudSave: false)
        {
            Metadata = FileMetadata.FromCurrentSettings(FileType.Player),
            Player = player
        };

        lock (SaveLock)
        {
            bool serverSideCharacter = Main.ServerSideCharacter;
            try
            {
                Main.ServerSideCharacter = false;
                Player.SavePlayer(data, skipMapSave: true);
            }
            finally
            {
                Main.ServerSideCharacter = serverSideCharacter;
            }
        }
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

    private static byte ClampByte(int value)
    {
        return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
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

    private static string SanitizeFileName(string name)
    {
        string sanitized = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "player" : sanitized;
    }

    private static int GetInt(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private static int? GetNullableInt(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static float GetFloat(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0f : reader.GetFloat(ordinal);
    }

    private static bool GetBool(SqliteDataReader reader, string name)
    {
        return GetInt(reader, name) != 0;
    }

    private static string GetString(SqliteDataReader reader, string name)
    {
        int ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }
}