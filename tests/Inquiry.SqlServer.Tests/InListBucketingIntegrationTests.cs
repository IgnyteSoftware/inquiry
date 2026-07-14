using System.Collections.Generic;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Proves the #67 win against real SQL Server: <c>Compare.In</c> expansion pads each list to the next
/// power-of-two bucket, so a workload issuing many different IN cardinalities reuses a small fixed set of
/// cached plans instead of one per length — without changing which rows match.
/// </summary>
/// <remarks>#199 replaces SQL Server bucketing with one stable TVP command and parameter signature.</remarks>
[Collection(SqlServerCollection.Name)]
public sealed class InListBucketingIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public InListBucketingIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InListCardinalitiesUseOneTvpSignatureAndPreserveResults()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "inbucket");
        var categories = harness.GetRequiredService<CategoryStore>();
        var products = harness.GetRequiredService<ProductStore>();

        var c1 = (await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" }))!.CategoryID!.Value;
        var c2 = (await categories.InsertReturningAsync(new Category { CategoryName = "Condiments" }))!.CategoryID!.Value;
        await products.InsertAsync(new Product { ProductName = "Chai", UnitPrice = 18m, UnitsInStock = 39, CategoryID = c1, Discontinued = false });
        await products.InsertAsync(new Product { ProductName = "Aniseed", UnitPrice = 10m, UnitsInStock = 13, CategoryID = c2, Discontinued = false });

        // Cardinalities 1..9 → buckets {1,2,4,4,8,8,8,8,16} → 5 distinct bucket lengths. Without bucketing
        // these nine lengths would each render distinct SQL text (nine cached plans).
        for (var k = 1; k <= 9; k++)
        {
            var ids = new List<int> { c1 };
            for (var i = 1; i < k; i++)
            {
                ids.Add(c2 + i); // filler ids that match no category — the SQL shape is what matters here
            }

            await products.InCategoriesAsync(ids);
        }

        // Results are unchanged by padding: a 3-element list of all-c1 (bucket 4, padded with a repeated c1)
        // returns only c1's products — the duplicate never widens the match set.
        var justC1 = await products.InCategoriesAsync(new List<int> { c1, c1, c1 });
        Assert.NotEmpty(justC1);
        Assert.All(justC1, p => Assert.Equal(c1, p.CategoryID));

        var distinctSignatures = await DistinctInSignaturesAsync(harness.ConnectionString);
        Assert.Equal(1, distinctSignatures);
    }

    // Distinct parameterized IN statement texts cached for THIS database (isolated by the dbid plan
    // attribute so a sibling test class in the collection can't pollute the count). Every expansion names
    // its first element @categoryID0, so that token identifies the InCategories query.
    private static async Task<int> DistinctInSignaturesAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(DISTINCT st.text)
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            CROSS APPLY sys.dm_exec_plan_attributes(cp.plan_handle) pa
            WHERE pa.attribute = 'dbid' AND pa.value = DB_ID()
              AND st.text LIKE '%Products%'
              AND st.text LIKE '%Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c%'
              AND st.text NOT LIKE '%dm_exec%';
            """;
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
