using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Generated;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

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
}
