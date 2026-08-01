using System.Collections.Generic;
using Inquiry.IntegrationTesting;
using Xunit;

namespace Inquiry.Tests;

public sealed class SchemaFidelityTests
{
    private static SchemaSnapshot OneTable(params IndexSnapshot[] indexes) => new(new[]
    {
        new TableSnapshot("Categories",
            new[] { new ColumnSnapshot("CategoryID", false), new ColumnSnapshot("CategoryName", false) },
            new[] { "CategoryID" }, new ForeignKeySnapshot[0], indexes),
    });

    [Fact]
    public void IdenticalSchemasMatch()
    {
        var s = OneTable(new IndexSnapshot(new[] { "CategoryName" }));
        SchemaFidelity.AssertMatches(s, s); // does not throw
    }

    [Fact]
    public void CaseInsensitiveIdentifiersMatch()
    {
        var expected = OneTable(new IndexSnapshot(new[] { "CategoryName" }));
        var actual = new SchemaSnapshot(new[]
        {
            new TableSnapshot("CATEGORIES",
                new[] { new ColumnSnapshot("CATEGORYID", false), new ColumnSnapshot("CATEGORYNAME", false) },
                new[] { "CATEGORYID" }, new ForeignKeySnapshot[0],
                new[] { new IndexSnapshot(new[] { "CATEGORYNAME" }) }),
        });
        SchemaFidelity.AssertMatches(expected, actual); // does not throw
    }

    [Fact]
    public void MissingIndexThrows()
    {
        var expected = OneTable(new IndexSnapshot(new[] { "CategoryName" }));
        var actual = OneTable(); // no secondary index
        var ex = Assert.Throws<SchemaFidelityException>(() => SchemaFidelity.AssertMatches(expected, actual));
        Assert.Contains("CategoryName", ex.Message);
    }

    [Fact]
    public void NullabilityMismatchThrows()
    {
        var expected = OneTable();
        var actual = new SchemaSnapshot(new[]
        {
            new TableSnapshot("Categories",
                new[] { new ColumnSnapshot("CategoryID", false), new ColumnSnapshot("CategoryName", true) },
                new[] { "CategoryID" }, new ForeignKeySnapshot[0], new IndexSnapshot[0]),
        });
        Assert.Throws<SchemaFidelityException>(() => SchemaFidelity.AssertMatches(expected, actual));
    }

    [Fact]
    public void CompositePkIndexSatisfiesLeadingColumnExpectation()
    {
        var expected = OneTable(new IndexSnapshot(new[] { "CategoryID" }));
        var actual = new SchemaSnapshot(new[]
        {
            new TableSnapshot("Categories",
                new[] { new ColumnSnapshot("CategoryID", false), new ColumnSnapshot("CategoryName", false) },
                new[] { "CategoryID" }, new ForeignKeySnapshot[0],
                new[] { new IndexSnapshot(new[] { "CategoryID", "CategoryName" }) }), // composite leads with CategoryID
        });
        SchemaFidelity.AssertMatches(expected, actual); // prefix match -> ok
    }
}
