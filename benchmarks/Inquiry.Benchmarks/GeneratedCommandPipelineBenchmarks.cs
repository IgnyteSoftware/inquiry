using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Data;

namespace Inquiry.Benchmarks;

/// <summary>
/// Measures real generated-store execution through <c>DefaultInquiry</c> and the built-in request
/// pipeline against a shared in-memory SQLite database.
/// </summary>
/// <remarks>
/// Measured stores receive the DI-resolved <c>DefaultInquiry</c> directly. Setup also invokes the
/// same generated methods through a routing guard that forwards immutable generated commands and
/// throws if the store reaches a boxed <see cref="InquiryCommand"/> scalar overload. The three legs
/// per state therefore include connection creation/opening, command creation, generated parameter
/// binding, interceptor routing, provider execution, scalar conversion, and disposal.
/// </remarks>
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 4, printSource: true)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GeneratedCommandPipelineBenchmarks
{
    private SqliteConnection _keeper = null!;
    private ServiceProvider _withoutInterceptorsProvider = null!;
    private ServiceProvider _inactiveTelemetryProvider = null!;
    private ServiceProvider _activeInterceptorProvider = null!;
    private ParameterBindingProbeStore _withoutInterceptorsStore = null!;
    private ParameterBindingProbeStore _inactiveTelemetryStore = null!;
    private ParameterBindingProbeStore _activeInterceptorStore = null!;
    private CountingInterceptor _activeInterceptor = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var connectionString = $"Data Source=InquiryBinding-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(connectionString);
        _keeper.Open();

        using (var command = _keeper.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE \"ParameterBindingProbe\" (" +
                "\"Id\" INTEGER PRIMARY KEY, " +
                "\"Filter1\" INTEGER NOT NULL, \"Filter2\" INTEGER NOT NULL, " +
                "\"Filter3\" INTEGER NOT NULL, \"Filter4\" INTEGER NOT NULL, " +
                "\"Filter5\" INTEGER NOT NULL, \"Filter6\" INTEGER NOT NULL, " +
                "\"Filter7\" INTEGER NOT NULL, \"Filter8\" INTEGER NOT NULL);" +
                "INSERT INTO \"ParameterBindingProbe\" VALUES (1, 1, 2, 3, 4, 5, 6, 7, 8);" +
                "INSERT INTO \"ParameterBindingProbe\" VALUES (2, 42, 0, 0, 0, 0, 0, 0, 0);";
            command.ExecuteNonQuery();
        }

        _withoutInterceptorsProvider = CreateProvider(connectionString);
        _inactiveTelemetryProvider = CreateProvider(connectionString, addInactiveTelemetry: true);
        _activeInterceptor = new CountingInterceptor();
        _activeInterceptorProvider = CreateProvider(connectionString, interceptor: _activeInterceptor);

        _withoutInterceptorsStore = CreateStore(_withoutInterceptorsProvider);
        _inactiveTelemetryStore = CreateStore(_inactiveTelemetryProvider);
        _activeInterceptorStore = CreateStore(_activeInterceptorProvider);

        AssertAllOperationsReturnTrue(_withoutInterceptorsStore);
        AssertAllOperationsReturnTrue(_inactiveTelemetryStore);
        AssertAllOperationsReturnTrue(CreateGuardedStore(_withoutInterceptorsProvider));
        var executingBefore = _activeInterceptor.ExecutingCount;
        AssertAllOperationsReturnTrue(_activeInterceptorStore);
        if (_activeInterceptor.ExecutingCount - executingBefore != 3)
        {
            throw new InvalidOperationException("The active-interceptor benchmark did not execute all three commands through the interceptor pipeline.");
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _activeInterceptorProvider.Dispose();
        _inactiveTelemetryProvider.Dispose();
        _withoutInterceptorsProvider.Dispose();
        _keeper.Dispose();
    }

    [BenchmarkCategory("RuntimeParameterless"), Benchmark(Baseline = true)]
    public bool Parameterless_NoInterceptors()
        => _withoutInterceptorsStore.AnyAsync().GetAwaiter().GetResult();

    [BenchmarkCategory("RuntimeParameterless"), Benchmark]
    public bool Parameterless_InactiveTelemetry()
        => _inactiveTelemetryStore.AnyAsync().GetAwaiter().GetResult();

    [BenchmarkCategory("RuntimeParameterless"), Benchmark]
    public bool Parameterless_ActiveCustomInterceptor()
        => _activeInterceptorStore.AnyAsync().GetAwaiter().GetResult();

    [BenchmarkCategory("RuntimeOneParameter"), Benchmark(Baseline = true)]
    public bool OneParameter_NoInterceptors()
        => _withoutInterceptorsStore.ExistsByFilter1Async(42).GetAwaiter().GetResult();

    [BenchmarkCategory("RuntimeOneParameter"), Benchmark]
    public bool OneParameter_InactiveTelemetry()
        => _inactiveTelemetryStore.ExistsByFilter1Async(42).GetAwaiter().GetResult();

    [BenchmarkCategory("RuntimeOneParameter"), Benchmark]
    public bool OneParameter_ActiveCustomInterceptor()
        => _activeInterceptorStore.ExistsByFilter1Async(42).GetAwaiter().GetResult();

    [BenchmarkCategory("RuntimeEightParameters"), Benchmark(Baseline = true)]
    public bool EightParameters_NoInterceptors()
        => _withoutInterceptorsStore.ExistsByEightFiltersAsync(1, 2, 3, 4, 5, 6, 7, 8).GetAwaiter().GetResult();

    [BenchmarkCategory("RuntimeEightParameters"), Benchmark]
    public bool EightParameters_InactiveTelemetry()
        => _inactiveTelemetryStore.ExistsByEightFiltersAsync(1, 2, 3, 4, 5, 6, 7, 8).GetAwaiter().GetResult();

    [BenchmarkCategory("RuntimeEightParameters"), Benchmark]
    public bool EightParameters_ActiveCustomInterceptor()
        => _activeInterceptorStore.ExistsByEightFiltersAsync(1, 2, 3, 4, 5, 6, 7, 8).GetAwaiter().GetResult();

    private static ServiceProvider CreateProvider(
        string connectionString,
        bool addInactiveTelemetry = false,
        IInquiryCommandInterceptor? interceptor = null)
    {
        var services = new ServiceCollection()
            .AddInquiry(options => options.PrepareStatements = PreparedStatementMode.None)
            .AddInquirySqlite(connectionString);

        if (addInactiveTelemetry)
        {
            services.AddInquiryTelemetry();
        }

        if (interceptor is not null)
        {
            services.AddSingleton<IInquiryCommandInterceptor>(interceptor);
        }

        return services.BuildServiceProvider();
    }

    private static ParameterBindingProbeStore CreateGuardedStore(ServiceProvider provider)
        => new(new GeneratedCommandRoutingGuard(provider.GetRequiredService<IInquiry>()));

    private static ParameterBindingProbeStore CreateStore(ServiceProvider provider)
        => new(provider.GetRequiredService<IInquiry>());

    private static void AssertAllOperationsReturnTrue(ParameterBindingProbeStore store)
    {
        if (!store.AnyAsync().GetAwaiter().GetResult()
            || !store.ExistsByFilter1Async(42).GetAwaiter().GetResult()
            || !store.ExistsByEightFiltersAsync(1, 2, 3, 4, 5, 6, 7, 8).GetAwaiter().GetResult())
        {
            throw new InvalidOperationException("The generated-command runtime benchmark database or generated SQL is invalid.");
        }
    }
}

