using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using Inquiry.Commands;
using Inquiry.Entities;
using Inquiry.Materialization;
using Inquiry.Parameters;
using Inquiry.Stores;
using Inquiry.Transactions;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;

namespace Inquiry.Benchmarks;

/// <summary>
/// Measures actual generated-store dispatch and parameter binding without opening a connection or
/// executing SQL. Each generated leg is compared with the direct static-binder floor for the same
/// retained provider command.
/// </summary>
/// <remarks>
/// The sink implements the immutable <see cref="InquiryGeneratedCommand{TArgs}"/> overload used by
/// generated stores, applies its static binder to a retained SQLite command, and rejects every
/// legacy <see cref="InquiryCommand"/> overload. A successful run therefore proves that the
/// benchmark exercises generated command dispatch rather than silently measuring the boxed fallback.
/// The eight-parameter operation also makes the compiler lower the generated tuple state through
/// <see cref="ValueTuple{T1,T2,T3,T4,T5,T6,T7,TRest}"/>.
/// </remarks>
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 4, printSource: true)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ParameterBindingBenchmarks
{
    private const string ParameterlessSql =
        "SELECT CASE WHEN EXISTS(SELECT 1 FROM \"ParameterBindingProbe\") THEN 1 ELSE 0 END";
    private const string OneParameterSql =
        "SELECT CASE WHEN EXISTS(SELECT 1 FROM \"ParameterBindingProbe\" WHERE \"Filter1\" = @Filter1) THEN 1 ELSE 0 END";
    private const string EightParameterSql =
        "SELECT CASE WHEN EXISTS(SELECT 1 FROM \"ParameterBindingProbe\" WHERE \"Filter1\" = @Filter1 AND \"Filter2\" = @Filter2 AND \"Filter3\" = @Filter3 AND \"Filter4\" = @Filter4 AND \"Filter5\" = @Filter5 AND \"Filter6\" = @Filter6 AND \"Filter7\" = @Filter7 AND \"Filter8\" = @Filter8) THEN 1 ELSE 0 END";
    private const string CollectionSql =
        "SELECT CASE WHEN EXISTS(SELECT 1 FROM \"ParameterBindingProbe\" WHERE \"Filter1\" IN (SELECT value FROM json_each(@Filter1))) THEN 1 ELSE 0 END";
    private static readonly IReadOnlyList<int> CollectionValues = new[] { 1, 2, 3 };

    private ParameterBindingSink _sink = null!;
    private ParameterBindingProbeStore _store = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _sink = new ParameterBindingSink();
        _store = new ParameterBindingProbeStore(_sink);

        // Fail setup if generator output or the direct floors drift apart. This also exercises each
        // generated method before BenchmarkDotNet starts collecting measurements.
        RunGeneratedParameterless();
        AssertBound(ParameterlessSql, 0);
        RunGeneratedOneParameter();
        AssertBound(OneParameterSql, 1);
        RunGeneratedEightParameters();
        AssertBound(EightParameterSql, 8);
        RunGeneratedCollection();
        AssertBound(CollectionSql, 1);
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _sink.Dispose();

    [BenchmarkCategory("Parameterless"), Benchmark(Baseline = true)]
    public int Parameterless_DirectStaticBinderFloor()
        => _sink.BindDirect(ParameterlessSql, 0, static (_, _) => { });

    [BenchmarkCategory("Parameterless"), Benchmark]
    public int Parameterless_GeneratedStore()
    {
        RunGeneratedParameterless();
        return _sink.ParameterCount;
    }

    [BenchmarkCategory("OneParameter"), Benchmark(Baseline = true)]
    public int OneParameter_DirectStaticBinderFloor()
        => _sink.BindDirect(OneParameterSql, 42, BindOneParameter);

    [BenchmarkCategory("OneParameter"), Benchmark]
    public int OneParameter_GeneratedStore()
    {
        RunGeneratedOneParameter();
        return _sink.ParameterCount;
    }

    [BenchmarkCategory("EightParameters"), Benchmark(Baseline = true)]
    public int EightParameters_DirectStaticBinderFloor()
        => _sink.BindDirect(EightParameterSql, (1, 2, 3, 4, 5, 6, 7, 8), BindEightParameters);

    [BenchmarkCategory("EightParameters"), Benchmark]
    public int EightParameters_GeneratedStore()
    {
        RunGeneratedEightParameters();
        return _sink.ParameterCount;
    }

    [BenchmarkCategory("Collection"), Benchmark(Baseline = true)]
    public int Collection_DirectStaticBinderFloor()
        => _sink.BindDirect(CollectionSql, CollectionValues, BindCollection);

    [BenchmarkCategory("Collection"), Benchmark]
    public int Collection_GeneratedStore()
    {
        RunGeneratedCollection();
        return _sink.ParameterCount;
    }

    private void RunGeneratedParameterless()
        => _ = _store.AnyAsync().GetAwaiter().GetResult();

    private void RunGeneratedOneParameter()
        => _ = _store.ExistsByFilter1Async(42).GetAwaiter().GetResult();

    private void RunGeneratedEightParameters()
        => _ = _store.ExistsByEightFiltersAsync(1, 2, 3, 4, 5, 6, 7, 8).GetAwaiter().GetResult();

    private void RunGeneratedCollection()
        => _ = _store.ExistsByFilter1InAsync(CollectionValues).GetAwaiter().GetResult();

    private void AssertBound(string expectedSql, int expectedParameters)
    {
        if (!string.Equals(_sink.CommandText, expectedSql, StringComparison.Ordinal) ||
            _sink.ParameterCount != expectedParameters)
        {
            throw new InvalidOperationException(
                $"Generated benchmark contract drifted. Expected {expectedParameters} parameters and SQL '{expectedSql}', " +
                $"but observed {_sink.ParameterCount} parameters and SQL '{_sink.CommandText}'.");
        }
    }

    private static void BindOneParameter(DbCommand command, int value)
        => AddParameter(command, "@Filter1", value);

    private static void BindEightParameters(
        DbCommand command,
        (int Filter1, int Filter2, int Filter3, int Filter4, int Filter5, int Filter6, int Filter7, int Filter8) values)
    {
        AddParameter(command, "@Filter1", values.Filter1);
        AddParameter(command, "@Filter2", values.Filter2);
        AddParameter(command, "@Filter3", values.Filter3);
        AddParameter(command, "@Filter4", values.Filter4);
        AddParameter(command, "@Filter5", values.Filter5);
        AddParameter(command, "@Filter6", values.Filter6);
        AddParameter(command, "@Filter7", values.Filter7);
        AddParameter(command, "@Filter8", values.Filter8);
    }

    private static void BindCollection(DbCommand command, IReadOnlyList<int> values)
        => InquiryJsonArrayParameter.Bind(command, "@Filter1", values);

    private static void AddParameter(DbCommand command, string name, int value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.Int32;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

[InquiryTable("ParameterBindingProbe")]
internal sealed class ParameterBindingProbe
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public int Filter1 { get; set; }

    [InquiryColumn]
    public int Filter2 { get; set; }

    [InquiryColumn]
    public int Filter3 { get; set; }

    [InquiryColumn]
    public int Filter4 { get; set; }

    [InquiryColumn]
    public int Filter5 { get; set; }

    [InquiryColumn]
    public int Filter6 { get; set; }

    [InquiryColumn]
    public int Filter7 { get; set; }

    [InquiryColumn]
    public int Filter8 { get; set; }
}

internal partial class ParameterBindingProbeStore : InquiryStore<ParameterBindingProbe>
{
    [InquiryExists]
    public partial Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    [InquiryExists]
    [InquiryWhere("Filter1")]
    public partial Task<bool> ExistsByFilter1Async(int filter1, CancellationToken cancellationToken = default);

    [InquiryExists]
    [InquiryWhere("Filter1")]
    [InquiryWhere("Filter2")]
    [InquiryWhere("Filter3")]
    [InquiryWhere("Filter4")]
    [InquiryWhere("Filter5")]
    [InquiryWhere("Filter6")]
    [InquiryWhere("Filter7")]
    [InquiryWhere("Filter8")]
    public partial Task<bool> ExistsByEightFiltersAsync(
        int filter1,
        int filter2,
        int filter3,
        int filter4,
        int filter5,
        int filter6,
        int filter7,
        int filter8,
        CancellationToken cancellationToken = default);

    [InquiryExists]
    [InquiryWhere("Filter1", Compare.In)]
    public partial Task<bool> ExistsByFilter1InAsync(
        IReadOnlyList<int> values,
        CancellationToken cancellationToken = default);
}

internal sealed class ParameterBindingSink : IInquiry, IDisposable
{
    private static readonly Task<bool> FalseTask = Task.FromResult(false);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbCommand _command;

    public ParameterBindingSink() => _command = _connection.CreateCommand();

    public string CommandText => _command.CommandText;
    public int ParameterCount => _command.Parameters.Count;

    public int BindDirect<TArgs>(string commandText, TArgs args, Action<DbCommand, TArgs> binder)
    {
        Prepare(commandText, CommandType.Text);
        binder(_command, args);
        return _command.Parameters.Count;
    }

    public Task<T> ExecuteScalarAsync<T, TArgs>(
        InquiryGeneratedCommand<TArgs> command,
        CancellationToken cancellationToken = default)
    {
        Prepare(command.CommandText, command.CommandType);
        command.BindParameters(_command, command.Args);

        if (typeof(T) == typeof(bool))
        {
            return (Task<T>)(object)FalseTask;
        }

        throw new InvalidOperationException($"Unexpected generated scalar type {typeof(T)}.");
    }

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
        => throw new NotSupportedException("Transactions are outside the binding benchmark.");

    public void Dispose()
    {
        _command.Dispose();
        _connection.Dispose();
    }

    private void Prepare(string commandText, CommandType commandType)
    {
        _command.Parameters.Clear();
        _command.CommandText = commandText;
        _command.CommandType = commandType;
    }

    private static InvalidOperationException BoxedPathUsed()
        => new("The parameter-binding benchmark reached a boxed InquiryCommand path.");
}
