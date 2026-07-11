using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using System.Xml.Linq;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class AnsiPredicatePlanIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public AnsiPredicatePlanIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task VarcharPredicate_HasStableSignatureAndSeekCompatiblePlan()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        const string ddl = """
            CREATE TABLE TAnsiPlanCacheItem
            (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Code VARCHAR(64) NOT NULL
            );
            CREATE NONCLUSTERED INDEX IX_TAnsiPlanCacheItem_Code ON TAnsiPlanCacheItem(Code);
            WITH n AS
            (
                SELECT 1 AS i
                UNION ALL SELECT i + 1 FROM n WHERE i < 1000
            )
            INSERT TAnsiPlanCacheItem(Code)
            SELECT CONCAT('code-', RIGHT(CONCAT('0000', i), 4)) FROM n OPTION (MAXRECURSION 1000);
            """;
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "ansiplan");
        var store = harness.GetRequiredService<AnsiPlanCacheItemStore>();

        var rows = await store.SelectByCodeAsync("code-0500");
        Assert.Single(rows);

        var plan = XDocument.Parse(await ReadCachedPlanAsync(harness.ConnectionString));
        var parameterList = Assert.Single(plan.Descendants(), static element => element.Name.LocalName == "ParameterList");
        var parameter = Assert.Single(parameterList.Elements(), static element =>
            element.Name.LocalName == "ColumnReference" &&
            string.Equals((string?)element.Attribute("Column"), "@Code", StringComparison.Ordinal));
        Assert.Equal("varchar(64)", (string?)parameter.Attribute("ParameterDataType"));

        var seek = Assert.Single(plan.Descendants(), static element =>
            element.Name.LocalName == "RelOp" &&
            string.Equals((string?)element.Attribute("PhysicalOp"), "Index Seek", StringComparison.Ordinal));
        Assert.Contains(seek.Descendants(), static element =>
            element.Name.LocalName == "Object" &&
            ((string?)element.Attribute("Index"))?.Contains("IX_TAnsiPlanCacheItem_Code", StringComparison.Ordinal) == true);
        var seekPredicates = Assert.Single(seek.Descendants(), static element => element.Name.LocalName == "SeekPredicates");
        Assert.Contains(seekPredicates.Descendants(), static element =>
            element.Name.LocalName == "ColumnReference" &&
            string.Equals((string?)element.Attribute("Column"), "Code", StringComparison.Ordinal));
        Assert.DoesNotContain("CONVERT_IMPLICIT", seekPredicates.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadCachedPlanAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CONVERT(nvarchar(max), qp.query_plan)
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
            CROSS APPLY sys.dm_exec_plan_attributes(cp.plan_handle) pa
            WHERE st.text LIKE '%TAnsiPlanCacheItem%'
              AND st.text LIKE '%= @Code%'
              AND st.text NOT LIKE '%dm_exec%'
              AND pa.attribute = 'dbid'
              AND CONVERT(int, pa.value) = DB_ID();
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var plan = reader.GetString(0);
        Assert.False(await reader.ReadAsync(), "Expected exactly one cached plan for the generated statement in this database.");
        return plan;
    }
}
