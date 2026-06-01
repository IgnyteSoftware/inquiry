using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Inquiry.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Inquiry.SqlServer.Tests;

/// <summary>Asserts the hand-written SQL Server Northwind schema built into the container matches the
/// canonical contract — every table, column nullability, PK, FK, and classic secondary index.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SchemaFidelityIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public SchemaFidelityIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task HandWrittenNorthwindMatchesContract()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "fidelity");
        await using var conn = new SqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new SqlServerSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
