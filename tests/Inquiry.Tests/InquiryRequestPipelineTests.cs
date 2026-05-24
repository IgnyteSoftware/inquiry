using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Parameters;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;

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
            new InquiryCommand(
                "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
                new[]
                {
                    new InquiryParameter("Id", 1),
                    new InquiryParameter("Name", "Alpha"),
                    new InquiryParameter("IsActive", 1),
                }),
            cancellationTokenSource.Token);

        var selected = await pipeline.QuerySingleOrDefaultAsync(
            new InquiryCommand(
                "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
                new[] { new InquiryParameter("Id", 1) }),
            MaterializeItem,
            cancellationTokenSource.Token);

        var missing = await pipeline.QuerySingleOrDefaultAsync(
            new InquiryCommand(
                "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
                new[] { new InquiryParameter("Id", 404) }),
            MaterializeItem,
            cancellationTokenSource.Token);

        var updated = await pipeline.ExecuteAsync(
            new InquiryCommand(
                "UPDATE Items SET Name = @Name WHERE Id = @Id",
                new[]
                {
                    new InquiryParameter("Name", "Beta"),
                    new InquiryParameter("Id", 1),
                }),
            cancellationTokenSource.Token);

        var deleted = await pipeline.ExecuteAsync(
            new InquiryCommand(
                "DELETE FROM Items WHERE Id = @Id",
                new[] { new InquiryParameter("Id", 1) }),
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

        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)"));
        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id, Name, IsActive) VALUES (2, 'Beta', 1)"));

        await foreach (var item in pipeline.QueryAsync(
            new InquiryCommand("SELECT Id, Name, IsActive FROM Items ORDER BY Id"),
            MaterializeItem))
        {
            Assert.Equal("Alpha", item.Name);
            break;
        }

        var updated = await pipeline.ExecuteAsync(new InquiryCommand("UPDATE Items SET Name = 'Gamma' WHERE Id = 2"));

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

        var inserted = await pipeline.ExecuteAsync(new InquiryCommand(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            new[]
            {
                new InquiryParameter("Id", 1),
                new InquiryParameter("Name", "Alpha"),
                new InquiryParameter("IsActive", 1),
            }));

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO MissingTable (Id) VALUES (1)")));

        Assert.Equal(1, inserted);
        Assert.Equal("INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)", interceptor.InitializedCommandTexts[0]);
        Assert.Equal(3, interceptor.InitializedParameterCounts[0]);
        Assert.Equal(7, interceptor.ExecutingTimeouts[0]);
        Assert.Contains(1, interceptor.ExecutedRecordsAffected);
        Assert.Same(exception, interceptor.Failures.Single());
    }

    [Fact]
    public void AddInquiryRegistersPipeline()
    {
        using var services = new ServiceCollection()
            .AddSingleton<IInquiryConnectionFactory>(new TestConnectionFactory("Data Source=:memory:"))
            .AddSingleton<IInquiryEntityMaterializer<TestItem>, TestItemMaterializer>()
            .AddInquiry()
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
            .AddInquiry()
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

    [Fact]
    public async Task InquiryFacadeBindsAnonymousObjectAndDictionaryParameters()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        using var services = new ServiceCollection()
            .AddSingleton<IInquiryConnectionFactory>(new TestConnectionFactory(connectionString))
            .AddSingleton<IInquiryEntityMaterializer<TestItem>, TestItemMaterializer>()
            .AddInquiry()
            .BuildServiceProvider();

        var inquiry = services.GetRequiredService<IInquiry>();

        var inserted = await inquiry.ExecuteAsync(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            new { Id = 1, Name = "Alpha", IsActive = 1 });

        var selected = await inquiry.QuerySingleOrDefaultAsync<TestItem>(
            "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
            new Dictionary<string, object?> { ["Id"] = 1 });

        var updated = await inquiry.ExecuteAsync(
            "UPDATE Items SET Name = @Name WHERE Id = @Id",
            new Dictionary<string, object?> { ["Name"] = "Beta", ["Id"] = 1 });

        var streamed = await ToListAsync(inquiry.QueryAsync<TestItem>(
            "SELECT Id, Name, IsActive FROM Items WHERE Name = @Name",
            new { Name = "Beta" }));

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Alpha", selected.Name);
        Assert.Equal(1, updated);
        Assert.Single(streamed);
        Assert.Equal("Beta", streamed[0].Name);
    }

    [Fact]
    public async Task CommandDefinitionBindsInquiryParameterCollection()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var interceptor = new RecordingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            new[] { interceptor });

        var inserted = await pipeline.ExecuteAsync(new InquiryCommand(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            new[]
            {
                new InquiryParameter("Id", 1),
                new InquiryParameter("Name", "Alpha", DbType.String, size: 50),
                new InquiryParameter("IsActive", 1),
            }));

        var selected = await pipeline.QuerySingleOrDefaultAsync(
            new InquiryCommand(
                "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
                new[] { new InquiryParameter("Id", 1) }),
            MaterializeItem);

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Alpha", selected.Name);
        Assert.Equal(3, interceptor.InitializedParameterCounts[0]);
        Assert.Contains("@Name", interceptor.InitializedParameterNames[0]);
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

        public List<string[]> InitializedParameterNames { get; } = new();

        public List<int> ExecutingTimeouts { get; } = new();

        public List<int?> ExecutedRecordsAffected { get; } = new();

        public List<Exception> Failures { get; } = new();

        public ValueTask CommandInitializedAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
        {
            InitializedCommandTexts.Add(context.Command.CommandText);
            InitializedParameterCounts.Add(context.Command.Parameters.Count);
            InitializedParameterNames.Add(context.Command.Parameters.Cast<DbParameter>().Select(static parameter => parameter.ParameterName).ToArray());
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
