using System.Reflection;
using TShockPlrExporter.Exporting;
using Xunit;

namespace TShockPlrExporter.Tests;

public class FileNameTests
{
    private static readonly MethodInfo SanitizeFileNameMethod =
        typeof(PlrExporter).GetMethod("SanitizeFileName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("找不到 PlrExporter.SanitizeFileName");

    [Theory]
    [InlineData("Alice", "Alice")]
    [InlineData("a/b", "a_b")]
    [InlineData("CON", "_CON")]
    [InlineData("..", "player")]
    [InlineData("", "player")]
    [InlineData("  ", "player")]
    public void SanitizeFileName_ReturnsSafeName(string input, string expected)
    {
        string actual = Invoke(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SanitizeFileName_TruncatesLongNames()
    {
        string actual = Invoke(new string('a', 100));

        Assert.Equal(64, actual.Length);
    }

    private static string Invoke(string input)
    {
        return (string)SanitizeFileNameMethod.Invoke(null, new object?[] { input })!;
    }
}
