using System.Reflection;
using Dapper;

namespace Tapedeck.Data;

/// <summary>
/// Applies the numbered scripts from <c>Migrations/NNN_name.sql</c>.
/// The current version is stored in <c>PRAGMA user_version</c>.
/// </summary>
public sealed class Migrator(SqliteConnectionFactory connectionFactory)
{
    private const string RESOURCE_PREFIX = "Tapedeck.Data.Migrations.";

    public async Task<int> MigrateAsync()
    {
        await using var connection = await connectionFactory.OpenAsync();
        var current = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");

        foreach (var (version, resource) in Scripts().Where(s => s.Version > current))
        {
            var sql = ReadResource(resource);
            await using var transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(sql, transaction: transaction);
            // PRAGMA takes no parameters, but version is an int parsed from the resource name
            await connection.ExecuteAsync($"PRAGMA user_version = {version};", transaction: transaction);
            await transaction.CommitAsync();
            current = version;
        }

        return current;
    }

    public static int LatestVersion => Scripts().Max(s => s.Version);

    private static IEnumerable<(int Version, string Resource)> Scripts() =>
        typeof(Migrator).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(RESOURCE_PREFIX, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n => (Version: int.Parse(n[RESOURCE_PREFIX.Length..].Split('_')[0]), Resource: n))
            .OrderBy(s => s.Version);

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
