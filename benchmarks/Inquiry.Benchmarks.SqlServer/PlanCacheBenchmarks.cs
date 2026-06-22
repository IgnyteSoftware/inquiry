using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Inquiry.Parameters;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>
/// Quantifies the SQL Server plan-cache wins shipped by #67 (IN-list power-of-two bucketing) and #56
/// (declared parameter <c>Size</c>). SQL Server keys its plan cache on the <c>sp_executesql</c> text +
/// parameter signature, so a workload whose IN cardinality (or string value length) varies compiles a
/// fresh plan per distinct shape unless that shape is stabilised. The correctness side — that the distinct
/// signature <em>count</em> collapses — is proven by <c>InListBucketingIntegrationTests</c> /
/// <c>PlanCacheSignatureIntegrationTests</c>; this benchmark <em>measures the cost</em> of the churn those
/// fixes remove.
/// <para>
/// Each benchmark clears the plan cache (<c>DBCC FREEPROCCACHE</c>) at the start of the measured region, so
/// every run pays real compilations — the constant clear cost is identical across the compared variants, so
/// the delta is the compile-count difference. The IN sweep uses <b>bucket-boundary-adjacent</b>
/// cardinalities (1,2,3,4,5,8,9,16,17) to exercise both within-bucket collapse and cross-boundary steps; a
/// 1/5/20/100 spread (as in the PostgreSQL array-path bench) would miss that.
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[Orderer(SummaryOrderPolicy.Declared, MethodOrderPolicy.Declared)]
public class PlanCacheBenchmarks
{
    // Bucket-boundary-adjacent IN cardinalities. Bucketed → next-pow2 {1,2,4,4,8,8,16,16,32} = 6 distinct
    // texts; unbucketed → 9 distinct texts (one per cardinality). The gap is the compiles bucketing saves.
    private static readonly int[] Cardinalities = { 1, 2, 3, 4, 5, 8, 9, 16, 17 };

    // Value lengths for the parameter-Size sweep: a declared Size pins one nvarchar(40) signature across all
    // of them (1 compile); inference declares nvarchar(value-length) and compiles one plan per length.
    private static readonly int[] ValueLengths = { 2, 4, 6, 8, 12, 20, 30, 40 };

    private SqlServerBenchmarkDatabase _db = null!;
    private int[] _categoryIds = null!;

    [Params(1000)]
    public int Rows;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _db = SqlServerBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();
        _categoryIds = LoadCategoryIdsAsync().GetAwaiter().GetResult();
    }

    private async Task<int[]> LoadCategoryIdsAsync()
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CategoryID FROM Categories ORDER BY CategoryID";
        var ids = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids.ToArray();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    // A list of n category ids (cycling the real ids so the query matches rows; repeats are fine for IN).
    private IReadOnlyList<int> IdsOf(int n)
    {
        var list = new int[n];
        for (var i = 0; i < n; i++)
        {
            list[i] = _categoryIds[i % _categoryIds.Length];
        }

        return list;
    }

    private async Task ClearPlanCacheAsync(SqlConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DBCC FREEPROCCACHE WITH NO_INFOMSGS;";
        await cmd.ExecuteNonQueryAsync();
    }

    // ---- IN-list bucketing (#67) -------------------------------------------------------------

    /// <summary>
    /// Shipped path: the runtime <see cref="InquiryInExpansion.Expand"/> helper pads each IN list to the
    /// next power of two, so the nine cardinalities collapse to six distinct <c>sp_executesql</c> texts —
    /// six compilations against a cold cache. (Bound on a raw command so the comparison with
    /// <see cref="Unbucketed"/> isolates the bucketing mechanics, not the surrounding store pipeline.)
    /// </summary>
    [BenchmarkCategory("InBucketing"), Benchmark(Baseline = true)]
    public async Task Bucketed()
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await ClearPlanCacheAsync(connection);

        foreach (var n in Cardinalities)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ProductID FROM Products WHERE CategoryID IN (@ids)";
            InquiryInExpansion.Expand(cmd, "@ids", IdsOf(n)); // pads to the next power of two
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { }
        }
    }

    /// <summary>
    /// Forced-unbucketed baseline (the pre-#67 behaviour): exact-cardinality IN SQL with one placeholder per
    /// element, so each distinct cardinality is a distinct text — nine compilations against a cold cache.
    /// </summary>
    [BenchmarkCategory("InBucketing"), Benchmark]
    public async Task Unbucketed()
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await ClearPlanCacheAsync(connection);

        foreach (var n in Cardinalities)
        {
            var ids = IdsOf(n);
            await using var cmd = connection.CreateCommand();
            var placeholders = string.Join(", ", Enumerable.Range(0, n).Select(i => "@p" + i));
            cmd.CommandText = "SELECT ProductID FROM Products WHERE CategoryID IN (" + placeholders + ")";
            for (var i = 0; i < n; i++)
            {
                cmd.Parameters.Add("@p" + i, System.Data.SqlDbType.Int).Value = ids[i];
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { }
        }
    }

    // ---- Parameter Size (#56) ----------------------------------------------------------------

    /// <summary>
    /// Shipped behaviour: a declared-length string predicate binds <c>@name nvarchar(40)</c> regardless of
    /// value length, so all eight lengths share one signature — one compilation against a cold cache.
    /// </summary>
    [BenchmarkCategory("ParameterSize"), Benchmark(Baseline = true)]
    public async Task DeclaredSize()
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await ClearPlanCacheAsync(connection);

        foreach (var len in ValueLengths)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ProductID FROM Products WHERE ProductName = @name";
            cmd.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 40).Value = new string('x', len);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { }
        }
    }

    /// <summary>
    /// Inference baseline (no declared Size): SqlClient declares <c>@name nvarchar(value-length)</c>, so each
    /// distinct value length is a distinct signature — one compilation per length against a cold cache.
    /// </summary>
    [BenchmarkCategory("ParameterSize"), Benchmark]
    public async Task InferredSize()
    {
        await using var connection = new SqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await ClearPlanCacheAsync(connection);

        foreach (var len in ValueLengths)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ProductID FROM Products WHERE ProductName = @name";
            cmd.Parameters.AddWithValue("@name", new string('x', len)); // size inferred from the value
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { }
        }
    }
}
