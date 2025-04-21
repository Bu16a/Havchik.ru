using Npgsql;
using NpgsqlTypes;

namespace Server;

public class DbService : IDbService
{
    private string _connectionString;

    public DbService(string connectionString = "")
    {
        if (connectionString == "")
            return;
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public void SetConnection(string connectionString)
    {
        if (connectionString == "")
            _connectionString = connectionString;
    }

    public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(
        string query,
        Dictionary<string, (object value, NpgsqlDbType dbType)> parameters)
    {
        var result = new List<Dictionary<string, object>>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(query, connection);

        foreach (var param in parameters)
        {
            command.Parameters.AddWithValue(param.Key, param.Value.dbType, param.Value.value);
        }

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var columnValue = reader.GetValue(i);
                row[columnName] = columnValue == DBNull.Value ? null : columnValue;
            }
            result.Add(row);
        }

        return result;
    }
}