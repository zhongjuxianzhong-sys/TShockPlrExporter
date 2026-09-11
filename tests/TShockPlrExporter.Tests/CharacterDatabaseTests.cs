using Microsoft.Data.Sqlite;
using TShockPlrExporter.Data;
using Xunit;

namespace TShockPlrExporter.Tests;

public class CharacterDatabaseTests
{
    [Fact]
    public void ResolveSqliteDatabasePath_UsesConfiguredRelativePath()
    {
        using TempDirectory temp = new();
        string expected = Path.Combine(temp.Path, "custom.sqlite");
        File.WriteAllText(expected, string.Empty);

        string actual = CharacterDatabase.ResolveSqliteDatabasePath(temp.Path, "custom.sqlite");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildSqliteConnectionString_UsesConnectionStringDataSource()
    {
        using TempDirectory temp = new();
        string expected = Path.Combine(temp.Path, "from-connection-string.sqlite");
        File.WriteAllText(expected, string.Empty);

        string connectionString = CharacterDatabase.BuildSqliteConnectionString(
            temp.Path,
            "Data Source=from-connection-string.sqlite",
            "ignored.sqlite");
        SqliteConnectionStringBuilder builder = new(connectionString);

        Assert.Equal(expected, builder.DataSource);
        Assert.Equal(SqliteOpenMode.ReadOnly, builder.Mode);
    }

    [Fact]
    public void BuildSqliteConnectionString_FallsBackToConfiguredPath()
    {
        using TempDirectory temp = new();
        string expected = Path.Combine(temp.Path, "from-path.sqlite");
        File.WriteAllText(expected, string.Empty);

        string connectionString = CharacterDatabase.BuildSqliteConnectionString(
            temp.Path,
            configuredConnectionString: null,
            configuredPath: "from-path.sqlite");
        SqliteConnectionStringBuilder builder = new(connectionString);

        Assert.Equal(expected, builder.DataSource);
        Assert.Equal(SqliteOpenMode.ReadOnly, builder.Mode);
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
