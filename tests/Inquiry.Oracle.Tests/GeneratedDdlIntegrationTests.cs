using System.Threading.Tasks;
using Inquiry.Generated;
using Inquiry.FeatureCatalog;
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

        var categories = harness.GetRequiredService<CategoryStore>();
        var inserted = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(inserted);
        Assert.True(inserted!.CategoryID > 0);
        Assert.NotNull(await categories.SelectByKeyAsync(inserted.CategoryID));

        await using var conn = new OracleConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new OracleSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertStructure(ExpectedNorthwindSchema.Schema, actual);
    }

    [SkippableFact]
    public async Task GeneratedSchemaSupportsDeferredCyclicAndInlineSelfForeignKeys()
    {
        var ddl = CyclicForeignKeyDdl.Extract(InquiryGeneratedSchema.Ddl);
        Assert.Contains("ALTER TABLE CyclicAlpha ADD CONSTRAINT FK_", ddl);
        Assert.Contains("ALTER TABLE CyclicBeta ADD CONSTRAINT FK_", ddl);
        Assert.Contains("REFERENCES CyclicAlpha(Id)", ddl);
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "gencycle");
        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, BetaId, ParentId) VALUES (1, NULL, NULL)");
        await ExecuteAsync(connection, "INSERT INTO CyclicBeta (Id, AlphaId) VALUES (1, 1)");
        await ExecuteAsync(connection, "UPDATE CyclicAlpha SET BetaId = 1, ParentId = 1 WHERE Id = 1");
        await Assert.ThrowsAsync<OracleException>(() => ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, BetaId) VALUES (2, 999)"));
        await Assert.ThrowsAsync<OracleException>(() => ExecuteAsync(connection, "INSERT INTO CyclicBeta (Id, AlphaId) VALUES (2, 999)"));
        await Assert.ThrowsAsync<OracleException>(() => ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, ParentId) VALUES (3, 999)"));
    }

    private static async Task ExecuteAsync(OracleConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
