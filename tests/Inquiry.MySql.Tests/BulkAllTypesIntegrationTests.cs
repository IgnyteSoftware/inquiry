using System;
using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Single-row all-types bulk-insert round-trip (#134): verifies every provider-primitive category
/// survives the MySqlBulkCopy path on MySQL.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class BulkAllTypesIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;
    public BulkAllTypesIntegrationTests(MySqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SingleRowAllTypesRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.BulkAllTypesMySqlDdl, "bulkalltypes");
        var store = harness.GetRequiredService<BulkAllTypesItemStore>();

        var guid = Guid.NewGuid();
        var now = new DateTime(2026, 7, 10, 12, 30, 45, DateTimeKind.Unspecified);
        var binary = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };

        var row = new BulkAllTypesItem
        {
            Id = 1,
            IntVal = 42,
            DecimalVal = 123.45m,
            BoolVal = true,
            GuidVal = guid,
            DateTimeVal = now,
            StringVal = "hello",
            NullableStringVal = null,
            BinaryVal = binary,
            EnumVal = BulkColor.Blue,
            ConvertedVal = new Money { Amount = 99.99m },
        };

        var written = await store.BulkInsertAsync(new[] { row });
        Assert.Equal(1L, written);

        var fetched = await store.GetAsync(1);
        Assert.NotNull(fetched);
        Assert.Equal(42, fetched!.IntVal);
        Assert.Equal(123.45m, fetched.DecimalVal);
        Assert.True(fetched.BoolVal);
        Assert.Equal(guid, fetched.GuidVal);
        Assert.Equal(now, fetched.DateTimeVal);
        Assert.Equal("hello", fetched.StringVal);
        Assert.Null(fetched.NullableStringVal);
        Assert.Equal(binary, fetched.BinaryVal);
        Assert.Equal(BulkColor.Blue, fetched.EnumVal);
        Assert.Equal(99.99m, fetched.ConvertedVal.Amount);
    }
}
