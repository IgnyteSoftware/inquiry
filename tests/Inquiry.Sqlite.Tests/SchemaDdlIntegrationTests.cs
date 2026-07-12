using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Generated;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.Sqlite;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("SchemaWidget")]
public sealed class SchemaWidget
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Name")]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn("Weight")]
    public double Weight { get; set; }

    [InquiryColumn("Notes")]
    public string? Notes { get; set; }
}

public partial class SchemaWidgetStore : InquiryStore<SchemaWidget>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SchemaWidget widget, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SchemaWidget>> AllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Round-trip: the generated <see cref="InquiryGeneratedSchema.Ddl"/> for this assembly executes
/// against a fresh SQLite database (proving every generated CREATE TABLE is valid SQLite), then a store
/// performs a real insert/select round-trip against the generated table.
/// </summary>
public sealed class SchemaDdlIntegrationTests
{
    [Fact]
    public async Task GeneratedSchemaExecutesAndRoundTripsCrud()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(InquiryGeneratedSchema.Ddl, "GenSchema");
        var store = harness.GetRequiredService<SchemaWidgetStore>();

        await store.InsertAsync(new SchemaWidget { Name = "Gadget", Weight = 2.5, Notes = null });
        await store.InsertAsync(new SchemaWidget { Name = "Gizmo", Weight = 4.0, Notes = "spare" });

        var all = await store.AllAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, w => w.Name == "Gadget" && w.Weight == 2.5 && w.Notes == null);
        Assert.Contains(all, w => w.Name == "Gizmo" && w.Notes == "spare");
    }

    [Fact]
    public async Task GeneratedSchemaSupportsCyclicAndSelfReferencingForeignKeys()
    {
        var ddl = CyclicForeignKeyDdl.Extract(InquiryGeneratedSchema.Ddl);
        Assert.DoesNotContain("ALTER TABLE \"Cyclic", ddl);
        Assert.Contains("REFERENCES \"CyclicAlpha\"", ddl);
        Assert.Contains("REFERENCES \"CyclicBeta\"", ddl);

        await using var harness = await SqliteTestHarness.CreateAsync(ddl, "GenCycle");
        await using var connection = new SqliteConnection(harness.ConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, BetaId, ParentId) VALUES (1, NULL, NULL)");
        await ExecuteAsync(connection, "INSERT INTO CyclicBeta (Id, AlphaId) VALUES (1, 1)");
        await ExecuteAsync(connection, "UPDATE CyclicAlpha SET BetaId = 1, ParentId = 1 WHERE Id = 1");

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, BetaId) VALUES (2, 999)"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO CyclicBeta (Id, AlphaId) VALUES (2, 999)"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO CyclicAlpha (Id, ParentId) VALUES (3, 999)"));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
