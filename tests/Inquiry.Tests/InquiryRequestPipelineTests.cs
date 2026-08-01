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
    public async Task ExecuteScalarAssociatesProviderCancellationWithCallerToken()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        var interceptor = new ProviderCancellationInterceptor(cancellationTokenSource);
        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            new[] { interceptor });

        var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ExecuteScalarAsync<int>(new InquiryCommand("SELECT 1"), cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, cancellation.CancellationToken);
        var reported = Assert.Single(interceptor.Failures);
        Assert.Same(cancellation, reported);
    }

    [Fact]
    public async Task ExecuteScalarFastPathAssociatesProviderCancellationWithCallerToken()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        var interceptor = new ProviderCancellationInterceptor(cancellationTokenSource);
        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            new[] { interceptor });

        var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ExecuteScalarAsync<int, int>(
                "SELECT 1",
                0,
                static (_, _) => { },
                cancellationTokenSource.Token));

        AssertCallerCancellation(cancellationTokenSource.Token, cancellation, interceptor);
    }

    [Fact]
    public async Task TransactedExecuteScalarAssociatesProviderCancellationWithCallerToken()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        var factory = new TestConnectionFactory(connectionString);
        await using var connection = await factory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        var interceptor = new ProviderCancellationInterceptor(cancellationTokenSource);
        var pipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            new[] { interceptor },
            factory,
            options: null);

        var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ExecuteScalarAsync<int>(new InquiryCommand("SELECT 1"), cancellationTokenSource.Token));

        AssertCallerCancellation(cancellationTokenSource.Token, cancellation, interceptor);
    }

    [Fact]
    public async Task TransactedExecuteScalarFastPathAssociatesProviderCancellationWithCallerToken()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        var factory = new TestConnectionFactory(connectionString);
        await using var connection = await factory.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        using var cancellationTokenSource = new CancellationTokenSource();
        var interceptor = new ProviderCancellationInterceptor(cancellationTokenSource);
        var pipeline = new TransactedInquiryRequestPipeline(
            connection,
            transaction,
            new[] { interceptor },
            factory,
            options: null);

        var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ExecuteScalarAsync<int, int>(
                "SELECT 1",
                0,
                static (_, _) => { },
                cancellationTokenSource.Token));

        AssertCallerCancellation(cancellationTokenSource.Token, cancellation, interceptor);
    }

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
            MaterializerInstance,
            cancellationTokenSource.Token);

        var missing = await pipeline.QuerySingleOrDefaultAsync(
            new InquiryCommand(
                "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
                new[] { new InquiryParameter("Id", 404) }),
            MaterializerInstance,
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
            MaterializerInstance))
        {
            Assert.Equal("Alpha", item.Name);
            break;
        }

        var updated = await pipeline.ExecuteAsync(new InquiryCommand("UPDATE Items SET Name = 'Gamma' WHERE Id = 2"));

        Assert.Equal(1, updated);
    }

    [Fact]
    public async Task QueryAsyncDoesNotReportFailureWhenEnumerationStopsEarly()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var interceptor = new RecordingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            new[] { interceptor });

        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)"));
        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id, Name, IsActive) VALUES (2, 'Beta', 1)"));

        await foreach (var _ in pipeline.QueryAsync(
            new InquiryCommand("SELECT Id, Name, IsActive FROM Items ORDER BY Id"),
            MaterializerInstance))
        {
            break;
        }

        Assert.Empty(interceptor.Failures);
    }

    [Fact]
    public async Task QueryAsyncReportsMaterializerFailures()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var interceptor = new RecordingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            new[] { interceptor });

        await pipeline.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in pipeline.QueryAsync<TestItem>(
                new InquiryCommand("SELECT Id, Name, IsActive FROM Items"),
                new ThrowingMaterializer()))
            {
            }
        });

        Assert.Same(exception, interceptor.Failures.Single());
    }

    [Fact]
    public async Task QueryAsyncReportsInterceptorSetupFailures()
    {
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var interceptor = new ThrowingExecutingInterceptor();
        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            new[] { interceptor });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in pipeline.QueryAsync(
                new InquiryCommand("SELECT Id, Name, IsActive FROM Items"),
                MaterializerInstance))
            {
            }
        });

        Assert.Same(exception, interceptor.Failures.Single());
    }

    [Fact]
    public async Task QuerySingleOrDefaultThrowsWhenMultipleRowsAreReturned()
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.QuerySingleOrDefaultAsync(
                new InquiryCommand("SELECT Id, Name, IsActive FROM Items ORDER BY Id"),
                MaterializerInstance));
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

        await inquiry.ExecuteAsync($"INSERT INTO Items (Id, Name, IsActive) VALUES (1, 'Alpha', 1)");

        var items = await ToListAsync(inquiry.QueryAsync<TestItem>($"SELECT Id, Name, IsActive FROM Items"));
        var selected = await inquiry.QuerySingleOrDefaultAsync<TestItem>($"SELECT Id, Name, IsActive FROM Items WHERE Id = 1");

        Assert.Single(items);
        Assert.Equal("Alpha", items[0].Name);
        Assert.NotNull(selected);
        Assert.Equal("Alpha", selected.Name);
    }

    [Fact]
    public async Task InquiryFacadeParameterizesInterpolatedSql()
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

        var id = 1;
        var insertedName = "Alpha";
        var active = 1;
        var inserted = await inquiry.ExecuteAsync(
            $"INSERT INTO Items (Id, Name, IsActive) VALUES ({id}, {insertedName}, {active})");

        var selected = await inquiry.QuerySingleOrDefaultAsync<TestItem>(
            $"SELECT Id, Name, IsActive FROM Items WHERE Id = {id}");

        var updatedName = "Beta";
        var updated = await inquiry.ExecuteAsync(
            $"UPDATE Items SET Name = {updatedName} WHERE Id = {id}");

        var streamed = await ToListAsync(inquiry.QueryAsync<TestItem>(
            $"SELECT Id, Name, IsActive FROM Items WHERE Name = {updatedName}"));

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
            MaterializerInstance);

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Alpha", selected.Name);
        Assert.Equal(3, interceptor.InitializedParameterCounts[0]);
        Assert.Contains("@Name", interceptor.InitializedParameterNames[0]);
    }

    [Fact]
    public async Task FastPathExecuteAsyncBindsParametersDirectlyToDbCommand()
    {
        // Exercises the new TArgs+Action overload on the real InquiryRequestPipeline.
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            Array.Empty<IInquiryCommandInterceptor>());

        var inserted = await pipeline.ExecuteAsync(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            (Id: 7, Name: "Fast", IsActive: 1),
            static (cmd, args) =>
            {
                var p0 = cmd.CreateParameter(); p0.ParameterName = "@Id"; p0.Value = args.Id; cmd.Parameters.Add(p0);
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@Name"; p1.Value = args.Name; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@IsActive"; p2.Value = args.IsActive; cmd.Parameters.Add(p2);
            });

        var selected = await pipeline.QuerySingleOrDefaultAsync(
            new InquiryCommand("SELECT Id, Name, IsActive FROM Items WHERE Id = 7"),
            MaterializerInstance);

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Fast", selected.Name);
    }

    [Fact]
    public async Task CustomPipelineWithoutFastPathOverrideFallsBackThroughInquiryCommand()
    {
        // A custom IInquiryRequestPipeline that does NOT override the new ExecuteAsync<TArgs>
        // overload must still execute the call — the default interface method routes it through
        // ExecuteAsync(InquiryCommand) using InquiryCommand.DbCommandBinder.
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var inner = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            Array.Empty<IInquiryCommandInterceptor>());
        var custom = new RecordingForwardingPipeline(inner);
        // Default interface methods must be invoked through the interface, not the concrete type.
        IInquiryRequestPipeline customAsInterface = custom;

        var inserted = await customAsInterface.ExecuteAsync(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            (Id: 9, Name: "Forwarded", IsActive: 1),
            static (cmd, args) =>
            {
                var p0 = cmd.CreateParameter(); p0.ParameterName = "@Id"; p0.Value = args.Id; cmd.Parameters.Add(p0);
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@Name"; p1.Value = args.Name; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@IsActive"; p2.Value = args.IsActive; cmd.Parameters.Add(p2);
            });

        var selected = await inner.QuerySingleOrDefaultAsync(
            new InquiryCommand("SELECT Id, Name, IsActive FROM Items WHERE Id = 9"),
            MaterializerInstance);

        Assert.Equal(1, inserted);
        Assert.NotNull(selected);
        Assert.Equal("Forwarded", selected.Name);
        // The default interface impl synthesised an InquiryCommand carrying the binder; the
        // forwarding pipeline saw it on the InquiryCommand path.
        Assert.Single(custom.SeenInquiryCommands);
        Assert.NotNull(custom.SeenInquiryCommands[0].DbCommandBinder);
    }

    [Fact]
    public async Task FastPathReadsBindParametersDirectlyToDbCommand()
    {
        // Exercises the new TArgs+Action read overloads (single + list) on the real pipeline.
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var pipeline = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            Array.Empty<IInquiryCommandInterceptor>());

        await pipeline.ExecuteAsync(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            (Id: 11, Name: "Fast", IsActive: 1),
            static (cmd, args) =>
            {
                var p0 = cmd.CreateParameter(); p0.ParameterName = "@Id"; p0.Value = args.Id; cmd.Parameters.Add(p0);
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@Name"; p1.Value = args.Name; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@IsActive"; p2.Value = args.IsActive; cmd.Parameters.Add(p2);
            });

        var single = await pipeline.QuerySingleOrDefaultAsync<TestItem, int, TestItemStructMaterializer>(
            "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
            11,
            static (cmd, id) => { var p = cmd.CreateParameter(); p.ParameterName = "@Id"; p.Value = id; cmd.Parameters.Add(p); },
            default);

        var list = await pipeline.QueryListAsync<TestItem, int, TestItemStructMaterializer>(
            "SELECT Id, Name, IsActive FROM Items WHERE IsActive = @IsActive",
            1,
            static (cmd, active) => { var p = cmd.CreateParameter(); p.ParameterName = "@IsActive"; p.Value = active; cmd.Parameters.Add(p); },
            default);

        Assert.NotNull(single);
        Assert.Equal("Fast", single.Name);
        Assert.Single(list);
        Assert.Equal("Fast", list[0].Name);
    }

    [Fact]
    public async Task CustomPipelineWithoutFastReadOverrideFallsBackThroughInquiryCommand()
    {
        // A custom IInquiryRequestPipeline that does NOT override the new read TArgs overloads
        // must still execute — the default interface methods route through
        // QuerySingleOrDefaultAsync(InquiryCommand, …) using InquiryCommand.DbCommandBinder.
        var connectionString = CreateSharedInMemoryConnectionString();
        await using var keeperConnection = new SqliteConnection(connectionString);
        await keeperConnection.OpenAsync();
        await CreateSchemaAsync(keeperConnection);

        var inner = new InquiryRequestPipeline(
            new TestConnectionFactory(connectionString),
            Array.Empty<IInquiryCommandInterceptor>());
        await inner.ExecuteAsync(
            "INSERT INTO Items (Id, Name, IsActive) VALUES (@Id, @Name, @IsActive)",
            (Id: 13, Name: "Forwarded", IsActive: 1),
            static (cmd, args) =>
            {
                var p0 = cmd.CreateParameter(); p0.ParameterName = "@Id"; p0.Value = args.Id; cmd.Parameters.Add(p0);
                var p1 = cmd.CreateParameter(); p1.ParameterName = "@Name"; p1.Value = args.Name; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@IsActive"; p2.Value = args.IsActive; cmd.Parameters.Add(p2);
            });

        var custom = new RecordingForwardingPipeline(inner);
        // Default interface methods must be invoked through the interface, not the concrete type.
        IInquiryRequestPipeline customAsInterface = custom;

        var selected = await customAsInterface.QuerySingleOrDefaultAsync<TestItem, int, TestItemStructMaterializer>(
            "SELECT Id, Name, IsActive FROM Items WHERE Id = @Id",
            13,
            static (cmd, id) => { var p = cmd.CreateParameter(); p.ParameterName = "@Id"; p.Value = id; cmd.Parameters.Add(p); },
            default);

        Assert.NotNull(selected);
        Assert.Equal("Forwarded", selected.Name);
        // The default interface impl synthesised an InquiryCommand carrying the binder; the
        // forwarding pipeline saw it on the QuerySingleOrDefault(InquiryCommand) path.
        Assert.Single(custom.SeenQueryCommands);
        Assert.NotNull(custom.SeenQueryCommands[0].DbCommandBinder);
    }

    /// <summary>
    /// Custom pipeline that only implements the existing InquiryCommand-based overloads.
    /// Verifies the default impl of <c>ExecuteAsync&lt;TArgs&gt;</c> bridges to it.
    /// </summary>
    private sealed class RecordingForwardingPipeline : IInquiryRequestPipeline
    {
        private readonly InquiryRequestPipeline _inner;
        public List<InquiryCommand> SeenInquiryCommands { get; } = new();
        public List<InquiryCommand> SeenQueryCommands { get; } = new();

        public RecordingForwardingPipeline(InquiryRequestPipeline inner) => _inner = inner;

        public Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
        {
            SeenInquiryCommands.Add(command);
            return _inner.ExecuteAsync(command, cancellationToken);
        }

        public IAsyncEnumerable<T> QueryAsync<T>(InquiryCommand command, IInquiryEntityMaterializer<T> materializer, CancellationToken cancellationToken = default) where T : class
            => _inner.QueryAsync(command, materializer, cancellationToken);

        public IAsyncEnumerable<T> QueryAsync<T, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default) where T : class where TMaterializer : struct, IInquiryEntityMaterializer<T>
            => _inner.QueryAsync<T, TMaterializer>(command, materializer, cancellationToken);

        public Task<IReadOnlyList<T>> QueryListAsync<T>(InquiryCommand command, IInquiryEntityMaterializer<T> materializer, CancellationToken cancellationToken = default) where T : class
            => _inner.QueryListAsync(command, materializer, cancellationToken);

        public Task<IReadOnlyList<T>> QueryListAsync<T, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default, int capacityHint = -1) where T : class where TMaterializer : struct, IInquiryEntityMaterializer<T>
            => _inner.QueryListAsync<T, TMaterializer>(command, materializer, cancellationToken, capacityHint);

        public Task<T?> QuerySingleOrDefaultAsync<T>(InquiryCommand command, IInquiryEntityMaterializer<T> materializer, CancellationToken cancellationToken = default) where T : class
            => _inner.QuerySingleOrDefaultAsync(command, materializer, cancellationToken);

        public Task<T?> QuerySingleOrDefaultAsync<T, TMaterializer>(InquiryCommand command, TMaterializer materializer, CancellationToken cancellationToken = default) where T : class where TMaterializer : struct, IInquiryEntityMaterializer<T>
        {
            SeenQueryCommands.Add(command);
            return _inner.QuerySingleOrDefaultAsync<T, TMaterializer>(command, materializer, cancellationToken);
        }
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

    private static readonly TestItemMaterializer MaterializerInstance = new();

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

    private static void AssertCallerCancellation(
        CancellationToken expectedToken,
        OperationCanceledException cancellation,
        ProviderCancellationInterceptor interceptor)
    {
        Assert.Equal(expectedToken, cancellation.CancellationToken);
        var reported = Assert.Single(interceptor.Failures);
        Assert.Same(cancellation, reported);
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

    private sealed class ThrowingExecutingInterceptor : IInquiryCommandInterceptor
    {
        public List<Exception> Failures { get; } = new();

        public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Interceptor setup failed.");

        public ValueTask CommandFailedAsync(InquiryCommandFailedContext context, CancellationToken cancellationToken = default)
        {
            Failures.Add(context.Exception);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProviderCancellationInterceptor : IInquiryCommandInterceptor
    {
        private readonly CancellationTokenSource _source;

        public ProviderCancellationInterceptor(CancellationTokenSource source) => _source = source;

        public List<Exception> Failures { get; } = new();

        public ValueTask CommandExecutingAsync(
            InquiryCommandContext context,
            CancellationToken cancellationToken = default)
        {
            _source.Cancel();
            throw new OperationCanceledException();
        }

        public ValueTask CommandFailedAsync(
            InquiryCommandFailedContext context,
            CancellationToken cancellationToken = default)
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

    // Struct materializer for the struct-constrained fast read overloads
    // (QueryListAsync/QuerySingleOrDefaultAsync<T, TArgs, TMaterializer>).
    private readonly struct TestItemStructMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public TestItem Materialize(DbDataReader reader) => MaterializeItem(reader);
    }

    private sealed class ThrowingMaterializer : IInquiryEntityMaterializer<TestItem>
    {
        public TestItem Materialize(DbDataReader reader)
            => throw new InvalidOperationException("Bad materializer");
    }
}
