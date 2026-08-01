using System.Linq;
using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;
using Xunit;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class UnsignedKeyEagerIntegrationTests
{
    private const uint AboveIntMax = 3_000_000_000u; // > int.MaxValue (2_147_483_647)

    private readonly OracleContainerFixture _fixture;

    public UnsignedKeyEagerIntegrationTests(OracleContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task EagerLoadByUnsignedKey_AboveIntMax_RoundTripsWithChildren()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedKeyEagerOracleDdl, "UnsignedKeyEager");
        var parents = harness.GetRequiredService<UnsignedKeyParentStore>();
        var children = harness.GetRequiredService<UnsignedKeyChildStore>();

        await parents.InsertAsync(new UnsignedKeyParent { Id = AboveIntMax, Name = "Root" });
        await children.InsertAsync(new UnsignedKeyChild { Id = 4_000_000_000u, ParentId = AboveIntMax, Label = "A" });
        await children.InsertAsync(new UnsignedKeyChild { Id = 4_100_000_000u, ParentId = AboveIntMax, Label = "B" });

        var loaded = await parents.GetWithChildrenAsync(AboveIntMax);

        Assert.NotNull(loaded);
        Assert.Equal(AboveIntMax, loaded!.Id);
        Assert.Equal("Root", loaded.Name);
        Assert.Equal(2, loaded.Children.Count);
        Assert.Equal(new[] { "A", "B" }, loaded.Children.Select(c => c.Label).OrderBy(s => s));
        Assert.All(loaded.Children, c => Assert.Equal(AboveIntMax, c.ParentId));
    }
}
