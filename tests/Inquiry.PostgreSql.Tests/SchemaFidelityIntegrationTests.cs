using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Inquiry.PostgreSql.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

/// <summary>Asserts the hand-written PostgreSQL Northwind schema built into the container matches the
/// canonical contract — every table, column nullability, PK, FK, and classic secondary index.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class SchemaFidelityIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public SchemaFidelityIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task HandWrittenNorthwindMatchesContract()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "fidelity");
        await using var conn = new NpgsqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new PostgreSqlSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
