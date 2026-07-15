using Inquiry.FeatureCatalog;
using Inquiry.PostgreSql.Tests.Fixtures;

namespace Inquiry.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class UnsignedCollectionIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public UnsignedCollectionIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task NativeArraysPreserveUnsignedBoundariesAcrossDirectConverterEnumAndDeleteAll()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        const string ddl = "CREATE TABLE \"UnsignedCollectionItem\" (\"Id\" INTEGER PRIMARY KEY, \"S8\" SMALLINT NOT NULL, \"U16\" SMALLINT NOT NULL, \"U32\" INTEGER NOT NULL, \"U64\" BIGINT NOT NULL, \"Code\" INTEGER NOT NULL, \"State\" INTEGER NOT NULL); CREATE TABLE \"UnsignedConverterKey\" (\"Id\" INTEGER PRIMARY KEY); CREATE TABLE \"UnsignedEnumKey\" (\"Id\" INTEGER PRIMARY KEY);";
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "unsignedcollections");
        await RunAsync(harness.GetRequiredService<UnsignedCollectionItemStore>());
        var converterKeys = harness.GetRequiredService<UnsignedConverterKeyStore>();
        await converterKeys.InsertAsync(new() { Id = new(uint.MaxValue) });
        Assert.Equal(1, await converterKeys.DeleteAllAsync(new[] { new UnsignedCollectionCode(uint.MaxValue) }));
        var enumKeys = harness.GetRequiredService<UnsignedEnumKeyStore>();
        await enumKeys.InsertAsync(new() { Id = UnsignedCollectionState.Max });
        Assert.Equal(1, await enumKeys.DeleteAllAsync(new[] { UnsignedCollectionState.Max }));
    }

    private static async Task RunAsync(UnsignedCollectionItemStore store)
    {
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
    }
}
