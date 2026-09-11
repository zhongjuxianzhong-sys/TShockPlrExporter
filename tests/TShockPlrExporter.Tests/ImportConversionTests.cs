using TShockPlrExporter.Importing;
using Xunit;

namespace TShockPlrExporter.Tests;

public class ImportConversionTests
{
    [Fact]
    public void ClampForImport_ClampsValueAndRecordsWarning()
    {
        List<string> warnings = new();

        int actual = PlrImporter.ClampForImport("hair", 500, 0, 9, warnings);

        Assert.Equal(9, actual);
        string warning = Assert.Single(warnings);
        Assert.Contains("hair", warning);
        Assert.Contains("500", warning);
        Assert.Contains("9", warning);
    }

    [Fact]
    public void ClampForImport_LeavesValidValueUnchanged()
    {
        List<string> warnings = new();

        int actual = PlrImporter.ClampForImport("team", 2, 0, 5, warnings);

        Assert.Equal(2, actual);
        Assert.Empty(warnings);
    }
}
