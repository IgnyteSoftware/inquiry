using Inquiry.FeatureCatalog;
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

/// <summary>
/// Repro tests for bugs #48 (read: GetFieldValue&lt;T&gt; with unsigned/sbyte type arg) and
/// #49 (write: DbType.UInt32 / UInt16 / UInt64 / SByte set on DbParameter).
///
/// The fix binds the REINTERPRETED same-width SIGNED value, which always fits the signed column:
///   sbyte -1            → byte 255             → TINYINT (0-255)
///   ushort 40000        → short -25536         → SMALLINT
///   uint 3_000_000_000  → int -1_294_967_296   → INT
///   ulong.MaxValue      → long -1              → BIGINT
/// The materializer reverses the cast on read, so high/negative values round-trip EXACTLY.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class UnsignedTypesIntegrationTests
{
    private readonly MySqlContainerFixture _fixture;

    public UnsignedTypesIntegrationTests(MySqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    // ---------------------------------------------------------------------------
    // Control cases — plain signed types that must work.
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task Byte_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyByte");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.ByteVal = 200;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal((byte)200, loaded!.ByteVal);
    }

    [SkippableFact]
    public async Task Int16_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyI16");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.Int16Val = short.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(short.MaxValue, loaded!.Int16Val);
    }

    [SkippableFact]
    public async Task Int32_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyI32");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.Int32Val = int.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(int.MaxValue, loaded!.Int32Val);
    }

    [SkippableFact]
    public async Task Int64_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyI64");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.Int64Val = long.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(long.MaxValue, loaded!.Int64Val);
    }

    [SkippableFact]
    public async Task EnumInt32_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyEI32");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumInt32Val = SampleEnumInt32.MaxSigned;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumInt32.MaxSigned, loaded!.EnumInt32Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: sbyte. Low positive, plus negative edge values (-1, MinValue).
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task SByte_Low_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMySB2");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.SByteVal = 42;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal((sbyte)42, loaded!.SByteVal);
    }

    [SkippableFact]
    public async Task SByte_NegativeOne_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMySB3");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // sbyte -1 → unchecked((byte)-1) = 255 → TINYINT; reads back GetByte=255 → (sbyte)=-1.
        var item = MakeItem(1); item.SByteVal = -1;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal((sbyte)-1, loaded!.SByteVal);
    }

    [SkippableFact]
    public async Task SByte_MinValue_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMySB4");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // sbyte.MinValue (-128) → unchecked((byte)) = 128 → TINYINT; reads back to -128.
        var item = MakeItem(1); item.SByteVal = sbyte.MinValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(sbyte.MinValue, loaded!.SByteVal);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: ushort. Low, plus edge values above short.MaxValue.
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task UInt16_Low_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyU162");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt16Val = 1000;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal((ushort)1000, loaded!.UInt16Val);
    }

    [SkippableFact]
    public async Task UInt16_AboveShortMax_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyU163");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // 40000 > short.MaxValue (32767); stored as signed -25536 in SMALLINT, reads back to 40000.
        var item = MakeItem(1); item.UInt16Val = 40000;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal((ushort)40000, loaded!.UInt16Val);
    }

    [SkippableFact]
    public async Task UInt16_MaxValue_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyU164");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // ushort.MaxValue (65535) → short -1 → SMALLINT; reads back to 65535.
        var item = MakeItem(1); item.UInt16Val = ushort.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(ushort.MaxValue, loaded!.UInt16Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: uint. Low, plus edge values above int.MaxValue.
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task UInt32_Low_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyU322");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt32Val = 1000u;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(1000u, loaded!.UInt32Val);
    }

    [SkippableFact]
    public async Task UInt32_AboveIntMax_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyU323");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // 3_000_000_000 > int.MaxValue (2_147_483_647); stored as -1_294_967_296 in INT, reads back exactly.
        var item = MakeItem(1); item.UInt32Val = 3_000_000_000u;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(3_000_000_000u, loaded!.UInt32Val);
    }

    [SkippableFact]
    public async Task UInt32_MaxValue_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyU324");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // uint.MaxValue → int -1 → INT; reads back to uint.MaxValue.
        var item = MakeItem(1); item.UInt32Val = uint.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(uint.MaxValue, loaded!.UInt32Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: ulong. Low, plus edge value above long.MaxValue.
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task UInt64_Low_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyU642");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt64Val = 1000ul;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(1000ul, loaded!.UInt64Val);
    }

    [SkippableFact]
    public async Task UInt64_AboveLongMax_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyU643");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // ulong.MaxValue > long.MaxValue; stored as long -1 in BIGINT, reads back to ulong.MaxValue.
        var item = MakeItem(1); item.UInt64Val = ulong.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(ulong.MaxValue, loaded!.UInt64Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: enum with sbyte underlying — negative member round-trips.
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task EnumSByte_Negative_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyESBn");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // SampleEnumSByte.Negative = -1 → byte 255 → TINYINT; reads back to the enum member.
        var item = MakeItem(1); item.EnumSByteVal = SampleEnumSByte.Negative;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumSByte.Negative, loaded!.EnumSByteVal);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: enum with ushort underlying — member above short.MaxValue round-trips.
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task EnumUInt16_AboveShortMax_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyEU16a");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // SampleEnumUInt16.AboveShortMax = 40000 (> 32767) → short -25536 → SMALLINT; round-trips.
        var item = MakeItem(1); item.EnumUInt16Val = SampleEnumUInt16.AboveShortMax;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumUInt16.AboveShortMax, loaded!.EnumUInt16Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: enum with uint underlying — member above int.MaxValue round-trips.
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task EnumUInt32_AboveIntMax_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyEU32a");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // SampleEnumUInt32.AboveIntMax = 3_000_000_000 (> int.MaxValue) → int -1_294_967_296 → INT.
        var item = MakeItem(1); item.EnumUInt32Val = SampleEnumUInt32.AboveIntMax;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumUInt32.AboveIntMax, loaded!.EnumUInt32Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: enum with ulong underlying — member above long.MaxValue round-trips.
    // ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task EnumUInt64_AboveLongMax_RoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, FeatureSchema.UnsignedTypesMySqlDdl, "UnsignedMyEU64a");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // SampleEnumUInt64.AboveLongMax = 1.8e19 (> long.MaxValue) → negative long → BIGINT; round-trips.
        var item = MakeItem(1); item.EnumUInt64Val = SampleEnumUInt64.AboveLongMax;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumUInt64.AboveLongMax, loaded!.EnumUInt64Val);
    }

    // ---------------------------------------------------------------------------
    // Helper — all non-tested columns set to safe zero values.
    // ---------------------------------------------------------------------------

    private static UnsignedTypesItem MakeItem(int id) => new()
    {
        Id = id,
        ByteVal = 0,
        Int16Val = 0,
        Int32Val = 0,
        Int64Val = 0,
        SByteVal = 0,
        UInt16Val = 0,
        UInt32Val = 0,
        UInt64Val = 0,
        EnumInt32Val = SampleEnumInt32.Zero,
        EnumSByteVal = SampleEnumSByte.Zero,
        EnumUInt16Val = SampleEnumUInt16.Zero,
        EnumUInt32Val = SampleEnumUInt32.Zero,
        EnumUInt64Val = SampleEnumUInt64.Zero,
    };
}
