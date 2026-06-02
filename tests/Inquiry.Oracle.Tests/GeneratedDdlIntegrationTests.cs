using System.Threading.Tasks;
using Inquiry.Generated;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace Inquiry.Oracle.Tests;

/// <summary>Verifies Inquiry's own generated DDL (InquiryGeneratedSchema.Ddl) stands up a working
/// schema on real Oracle: it executes (split into single statements — Oracle has no multi-statement
/// batch), supports CRUD, and is structurally correct (tables/PKs). The strict full-contract
/// index/column check runs against the hand-written DDL.</summary>
[Collection(OracleCollection.Name)]
public sealed class GeneratedDdlIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public GeneratedDdlIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InquiryGeneratedSchemaStandsUpAndRoundTripsCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, InquiryGeneratedSchema.Ddl, "gends");

        // Oracle does not support result-set RETURNING (ReturnEntity = true degrades to an INQ039 stub),
        // so the generated key is read back via SelectAll rather than InsertReturning.
        var categories = harness.GetRequiredService<CategoryStore>();
        await categories.InsertAsync(new Category { CategoryName = "Beverages" });
        var inserted = (await categories.SelectAllAsync().ToListAsync())
            .Single(c => c.CategoryName == "Beverages");
        Assert.True(inserted.CategoryID!.Value > 0);
        Assert.NotNull(await categories.SelectByKeyAsync(inserted.CategoryID));

        await using var conn = new OracleConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new OracleSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertStructure(ExpectedNorthwindSchema.Schema, actual);
    }
}
