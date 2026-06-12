using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Inquiry.Tests;

/// <summary>
/// Covers the streaming <c>QueryAsync&lt;T, TArgs, TMaterializer&gt;</c> fast path — the
/// allocation-free overload generated stores use for streaming filtered selects.
/// </summary>
public sealed class StreamingFastPathTests
{
    [Fact]
    public async Task StreamingTArgsOverloadBindsAndStreamsRows()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryStreamFast_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var connectionString = builder.ToString();

        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var command = keeper.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, IsActive INTEGER NOT NULL);
                INSERT INTO Items VALUES (1, 'Alpha', 1), (2, 'Beta', 0), (3, 'Gamma', 1);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var pipeline = new InquiryRequestPipeline(
            new FastPathConnectionFactory(connectionString),
            Array.Empty<IInquiryCommandInterceptor>());

        var names = new List<string>();
        await foreach (var item in pipeline.QueryAsync<Row, int, RowMaterializer>(
            "SELECT Id, Name FROM Items WHERE IsActive = @active ORDER BY Id",
            1,
            static (cmd, active) =>
            {
                var p = cmd.CreateParameter();
                p.ParameterName = "@active";
                p.Value = active;
                cmd.Parameters.Add(p);
            },
            default))
        {
            names.Add(item.Name);
        }

        Assert.Equal(new[] { "Alpha", "Gamma" }, names);
    }

    private sealed class Row
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private readonly struct RowMaterializer : IInquiryEntityMaterializer<Row>
    {
        public Row Materialize(DbDataReader reader) => new()
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
        };
    }

    private sealed class FastPathConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;

        public FastPathConnectionFactory(string connectionString) => _connectionString = connectionString;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
