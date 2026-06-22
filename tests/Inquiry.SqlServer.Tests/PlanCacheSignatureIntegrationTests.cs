using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Proves the #56 win against real SQL Server: a generated predicate query on a declared-length string
/// column emits <c>_p.Size = 64</c>, so SqlClient declares the <c>sp_executesql</c> parameter as
/// <c>@Name nvarchar(64)</c> regardless of the value's length — one cached plan signature for all
/// lengths instead of one per length (plan-cache pollution).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PlanCacheSignatureIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public PlanCacheSignatureIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task DeclaredLengthStringPredicate_KeepsOneSpExecuteSqlSignatureAcrossValueLengths()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        const string ddl = "CREATE TABLE TPlanCacheItem (Id INT IDENTITY(1,1) PRIMARY KEY, Name NVARCHAR(64) NOT NULL);";
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "plancache");
        var store = harness.GetRequiredService<PlanCacheItemStore>();

        // Same generated query, three different value lengths. An empty table is fine — the plan is
        // cached regardless of how many rows match.
        await store.SelectByNameAsync("ab");
        await store.SelectByNameAsync("abcd");
        await store.SelectByNameAsync("abcdef");

        var (distinctSignatures, sampleText) = await QueryPlanCacheAsync(harness.ConnectionString);

        // One signature for all three lengths (would be three without the emitted Size), and that
        // signature pins the declared nvarchar(64) rather than a value-derived size.
        Assert.Equal(1, distinctSignatures);
        Assert.Contains("nvarchar(64)", sampleText);
    }

    // Distinct parameterized statement texts in the plan cache that target TPlanCacheItem and filter by
    // @Name (the WHERE excludes the INSERT path; NOT LIKE '%dm_exec%' excludes this diagnostic query).
    private static async Task<(int DistinctSignatures, string SampleText)> QueryPlanCacheAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(DISTINCT st.text), MAX(st.text)
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            WHERE st.text LIKE '%TPlanCacheItem%'
              AND st.text LIKE '%= @Name%'
              AND st.text NOT LIKE '%dm_exec%';
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var count = reader.GetInt32(0);
        var text = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        return (count, text);
    }
}
