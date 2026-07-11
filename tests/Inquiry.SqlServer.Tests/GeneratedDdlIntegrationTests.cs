using System.Threading.Tasks;
using Inquiry.Generated;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Inquiry.SqlServer.Tests;

/// <summary>Verifies Inquiry's own generated DDL (InquiryGeneratedSchema.Ddl) stands up a working
/// schema on real SQL Server: it executes, supports CRUD, and is structurally correct
/// (tables/PKs/FKs). The strict full-contract index/column check runs against the hand-written DDL.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class GeneratedDdlIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public GeneratedDdlIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InquiryGeneratedSchemaStandsUpAndRoundTripsCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, InquiryGeneratedSchema.Ddl, "gends", provisionProviderArtifacts: false);

        var categories = harness.GetRequiredService<CategoryStore>();
        var inserted = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(inserted);
        Assert.True(inserted!.CategoryID > 0);
        Assert.NotNull(await categories.SelectByKeyAsync(inserted.CategoryID));

        await using var conn = new SqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        await using (var validation = conn.CreateCommand())
        {
            validation.CommandText = InquiryGeneratedSchema.ProviderArtifactsValidationSql;
            await using var reader = await validation.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync());
        }
        var actual = await new SqlServerSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertStructure(ExpectedNorthwindSchema.Schema, actual);
    }
}
