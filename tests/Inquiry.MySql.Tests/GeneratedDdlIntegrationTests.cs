using System.Threading.Tasks;
using Inquiry.Generated;
using Inquiry.FeatureCatalog;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.MySql.Tests.Fixtures;
using MySqlConnector;
using Xunit;

namespace Inquiry.MySql.Tests;

/// <summary>Verifies Inquiry's own generated DDL (InquiryGeneratedSchema.Ddl) stands up a working
/// schema on real MySQL: it executes, supports CRUD, and is structurally correct
/// (tables/PKs/FKs). The strict full-contract index/column check runs against the hand-written DDL.</summary>
[Collection(MySqlCollection.Name)]
public sealed class GeneratedDdlIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public GeneratedDdlIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InquiryGeneratedSchemaStandsUpAndRoundTripsCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, InquiryGeneratedSchema.Ddl, "gends");

        var categories = harness.GetRequiredService<CategoryStore>();
        var inserted = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(inserted);
        Assert.True(inserted!.CategoryID > 0);
        Assert.NotNull(await categories.SelectByKeyAsync(inserted.CategoryID));

        await using var conn = new MySqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new MySqlSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertStructure(ExpectedNorthwindSchema.Schema, actual);
    }

    [SkippableFact]
    public async Task GeneratedSchemaSupportsDeferredCyclicAndInlineSelfForeignKeys()
    {
        var ddl = CyclicForeignKeyDdl.Extract(InquiryGeneratedSchema.Ddl);
        Assert.Contains("ALTER TABLE `CyclicAlpha` ADD CONSTRAINT `FK_", ddl);
        Assert.Contains("ALTER TABLE `CyclicBeta` ADD CONSTRAINT `FK_", ddl);
        Assert.Contains("REFERENCES `CyclicAlpha`(`Id`)", ddl);
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "gencycle");
        await using var connection = new MySqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, BetaId, ParentId) VALUES (1, NULL, NULL)");
        await ExecuteAsync(connection, "INSERT INTO CyclicBeta (Id, AlphaId) VALUES (1, 1)");
        await ExecuteAsync(connection, "UPDATE CyclicAlpha SET BetaId = 1, ParentId = 1 WHERE Id = 1");
        await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, BetaId) VALUES (2, 999)"));
        await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(connection, "INSERT INTO CyclicBeta (Id, AlphaId) VALUES (2, 999)"));
        await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, ParentId) VALUES (3, 999)"));
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
