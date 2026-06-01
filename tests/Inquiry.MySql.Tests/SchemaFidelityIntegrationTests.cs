using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Inquiry.MySql.Tests.Fixtures;
using MySqlConnector;
using Xunit;

namespace Inquiry.MySql.Tests;

/// <summary>Asserts the hand-written MySQL Northwind schema built into the container matches the
/// canonical contract — every table, column nullability, PK, FK, and classic secondary index.</summary>
[Collection(MySqlCollection.Name)]
public sealed class SchemaFidelityIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public SchemaFidelityIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task HandWrittenNorthwindMatchesContract()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "fidelity");
        await using var conn = new MySqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new MySqlSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
