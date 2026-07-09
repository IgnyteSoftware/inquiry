using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Inquiry.MariaDb.Tests.Fixtures;
using MySqlConnector;
using Xunit;

namespace Inquiry.MariaDb.Tests;

/// <summary>Asserts the hand-written MariaDB Northwind schema built into the container matches the
/// canonical contract — every table, column nullability, PK, FK, and classic secondary index.</summary>
[Collection(MariaDbCollection.Name)]
public sealed class SchemaFidelityIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public SchemaFidelityIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task HandWrittenNorthwindMatchesContract()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateAsync(_fixture.AdminConnectionString, "fidelity");
        await using var conn = new MySqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new MariaDbSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
