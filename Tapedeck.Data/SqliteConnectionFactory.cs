using Dapper;
using Microsoft.Data.Sqlite;

namespace Tapedeck.Data;

public sealed class SqliteConnectionFactory(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        ForeignKeys = true,
    }.ToString();

    static SqliteConnectionFactory()
    {
        // map snake_case columns to PascalCase properties
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;");
        return connection;
    }
}
