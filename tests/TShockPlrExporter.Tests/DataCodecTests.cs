using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using TShockAPI;
using TShockPlrExporter.Data;
using Xunit;

namespace TShockPlrExporter.Tests;

public class DataCodecTests
{
    private static readonly MethodInfo DecodeColorMethod = GetPrivateStaticMethod(typeof(CharacterDatabase), "DecodeColor");
    private static readonly MethodInfo DecodeBoolArrayMethod = GetPrivateStaticMethod(typeof(CharacterDatabase), "DecodeBoolArray");
    private static readonly MethodInfo DecodeInventoryMethod = GetPrivateStaticMethod(typeof(CharacterDatabase), "DecodeInventory");

    [Theory]
    [InlineData(0x00010203, 3, 2, 1)]
    [InlineData(0x00FF00FF, 255, 0, 255)]
    [InlineData(0x00000000, 0, 0, 0)]
    [InlineData(-1, 255, 255, 255)]
    public void DecodeColor_MatchesTShockLittleEndianPacking(int encoded, int red, int green, int blue)
    {
        object? result = DecodeColorMethod.Invoke(null, new object?[] { encoded, new Color(0, 0, 0) });
        Color color = Assert.IsType<Color>(result);

        Assert.Equal((byte)red, color.R);
        Assert.Equal((byte)green, color.G);
        Assert.Equal((byte)blue, color.B);
    }

    [Fact]
    public void DecodeBoolArray_MatchesTShockBitOrder()
    {
        object? result = DecodeBoolArrayMethod.Invoke(null, new object?[] { 0b1010, 10 });
        bool[] values = Assert.IsType<bool[]>(result);

        Assert.Equal(10, values.Length);
        Assert.False(values[0]);
        Assert.True(values[1]);
        Assert.False(values[2]);
        Assert.True(values[3]);
    }

    [Fact]
    public void DecodeInventory_EmptyStringCreatesAllSlots()
    {
        object? result = DecodeInventoryMethod.Invoke(null, new object?[] { string.Empty });
        Item[] items = Assert.IsType<Item[]>(result);

        Assert.Equal(NetItem.MaxInventory, items.Length);
        Assert.All(items, item => Assert.NotNull(item));
    }

    private static MethodInfo GetPrivateStaticMethod(Type type, string name)
    {
        return type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到私有静态方法 {type.Name}.{name}");
    }
}
