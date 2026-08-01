using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Inquiry.Sqlite.Tests;

/// <summary>WS2/WS4 anchor: the hand-written SQLite Northwind DDL must produce a schema that
/// matches the canonical contract — including the full classic secondary-index set.</summary>
public sealed class NorthwindFidelityIntegrationTests
{
    [Fact]
    public async Task SqliteNorthwindMatchesExpectedContract()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Fidelity");
        await using var conn = new SqliteConnection(harness.ConnectionString);
        await conn.OpenAsync();

        var actual = await new SqliteSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
