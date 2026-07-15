using System;
using System.Threading.Tasks;
using Xunit;

namespace Inquiry.FeatureCatalog;

/// <summary>Provider-neutral exact-value acceptance contract for issue #134.</summary>
public static class BulkAllTypesCases
{
    public static async Task RunAsync(BulkAllTypesItemStore store)
    {
        Assert.Equal(0L, await store.BulkInsertAsync(Array.Empty<BulkAllTypesItem>()));
        Assert.Equal(0L, await store.CountAsync());

        var expected = Create();
        Assert.Equal(1L, await store.BulkInsertAsync(new[] { expected[0] }));
        Assert.Equal(2L, await store.BulkInsertAsync(new[] { expected[1], expected[2] }));
        Assert.Equal(expected.LongLength, await store.CountAsync());

        foreach (var item in expected)
        {
            var actual = await store.GetAsync(item.Id);
            Assert.NotNull(actual);
            AssertEqual(item, actual!);
        }
    }

    public static BulkAllTypesItem[] Create()
        => new[]
        {
            new BulkAllTypesItem
            {
                Id = 1,
                IntVal = int.MinValue,
                DecimalVal = -999999999999.25m,
                BoolVal = false,
                GuidVal = Guid.Empty,
                DateTimeVal = new DateTime(1000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                // Oracle stores an empty VARCHAR2 as NULL, so "A" is the portable exact minimum.
                StringVal = "A",
                NullableStringVal = null,
                // Oracle binds an empty BLOB value as NULL, so one byte is the portable exact minimum.
                BinaryVal = new byte[] { 0x00 },
                EnumVal = BulkColor.Red,
                ConvertedVal = new Money { Amount = -999999999999.25m },
            },
            new BulkAllTypesItem
            {
                Id = 2,
                IntVal = 0,
                DecimalVal = 0m,
                BoolVal = true,
                GuidVal = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                DateTimeVal = new DateTime(2026, 7, 10, 12, 30, 45, DateTimeKind.Unspecified).AddTicks(1_234_560),
                StringVal = "middle",
                NullableStringVal = "nullable",
                BinaryVal = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE },
                EnumVal = BulkColor.Green,
                ConvertedVal = new Money { Amount = 0m },
            },
            new BulkAllTypesItem
            {
                Id = 3,
                IntVal = int.MaxValue,
                DecimalVal = 999999999999.25m,
                BoolVal = true,
                GuidVal = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                DateTimeVal = new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Unspecified).AddTicks(9_999_990),
                StringVal = new string('Z', 200),
                NullableStringVal = new string('N', 200),
                BinaryVal = CreateBinaryBoundary(),
                EnumVal = BulkColor.Blue,
                ConvertedVal = new Money { Amount = 999999999999.25m },
            },
        };

    private static byte[] CreateBinaryBoundary()
    {
        var value = new byte[4096];
        for (var i = 0; i < value.Length; i++)
        {
            value[i] = unchecked((byte)i);
        }

        return value;
    }

    private static void AssertEqual(BulkAllTypesItem expected, BulkAllTypesItem actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.IntVal, actual.IntVal);
        Assert.Equal(expected.DecimalVal, actual.DecimalVal);
        Assert.Equal(expected.BoolVal, actual.BoolVal);
        Assert.Equal(expected.GuidVal, actual.GuidVal);
        Assert.Equal(expected.DateTimeVal, actual.DateTimeVal);
        Assert.Equal(expected.StringVal, actual.StringVal);
        Assert.Equal(expected.NullableStringVal, actual.NullableStringVal);
        Assert.Equal(expected.BinaryVal, actual.BinaryVal);
        Assert.Equal(expected.EnumVal, actual.EnumVal);
        Assert.Equal(expected.ConvertedVal.Amount, actual.ConvertedVal.Amount);
    }
}
