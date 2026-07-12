using Inquiry.FeatureCatalog;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class UnsignedCollectionIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public UnsignedCollectionIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task TvpsPreserveUnsignedBoundariesAcrossDirectConverterEnumAndDeleteAll()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        const string ddl = "CREATE TABLE [UnsignedCollectionItem] ([Id] INT PRIMARY KEY, [S8] TINYINT NOT NULL, [U16] SMALLINT NOT NULL, [U32] INT NOT NULL, [U64] BIGINT NOT NULL, [Code] INT NOT NULL, [State] INT NOT NULL); CREATE TABLE [UnsignedConverterKey] ([Id] INT PRIMARY KEY); CREATE TABLE [UnsignedEnumKey] ([Id] INT PRIMARY KEY);";
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "unsignedcollections");
        var store = harness.GetRequiredService<UnsignedCollectionItemStore>();
        await store.InsertAsync(new() { Id = uint.MaxValue, S8 = -1, U16 = ushort.MaxValue, U32 = 3_000_000_000u, U64 = ulong.MaxValue, Code = new(uint.MaxValue), State = UnsignedCollectionState.Max });
        Assert.Single(await store.ByS8Async(new sbyte[] { -1 }));
        Assert.Single(await store.ByU16Async(new[] { ushort.MaxValue }));
        Assert.Single(await store.ByU32Async(new[] { 3_000_000_000u }));
        Assert.Single(await store.ByU64Async(new[] { ulong.MaxValue }));
        Assert.Single(await store.ByCodeAsync(new[] { new UnsignedCollectionCode(uint.MaxValue) }));
        Assert.Single(await store.ByStateAsync(new[] { UnsignedCollectionState.Max }));
        Assert.Empty(await store.ByU32Async(System.Array.Empty<uint>()));
        Assert.Empty(await store.ByU32Async(null!));
        Assert.Single(await store.NotU32Async(System.Array.Empty<uint>()));
        Assert.Equal(1, await store.DeleteAllAsync(new[] { uint.MaxValue }));
        Assert.Equal(0, await store.DeleteAllAsync(null!));
        var converterKeys = harness.GetRequiredService<UnsignedConverterKeyStore>();
        await converterKeys.InsertAsync(new() { Id = new(uint.MaxValue) });
        Assert.Equal(1, await converterKeys.DeleteAllAsync(new[] { new UnsignedCollectionCode(uint.MaxValue) }));
        var enumKeys = harness.GetRequiredService<UnsignedEnumKeyStore>();
        await enumKeys.InsertAsync(new() { Id = UnsignedCollectionState.Max });
        Assert.Equal(1, await enumKeys.DeleteAllAsync(new[] { UnsignedCollectionState.Max }));
    }
}
