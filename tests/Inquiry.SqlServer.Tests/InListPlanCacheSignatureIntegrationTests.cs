using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Proves the #102 win against real SQL Server: a <c>Compare.In</c> predicate over a declared-length
/// string column threads <c>Size = 64</c> onto each expanded IN-element parameter, so SqlClient declares
/// <c>@name0 nvarchar(64)</c> regardless of the value's length. A single-element list pads to bucket 1, so
/// the SQL text is identical across the three calls and the only thing that could vary the
/// <c>sp_executesql</c> signature is the element's declared size — one cached plan for all lengths instead
/// of one per length, the IN-path counterpart of <see cref="PlanCacheSignatureIntegrationTests"/>.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class InListPlanCacheSignatureIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public InListPlanCacheSignatureIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task DeclaredLengthStringInList_KeepsOneSpExecuteSqlSignatureAcrossValueLengths()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        const string ddl = "CREATE TABLE TPlanCacheItem (Id INT IDENTITY(1,1) PRIMARY KEY, Name NVARCHAR(64) NOT NULL);";
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "planinlist");
        var store = harness.GetRequiredService<PlanCacheItemStore>();

        // One-element IN lists all pad to bucket 1, so the SQL text "(@name0)" is identical across the three
        // calls — the value length is the only thing that could split the signature. Without the threaded
        // Size each would declare @name0 as nvarchar(2/4/6) → three plans.
        await store.InNamesAsync(["ab"]);
        await store.InNamesAsync(["abcd"]);
        await store.InNamesAsync(["abcdef"]);

        var (distinctSignatures, sampleText) = await QueryInListPlanCacheAsync(harness.ConnectionString);

        Assert.Equal(1, distinctSignatures);
        Assert.Contains("nvarchar(64)", sampleText);
    }

    // Distinct parameterized IN statement texts cached for THIS database (isolated by the dbid plan
    // attribute so a sibling test class in the collection can't pollute the count). Every expansion names
    // its first element @name0, which identifies the InNames query and excludes the INSERT/SelectByName paths.
    private static async Task<(int DistinctSignatures, string SampleText)> QueryInListPlanCacheAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(DISTINCT st.text), MAX(st.text)
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            CROSS APPLY sys.dm_exec_plan_attributes(cp.plan_handle) pa
            WHERE pa.attribute = 'dbid' AND pa.value = DB_ID()
              AND st.text LIKE '%TPlanCacheItem%'
              AND st.text LIKE '%@name0%'
              AND st.text NOT LIKE '%dm_exec%';
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var count = reader.GetInt32(0);
        var text = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        return (count, text);
    }
}
