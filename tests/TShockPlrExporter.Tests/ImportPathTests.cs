using TShockPlrExporter.Importing;
using Xunit;

namespace TShockPlrExporter.Tests;

public class ImportPathTests
{
    [Fact]
    public void ResolveInputPath_AcceptsFileNameInsideImportDirectory()
    {
        using TempDirectory temp = new();
        string expected = Path.Combine(temp.Path, "Alice.plr");
        File.WriteAllText(expected, "not empty");

        string actual = PlrImporter.ResolveInputPath(temp.Path, "Alice.plr");

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("../Alice.plr")]
    [InlineData("..\\Alice.plr")]
    [InlineData("nested/Alice.plr")]
    [InlineData("C:\\Temp\\Alice.plr")]
    public void ResolveInputPath_RejectsPathTraversalAndAbsolutePaths(string fileName)
    {
        using TempDirectory temp = new();

        Assert.Throws<InvalidOperationException>(() => PlrImporter.ResolveInputPath(temp.Path, fileName));
    }

    [Fact]
    public void ResolveInputPath_RejectsMissingPlrExtension()
    {
        using TempDirectory temp = new();

        Assert.Throws<InvalidOperationException>(() => PlrImporter.ResolveInputPath(temp.Path, "Alice.dat"));
    }

    [Fact]
    public void ResolveInputPath_RejectsMissingFile()
    {
        using TempDirectory temp = new();

        Assert.Throws<FileNotFoundException>(() => PlrImporter.ResolveInputPath(temp.Path, "missing.plr"));
    }

    [Fact]
    public void ResolveInputPath_RejectsEmptyFile()
    {
        using TempDirectory temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "empty.plr"), string.Empty);

        Assert.Throws<InvalidDataException>(() => PlrImporter.ResolveInputPath(temp.Path, "empty.plr"));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"TShockPlrExporter-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
