using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace Inquiry.Tests;

/// <summary>
/// Covers <c>ExecuteBatchAsync</c> — the multi-item write path generated batch helpers use.
/// Microsoft.Data.Sqlite reports <c>CanCreateBatch == false</c>, so these tests exercise the
/// sequential fallback (one connection, one command per item).
/// </summary>
public sealed class ExecuteBatchTests
{
    [Fact]
    public async Task ExecuteBatchSumsRowsAffectedAcrossItems()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString),
            Array.Empty<IInquiryCommandInterceptor>());

        var total = await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma") },
            static (target, item) => BindItem(target, item));

        Assert.Equal(3, total);
        Assert.Equal(3, await CountItemsAsync(keeper));
    }

    [Fact]
    public async Task EmptyListReturnsZeroWithoutOpeningConnection()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        var factory = new BatchTestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());

        var total = await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            Array.Empty<(int, string)>(),
            static (target, item) => BindItem(target, item));

        Assert.Equal(0, total);
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public async Task SequentialFallbackFiresInterceptorPerItem()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        var interceptor = new CountingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString),
            new IInquiryCommandInterceptor[] { interceptor });

        await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta"), (3, "Gamma") },
            static (target, item) => BindItem(target, item));

        Assert.Equal(3, interceptor.ExecutingCount);
        Assert.Equal(3, interceptor.ExecutedCount);
    }

    [Fact]
    public async Task DefaultCommandTimeoutAppliesToSequentialFallback()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        var interceptor = new CountingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new BatchTestConnectionFactory(connectionString),
            new IInquiryCommandInterceptor[] { interceptor },
            new InquiryOptions { DefaultCommandTimeout = TimeSpan.FromSeconds(9) });

        await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta") },
            static (target, item) => BindItem(target, item));

        Assert.Equal(new[] { 9, 9 }, interceptor.ObservedTimeouts);
    }

    [Fact]
    public async Task TransactedPipelineExecutesBatchWithinTransaction()
    {
        var (connectionString, keeper) = await CreateDatabaseAsync();
        await using var _ = keeper;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var pipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            Array.Empty<IInquiryCommandInterceptor>(),
            new BatchTestConnectionFactory(connectionString),
            options: null);

        var total = await pipeline.ExecuteBatchAsync(
            "INSERT INTO Items (Id, Name) VALUES (@id, @name)",
            new[] { (1, "Alpha"), (2, "Beta") },
            static (target, item) => BindItem(target, item));

        Assert.Equal(2, total);

        await transaction.CommitAsync();
        Assert.Equal(2, await CountItemsAsync(keeper));
    }

    private static void BindItem(InquiryParameterTarget target, (int Id, string Name) item)
    {
        var id = target.CreateParameter();
        id.ParameterName = "@id";
        id.Value = item.Id;
        target.AddParameter(id);

        var name = target.CreateParameter();
        name.ParameterName = "@name";
        name.Value = item.Name;
        target.AddParameter(name);
    }

    private static async Task<(string ConnectionString, SqliteConnection Keeper)> CreateDatabaseAsync()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryExecuteBatch_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };
        var connectionString = builder.ToString();

        var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var command = keeper.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        return (connectionString, keeper);
    }

    private static async Task<long> CountItemsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Items";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class CountingInterceptor : IInquiryCommandInterceptor
    {
        public int ExecutingCount { get; private set; }

        public int ExecutedCount { get; private set; }

        public List<int> ObservedTimeouts { get; } = new();

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            ExecutingCount++;
            ObservedTimeouts.Add(context.Command.CommandTimeout);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken cancellationToken = default)
        {
            ExecutedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BatchTestConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;
        private int _openCount;

        public BatchTestConnectionFactory(string connectionString) => _connectionString = connectionString;

        public int OpenCount => _openCount;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _openCount);
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
