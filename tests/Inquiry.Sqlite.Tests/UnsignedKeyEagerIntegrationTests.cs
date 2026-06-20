using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Eager-load sibling of bug #49: a uint KEY above int.MaxValue. The eager-load emitter binds the
/// key through new InquiryParameter(name, rawUintValue, DbType.Int32). Before the runtime binder
/// reinterpreted the value, this threw OverflowException on a checked Convert.ToInt32(uint).
/// </summary>
public sealed class UnsignedKeyEagerIntegrationTests
{
    private const uint AboveIntMax = 3_000_000_000u; // > int.MaxValue (2_147_483_647)

    [Fact]
    public async Task EagerLoadByUnsignedKey_AboveIntMax_RoundTripsWithChildren()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(
            FeatureSchema.UnsignedKeyEagerSqliteDdl, "UnsignedKeyEager");
        var parents = harness.GetRequiredService<UnsignedKeyParentStore>();
        var children = harness.GetRequiredService<UnsignedKeyChildStore>();

        await parents.InsertAsync(new UnsignedKeyParent { Id = AboveIntMax, Name = "Root" });
        await children.InsertAsync(new UnsignedKeyChild { Id = 4_000_000_000u, ParentId = AboveIntMax, Label = "A" });
        await children.InsertAsync(new UnsignedKeyChild { Id = 4_100_000_000u, ParentId = AboveIntMax, Label = "B" });

        // Eager-load by the above-int.MaxValue key — the bind path that previously overflowed.
        var loaded = await parents.GetWithChildrenAsync(AboveIntMax);

        Assert.NotNull(loaded);
        Assert.Equal(AboveIntMax, loaded!.Id);
        Assert.Equal("Root", loaded.Name);
        Assert.Equal(2, loaded.Children.Count);
        Assert.Equal(new[] { "A", "B" }, loaded.Children.Select(c => c.Label).OrderBy(s => s));
        // The child FK (also a uint > int.MaxValue) round-trips too.
        Assert.All(loaded.Children, c => Assert.Equal(AboveIntMax, c.ParentId));
    }
}
