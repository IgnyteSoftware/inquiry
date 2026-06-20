using Inquiry.FeatureCatalog;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Repro tests for bugs #48 (read: GetFieldValue&lt;T&gt; with unsigned/sbyte type arg) and
/// #49 (write: DbType.UInt32 / UInt16 / UInt64 / SByte set on SqliteParameter).
///
/// Each [Fact] inserts a row containing one problematic type value and reads it back,
/// isolating whether the failure is on INSERT or SELECT.
/// </summary>
public sealed class UnsignedTypesIntegrationTests
{
    // ---------------------------------------------------------------------------
    // Control cases — plain signed types that must work.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Byte_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteByte");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.ByteVal = 200;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal((byte)200, loaded!.ByteVal);
    }

    [Fact]
    public async Task Int16_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteI16");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.Int16Val = short.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(short.MaxValue, loaded!.Int16Val);
    }

    [Fact]
    public async Task Int32_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteI32");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.Int32Val = int.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(int.MaxValue, loaded!.Int32Val);
    }

    [Fact]
    public async Task Int64_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteI64");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.Int64Val = long.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(long.MaxValue, loaded!.Int64Val);
    }

    [Fact]
    public async Task EnumInt32_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteEI32");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumInt32Val = SampleEnumInt32.MaxSigned;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumInt32.MaxSigned, loaded!.EnumInt32Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: sbyte (maps to DbTypeClass.Byte → DbType.SByte on write;
    // GetFieldValue<sbyte> on read).
    // Low value (positive, within byte range) — isolates bug, not overflow.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SByte_Low_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteSB1");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // sbyte = 42: value is positive; triggers bug #49 (DbType.SByte).
        var item = MakeItem(1); item.SByteVal = 42;
        await store.InsertAsync(item); // bug #49 — expected to throw before fix
    }

    [Fact]
    public async Task SByte_Low_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteSB2");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.SByteVal = 42;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1); // bug #48 — expected to throw before fix
        Assert.NotNull(loaded);
        Assert.Equal((sbyte)42, loaded!.SByteVal);
    }

    [Fact]
    public async Task SByte_Negative_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteSB3");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // sbyte = -1; SQLite stores INTEGER as signed 64-bit so this can be stored.
        var item = MakeItem(1); item.SByteVal = -1;
        await store.InsertAsync(item);
    }

    [Fact]
    public async Task SByte_Negative_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteSB4");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.SByteVal = -1;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal((sbyte)-1, loaded!.SByteVal);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: ushort (DbTypeClass.Int16 → DbType.UInt16 / GetFieldValue<ushort>)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UInt16_Low_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU161");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt16Val = 1000;
        await store.InsertAsync(item); // bug #49
    }

    [Fact]
    public async Task UInt16_Low_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU162");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt16Val = 1000;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1); // bug #48
        Assert.NotNull(loaded);
        Assert.Equal((ushort)1000, loaded!.UInt16Val);
    }

    [Fact]
    public async Task UInt16_AboveShortMax_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU163");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // 40000 > short.MaxValue (32767); tests both bug #49 and storage-range.
        var item = MakeItem(1); item.UInt16Val = 40000;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal((ushort)40000, loaded!.UInt16Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: uint (DbTypeClass.Int32 → DbType.UInt32 / GetFieldValue<uint>)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UInt32_Low_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU321");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt32Val = 1000u;
        await store.InsertAsync(item); // bug #49
    }

    [Fact]
    public async Task UInt32_Low_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU322");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt32Val = 1000u;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1); // bug #48
        Assert.NotNull(loaded);
        Assert.Equal(1000u, loaded!.UInt32Val);
    }

    [Fact]
    public async Task UInt32_AboveIntMax_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU323");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // 3_000_000_000 > int.MaxValue (2_147_483_647).
        var item = MakeItem(1); item.UInt32Val = 3_000_000_000u;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(3_000_000_000u, loaded!.UInt32Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: ulong (DbTypeClass.Int64 → DbType.UInt64 / GetFieldValue<ulong>)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UInt64_Low_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU641");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt64Val = 1000ul;
        await store.InsertAsync(item); // bug #49
    }

    [Fact]
    public async Task UInt64_Low_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU642");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.UInt64Val = 1000ul;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1); // bug #48
        Assert.NotNull(loaded);
        Assert.Equal(1000ul, loaded!.UInt64Val);
    }

    [Fact]
    public async Task UInt64_AboveLongMax_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteU643");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // ulong.MaxValue: bit-reinterpret stored as long -1, recovered as ulong.MaxValue on read.
        var item = MakeItem(1); item.UInt64Val = ulong.MaxValue;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(ulong.MaxValue, loaded!.UInt64Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: enum with sbyte underlying
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task EnumSByte_Negative_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteESB1");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumSByteVal = SampleEnumSByte.Negative;
        await store.InsertAsync(item); // bug #49
    }

    [Fact]
    public async Task EnumSByte_Negative_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteESB2");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumSByteVal = SampleEnumSByte.Negative;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1); // bug #48
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumSByte.Negative, loaded!.EnumSByteVal);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: enum with ushort underlying
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task EnumUInt16_AboveShortMax_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteEU161");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumUInt16Val = SampleEnumUInt16.AboveShortMax;
        await store.InsertAsync(item); // bug #49
    }

    [Fact]
    public async Task EnumUInt16_AboveShortMax_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteEU162");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumUInt16Val = SampleEnumUInt16.AboveShortMax;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1); // bug #48
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumUInt16.AboveShortMax, loaded!.EnumUInt16Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: enum with uint underlying
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task EnumUInt32_AboveIntMax_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteEU321");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumUInt32Val = SampleEnumUInt32.AboveIntMax;
        await store.InsertAsync(item); // bug #49
    }

    [Fact]
    public async Task EnumUInt32_AboveIntMax_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteEU322");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumUInt32Val = SampleEnumUInt32.AboveIntMax;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1); // bug #48
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumUInt32.AboveIntMax, loaded!.EnumUInt32Val);
    }

    // ---------------------------------------------------------------------------
    // Bug #49 + #48: enum with ulong underlying
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task EnumUInt64_Large_Write_DoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteEU641");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumUInt64Val = SampleEnumUInt64.Large;
        await store.InsertAsync(item); // bug #49
    }

    [Fact]
    public async Task EnumUInt64_Large_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteEU642");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        var item = MakeItem(1); item.EnumUInt64Val = SampleEnumUInt64.Large;
        await store.InsertAsync(item);
        var loaded = await store.SelectByKeyAsync(1); // bug #48
        Assert.NotNull(loaded);
        Assert.Equal(SampleEnumUInt64.Large, loaded!.EnumUInt64Val);
    }

    [Fact]
    public async Task EnumUInt64_AboveLongMax_RoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(FeatureSchema.UnsignedTypesSqliteDdl, "UnsignedSqliteEU643");
        var store = harness.GetRequiredService<UnsignedTypesItemStore>();

        // AboveLongMax (> long.MaxValue) → negative long bit pattern → round-trips exactly.
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
