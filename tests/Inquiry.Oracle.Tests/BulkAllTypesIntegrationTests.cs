using System;
using Inquiry.FeatureCatalog;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Single-row all-types bulk-insert round-trip (#134): verifies every provider-primitive category
/// survives the batch-INSERT fallback path (<c>INSERT ALL ... SELECT 1 FROM dual</c>) on Oracle.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class BulkAllTypesIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public BulkAllTypesIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SingleRowAllTypesRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.BulkAllTypesOracleDdl, "bulkalltypes");
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
