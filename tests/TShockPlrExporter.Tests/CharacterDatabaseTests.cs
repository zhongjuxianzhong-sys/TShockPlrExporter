using System.Reflection;
using Microsoft.Data.Sqlite;
using Npgsql;
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

    [Fact]
    public void BuildPostgresConnectionString_PrefersConfiguredConnectionString()
    {
        string connectionString = CharacterDatabase.BuildPostgresConnectionString(
            "Host=pg.example.com;Port=5433;Database=tshock;Username=admin;Password=secret",
            host: "ignored:5432",
            database: "ignored",
            username: "ignored",
            password: "ignored");
        NpgsqlConnectionStringBuilder builder = new(connectionString);

        Assert.Equal("pg.example.com", builder.Host);
        Assert.Equal(5433, builder.Port);
        Assert.Equal("tshock", builder.Database);
        Assert.Equal("admin", builder.Username);
    }

    [Fact]
    public void BuildPostgresConnectionString_UsesHostPortAndDefaults()
    {
        NpgsqlConnectionStringBuilder withPort = new(CharacterDatabase.BuildPostgresConnectionString(
            configuredConnectionString: null,
            host: "10.0.0.5:6543",
            database: "tshock",
            username: "tshock",
            password: "secret"));
        NpgsqlConnectionStringBuilder withoutPort = new(CharacterDatabase.BuildPostgresConnectionString(
            configuredConnectionString: null,
            host: "10.0.0.5",
            database: "tshock",
            username: "tshock",
            password: "secret"));

        Assert.Equal("10.0.0.5", withPort.Host);
        Assert.Equal(6543, withPort.Port);
        Assert.Equal(5432, withoutPort.Port);
    }

    [Fact]
    public void BuildPostgresConnectionString_RejectsEmptyHost()
    {
        Assert.Throws<InvalidOperationException>(() => CharacterDatabase.BuildPostgresConnectionString(
            configuredConnectionString: null,
            host: "  ",
            database: "tshock",
            username: "tshock",
            password: "secret"));
    }

    [Fact]
    public void QuoteIdentifier_QuotesOnlyForPostgres()
    {
        Assert.Equal("\"Users\"", CharacterDatabase.QuoteIdentifier("Users", StorageBackend.Postgres));
        Assert.Equal("\"tsCharacter\"", CharacterDatabase.QuoteIdentifier("tsCharacter", StorageBackend.Postgres));
        Assert.Equal("Users", CharacterDatabase.QuoteIdentifier("Users", StorageBackend.Sqlite));
        Assert.Equal("tsCharacter", CharacterDatabase.QuoteIdentifier("tsCharacter", StorageBackend.MySql));
    }

    /// <summary>
    /// TShock 在 PostgreSQL 上以双引号建表，插件查询必须带上同样的引号，
    /// 否则表名会被折叠成小写而找不到表。
    /// </summary>
    [Fact]
    public void Sql_QuotesTableNamesForPostgres()
    {
        using NpgsqlConnection connection = new("Host=localhost;Database=tshock;Username=probe;Password=probe");
        using CharacterDatabase database = CreateDatabase(connection, StorageBackend.Postgres);
        string sql = string.Join("\n", ReadSql(database));

        Assert.Contains("FROM \"Users\" u", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN \"tsCharacter\" c", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO \"tsCharacter\"", sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE \"tsCharacter\" SET", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_LeavesTableNamesUnquotedForSqlite()
    {
        AssertTableNamesUnquoted(StorageBackend.Sqlite);
    }

    [Fact]
    public void Sql_LeavesTableNamesUnquotedForMySql()
    {
        AssertTableNamesUnquoted(StorageBackend.MySql);
    }

    private static void AssertTableNamesUnquoted(StorageBackend backend)
    {
        using NpgsqlConnection connection = new("Host=localhost;Database=tshock;Username=probe;Password=probe");
        using CharacterDatabase database = CreateDatabase(connection, backend);
        string sql = string.Join("\n", ReadSql(database));

        Assert.Contains("FROM Users u", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN tsCharacter c", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO tsCharacter", sql, StringComparison.Ordinal);
        Assert.Contains("UPDATE tsCharacter SET", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Users\"", sql, StringComparison.Ordinal);
    }

    private static CharacterDatabase CreateDatabase(System.Data.IDbConnection connection, StorageBackend backend)
    {
        ConstructorInfo constructor = typeof(CharacterDatabase).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(System.Data.IDbConnection), typeof(StorageBackend) },
            modifiers: null) ?? throw new InvalidOperationException("找不到 CharacterDatabase 的私有构造函数");

        return (CharacterDatabase)constructor.Invoke(new object[] { connection, backend });
    }

    private static IEnumerable<string> ReadSql(CharacterDatabase database)
    {
        string[] names =
        {
            "accountSelect", "sscAccountSelect", "insertCharacterSql", "updateCharacterSql"
        };

        foreach (string name in names)
        {
            FieldInfo field = typeof(CharacterDatabase).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"找不到 CharacterDatabase.{name}");

            yield return (string)field.GetValue(database)!;
        }
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
