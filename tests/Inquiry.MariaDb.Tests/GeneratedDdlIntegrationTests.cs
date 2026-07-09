using System.Threading.Tasks;
using Inquiry.Generated;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.MariaDb.Tests.Fixtures;
using MySqlConnector;
using Xunit;

namespace Inquiry.MariaDb.Tests;

/// <summary>Verifies Inquiry's own generated DDL (InquiryGeneratedSchema.Ddl) stands up a working
/// schema on real MariaDB: it executes, supports CRUD, and is structurally correct
/// (tables/PKs/FKs). The strict full-contract index/column check runs against the hand-written DDL.</summary>
[Collection(MariaDbCollection.Name)]
public sealed class GeneratedDdlIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public GeneratedDdlIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InquiryGeneratedSchemaStandsUpAndRoundTripsCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, InquiryGeneratedSchema.Ddl, "gends");

        var categories = harness.GetRequiredService<CategoryStore>();
        var inserted = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(inserted);
        Assert.True(inserted!.CategoryID > 0);
        Assert.NotNull(await categories.SelectByKeyAsync(inserted.CategoryID));

        await using var conn = new MySqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new MariaDbSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertStructure(ExpectedNorthwindSchema.Schema, actual);
    }
}
