using Microsoft.Xna.Framework;
using Terraria;

namespace TShockPlrExporter.Data;

/// <summary>tsCharacter 中一行 SSC 人物数据的内存表示。</summary>
internal sealed class CharacterRecord
{
    public int Account { get; init; }
    public int Health { get; init; }
    public int MaxHealth { get; init; }
    public int Mana { get; init; }
    public int MaxMana { get; init; }
    public Item[] Inventory { get; init; } = Array.Empty<Item>();
    public int ExtraSlot { get; init; }
    public int SpawnX { get; init; }
    public int SpawnY { get; init; }
    public int SkinVariant { get; init; }
    public int Hair { get; init; }
    public int HairDye { get; init; }
    public Color HairColor { get; init; }
    public Color PantsColor { get; init; }
    public Color ShirtColor { get; init; }
    public Color UnderShirtColor { get; init; }
    public Color ShoeColor { get; init; }
    public bool[] HideVisuals { get; init; } = Array.Empty<bool>();
    public Color SkinColor { get; init; }
    public Color EyeColor { get; init; }
    public int QuestsCompleted { get; init; }
    public bool UsingBiomeTorches { get; init; }
    public bool HappyFunTorchTime { get; init; }
    public bool UnlockedBiomeTorches { get; init; }
    public int CurrentLoadoutIndex { get; init; }
    public bool AteArtisanBread { get; init; }
    public bool UsedAegisCrystal { get; init; }
    public bool UsedAegisFruit { get; init; }
    public bool UsedArcaneCrystal { get; init; }
    public bool UsedGalaxyPearl { get; init; }
    public bool UsedGummyWorm { get; init; }
    public bool UsedAmbrosia { get; init; }
    public bool UnlockedSuperCart { get; init; }
    public bool EnabledSuperCart { get; init; }
    public int DeathsPve { get; init; }
    public int DeathsPvp { get; init; }
    public int VoiceVariant { get; init; }
    public float VoicePitchOffset { get; init; }
    public int Team { get; init; }
}