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
/// <remarks>#199 binds one schema-qualified TVP signature; #509 binds short lists as scalar parameters again.</remarks>
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

        // One-element IN lists bind one scalar parameter (#509), so the SQL text is identical across the three
        // calls and the value length is the only thing that could split the signature. The declared Size keeps
        // @Name_in0 at nvarchar(64) instead of nvarchar(2/4/6), so there is one plan.
        await store.InNamesAsync(["ab"]);
        await store.InNamesAsync(["abcd"]);
        await store.InNamesAsync(["abcdef"]);

        var (scalarSignatures, scalarText) = await QueryInListPlanCacheAsync(harness.ConnectionString, "%@Name_in0 nvarchar(64)%");

        Assert.Equal(1, scalarSignatures);
        Assert.DoesNotContain("Inquiry_Tvp_", scalarText, StringComparison.Ordinal);

        // Lists longer than the threshold bind one TVP whose signature does not depend on value length either.
        await store.InNamesAsync(LongList("ab"));
        await store.InNamesAsync(LongList("abcdef"));

        var (tvpSignatures, _) = await QueryInListPlanCacheAsync(harness.ConnectionString, "%Inquiry_Tvp_f2eaaa262a5392ae45922f38ea30b9ed4c414a6e6c502340e41458a5e1eded0f%");

        Assert.Equal(1, tvpSignatures);

        static List<string> LongList(string value) => Enumerable.Range(0, 17).Select(i => value + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    // Distinct parameterized IN statement texts cached for THIS database (isolated by the dbid plan
    // attribute so a sibling test class in the collection can't pollute the count). The pattern names
    // the scalar element or the TVP type, so it matches only the InNames query.
    private static async Task<(int DistinctSignatures, string SampleText)> QueryInListPlanCacheAsync(string connectionString, string textPattern)
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
              AND st.text LIKE @pattern
              AND st.text NOT LIKE '%dm_exec%';
            """;
        cmd.Parameters.AddWithValue("@pattern", textPattern);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var count = reader.GetInt32(0);
        var text = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        return (count, text);
    }
}