internal sealed class CountingInterceptor : IInquiryCommandInterceptor
{
    public long ExecutingCount { get; private set; }

    public ValueTask CommandExecutingAsync(InquiryCommandContext context, CancellationToken cancellationToken = default)
    {
        ExecutingCount++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class GeneratedCommandRoutingGuard : IInquiry
{
    private readonly IInquiry _inner;

    public GeneratedCommandRoutingGuard(IInquiry inner) => _inner = inner;

    public Task<T> ExecuteScalarAsync<T, TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
        => _inner.ExecuteScalarAsync<T, TArgs>(command, cancellationToken);

    public Task<T> ExecuteScalarAsync<T>(InquiryCommand command, CancellationToken cancellationToken = default)
        => throw BoxedPathUsed();

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => throw BoxedPathUsed();

    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => throw BoxedPathUsed();

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => throw BoxedPathUsed();

    public IAsyncEnumerable<TEntity> QueryAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => throw BoxedPathUsed();

    public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default,
        int capacityHint = -1)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => throw BoxedPathUsed();

    public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
        => throw BoxedPathUsed();

    public Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
        => throw BoxedPathUsed();

    public Task<IInquiryTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Transactions are outside the generated-command runtime benchmark.");

    private static InvalidOperationException BoxedPathUsed()
        => new("The generated-command runtime benchmark reached a boxed InquiryCommand path.");
}
