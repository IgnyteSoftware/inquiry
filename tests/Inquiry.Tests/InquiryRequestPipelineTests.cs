using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests;

public sealed class InquiryRequestPipelineTests
{
    [Fact]
    public async Task PipelineExecutesQueriesAndNonQueriesAgainstSqlite()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var factory = new TestConnectionFactory(connectionString);
        var pipeline = new InquiryRequestPipeline(factory, Array.Empty<IInquiryCommandInterceptor>());
        using var cancellationTokenSource = new CancellationTokenSource();

        var inserted = await pipeline.ExecuteAsync(
            new InquiryCommandDefinition(
                "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
                command =>
                {
                    AddParameter(command, "@Id", 1);
                    AddParameter(command, "@Name", "Alpha");
                    AddParameter(command, "@IsActive", 1);
                }),
            cancellationTokenSource.Token);

        var selected = await pipeline.QuerySingleOrDefaultAsync(
            new InquiryCommandDefinition(
                "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
                command => AddParameter(command, "@Id", 1)),
            MaterializeItem,
            cancellationTokenSource.Token);

        var missing = await pipeline.QuerySingleOrDefaultAsync(
            new InquiryCommandDefinition(
                "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
                command => AddParameter(command, "@Id", 404)),
            MaterializeItem,
            cancellationTokenSource.Token);

        var updated = await pipeline.ExecuteAsync(
            new InquiryCommandDefinition(
                "UPDATE Items SET Name = @Name WHERE Id = @Id",
                command =>
                {
                    AddParameter(command, "@Name", "Beta");
                    AddParameter(command, "@Id", 1);
                }),
            cancellationTokenSource.Token);

        var deleted = await pipeline.ExecuteAsync(
            new InquiryCommandDefinition(
                "DELETE FROM Items WHERE Id = @Id",
                command => AddParameter(command, "@Id", 1)),
            cancellationTokenSource.Token);

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Alpha", selected.Name);
        Assert.Null(missing);
        Assert.Equal(1, updated);
        Assert.Equal(1, deleted);
        Assert.Equal(cancellationTokenSource.Token, factory.LastCancellationToken);
    }

    [Fact]
    public async Task QueryAsyncDisposesResourcesWhenEnumerationStopsEarly()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            Array.Empty<IInquiryCommandInterceptor>());

        await pipeline.ExecuteAsync(new InquiryCommandDefinition("INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)"));
        await pipeline.ExecuteAsync(new InquiryCommandDefinition("INSERT INTO Items (Id, Name, IsActive) VALUES (2, 'Beta', 1)"));

        await foreach (var item in pipeline.QueryAsync(
            new InquiryCommandDefinition("SELECT Id, Name, IsActive FROM Items ORDER BY Id"),
            MaterializeItem))
        {
            Assert.Equal("Alpha", item.Name);
            break;
        }

        var updated = await pipeline.ExecuteAsync(new InquiryCommandDefinition("UPDATE Items SET Name = 'Gamma' WHERE Id = 2"));

        Assert.Equal(1, updated);
    }

    [Fact]
    public async Task InterceptorsObserveMutateAndReceiveFailures()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var interceptor = new RecordingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            new[] { interceptor });

        var inserted = await pipeline.ExecuteAsync(new InquiryCommandDefinition(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            command =>
            {
                AddParameter(command, "@Id", 1);
                AddParameter(command, "@Name", "Alpha");
                AddParameter(command, "@IsActive", 1);
            }));

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            pipeline.ExecuteAsync(new InquiryCommandDefinition("INSERT INTO MissingTable (Id) VALUES (1)")));

        Assert.Equal(1, inserted);
        Assert.Equal("INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)", interceptor.InitializedCommandTexts[0]);
        Assert.Equal(3, interceptor.InitializedParameterCounts[0]);
        Assert.Equal(7, interceptor.ExecutingTimeouts[0]);
        Assert.Contains(1, interceptor.ExecutedRecordsAffected);
        Assert.Same(exception, interceptor.Failures.Single());
    }

    [Fact]
    public void AddInquiryCoreRegistersPipeline()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IInquiryConnectionFactory>(new TestConnectionFactory("Data Source=:memory:"))
            .AddSingleton<IInquiryEntityMaterializer<TestItem>, TestItemMaterializer>()
            .AddInquiryCore()
            .BuildServiceProvider();

        Assert.IsType<InquiryRequestPipeline>(services.GetRequiredService<IInquiryRequestPipeline>());
        Assert.IsType<DefaultInquiry>(services.GetRequiredService<IInquiry>());
    }

    [Fact]
    public async Task InquiryFacadeQueriesMappedEntitiesThroughPipeline()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        using var services = new ServiceCollection()
            .AddSingleton<IInquiryConnectionFactory>(new TestConnectionFactory(connectionString))
            .AddSingleton<IInquiryEntityMaterializer<TestItem>, TestItemMaterializer>()
            .AddInquiryCore()
            .BuildServiceProvider();

        var inquiry = services.GetRequiredService<IInquiry>();

        await inquiry.ExecuteAsync("INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)");

        var items = await ToListAsync(inquiry.QueryAsync<TestItem>("SELECT Id, Name, IsActive FROM Items"));
        var selected = await inquiry.QuerySingleOrDefaultAsync<TestItem>("SELECT Id, Name, IsActive FROM Items WHERE Id = 1");

        Assert.Single(items);
        Assert.Equal("Alpha", items[0].Name);
        Assert.NotNull(selected);
        Assert.Equal("Alpha", selected.Name);
    }

    private static string CreateSharedInMemoryConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryPipeline_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        };

        return builder.ToString();
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Items (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static TestItem MaterializeItem(DbDataReader reader)
    {
        return new TestItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2) == 1);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source)
        {
            items.Add(item);
        }

        return items;
    }

    private sealed class TestConnectionFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;

        public TestConnectionFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public CancellationToken LastCancellationToken { get; private set; }

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }

    private sealed class RecordingInterceptor : IInquiryCommandInterceptor
    {
        public List<string> InitializedCommandTexts { get; } = new();

        public List<int> InitializedParameterCounts { get; } = new();

        public List<int> ExecutingTimeouts { get; } = new();

        public List<int?> ExecutedRecordsAffected { get; } = new();

        public List<Exception> Failures { get; } = new();

        public ValueTask CommandInitializedAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            InitializedCommandTexts.Add(context.Command.CommandText);
            InitializedParameterCounts.Add(context.Command.Parameters.Count);
            context.Command.CommandTimeout = 7;
            return ValueTask.CompletedTask;
        }

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            ExecutingTimeouts.Add(context.Command.CommandTimeout);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommandExecutedAsync(InquiryCommandExecutedContext context, CancellationToken cancellationToken = default)
        {
            ExecutedRecordsAffected.Add(context.RecordsAffected);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommandFailedAsync(InquiryCommandFailedContext context, CancellationToken cancellationToken = default)
        {
            Failures.Add(context.Exception);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record TestItem(int Id, string Name, bool IsActive);

    private sealed class TestItemMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public TestItem Materialize(DbDataReader reader)
        {
            return MaterializeItem(reader);
        }
    }
}
