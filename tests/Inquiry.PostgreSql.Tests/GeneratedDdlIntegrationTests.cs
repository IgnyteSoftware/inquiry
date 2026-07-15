using System.Threading.Tasks;
using Inquiry.Generated;
using Inquiry.FeatureCatalog;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

/// <summary>Verifies Inquiry's own generated DDL (InquiryGeneratedSchema.Ddl) stands up a working
/// schema on real PostgreSQL: it executes, supports CRUD, and is structurally correct
/// (tables/PKs/FKs). The strict full-contract index/column check runs against the hand-written DDL.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class GeneratedDdlIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public GeneratedDdlIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InquiryGeneratedSchemaStandsUpAndRoundTripsCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, InquiryGeneratedSchema.Ddl, "gends");

        var categories = harness.GetRequiredService<CategoryStore>();
        var inserted = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(inserted);
        Assert.True(inserted!.CategoryID > 0);
        Assert.NotNull(await categories.SelectByKeyAsync(inserted.CategoryID));

        await using var conn = new NpgsqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new PostgreSqlSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertStructure(ExpectedNorthwindSchema.Schema, actual);
    }

    [SkippableFact]
    public async Task GeneratedSchemaSupportsDeferredCyclicAndInlineSelfForeignKeys()
    {
        var ddl = CyclicForeignKeyDdl.Extract(InquiryGeneratedSchema.Ddl);
        Assert.Contains("ALTER TABLE \"CyclicAlpha\" ADD CONSTRAINT \"FK_", ddl);
        Assert.Contains("ALTER TABLE \"CyclicBeta\" ADD CONSTRAINT \"FK_", ddl);
        Assert.Contains("REFERENCES \"CyclicAlpha\"(\"Id\")", ddl);
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "gencycle");
        await using var connection = new NpgsqlConnection(harness.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO \"CyclicAlpha\" (\"Id\", \"BetaId\", \"ParentId\") VALUES (1, NULL, NULL)");
        await ExecuteAsync(connection, "INSERT INTO \"CyclicBeta\" (\"Id\", \"AlphaId\") VALUES (1, 1)");
        await ExecuteAsync(connection, "UPDATE \"CyclicAlpha\" SET \"BetaId\" = 1, \"ParentId\" = 1 WHERE \"Id\" = 1");
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO \"CyclicAlpha\" (\"Id\", \"BetaId\") VALUES (2, 999)"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO \"CyclicBeta\" (\"Id\", \"AlphaId\") VALUES (2, 999)"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "INSERT INTO \"CyclicAlpha\" (\"Id\", \"ParentId\") VALUES (3, 999)"));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
