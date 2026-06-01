using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace Inquiry.Oracle.Tests;

/// <summary>Asserts the hand-written Oracle Northwind schema built into the container matches the
/// canonical contract — every table, column nullability, PK, FK, and classic secondary index.</summary>
[Collection(OracleCollection.Name)]
public sealed class SchemaFidelityIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public SchemaFidelityIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task HandWrittenNorthwindMatchesContract()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "fidelity");
        await using var conn = new OracleConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new OracleSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
