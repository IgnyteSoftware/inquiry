using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.PostgreSql.Tests;

public readonly record struct PostgreSqlWallClock(DateTime Value);

public sealed class PostgreSqlWallClockConverter : IInquiryValueConverter<PostgreSqlWallClock, DateTime>
{
    public DateTime ToProvider(PostgreSqlWallClock model) => model.Value;
    public PostgreSqlWallClock FromProvider(DateTime provider) => new(provider);
}

[InquiryTable("PostgreSqlDateTimeItem")]
public sealed class PostgreSqlDateTimeItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public DateTime OccurredAt { get; set; }

    [InquiryColumn]
    public DateTime? OptionalAt { get; set; }

    [InquiryColumn(Converter = typeof(PostgreSqlWallClockConverter))]
    public PostgreSqlWallClock ConvertedAt { get; set; }

    [InquiryColumn]
    public DateTimeOffset OffsetAt { get; set; }
}

public partial class PostgreSqlDateTimeItemStore : InquiryStore<PostgreSqlDateTimeItem>
{
    [InquiryInsert(ReturnEntity = true)]
    public partial Task<PostgreSqlDateTimeItem?> InsertAsync(PostgreSqlDateTimeItem item, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(PostgreSqlDateTimeItem.OccurredAt), Compare.In)]
    public partial Task<IReadOnlyList<PostgreSqlDateTimeItem>> InOccurredAsync(IReadOnlyList<DateTime> values, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(PostgreSqlDateTimeItem.ConvertedAt), Compare.In)]
    public partial Task<IReadOnlyList<PostgreSqlDateTimeItem>> InConvertedAsync(IReadOnlyList<PostgreSqlWallClock> values, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
}

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlDateTimeBindingIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE "PostgreSqlDateTimeItem" (
            "Id" INTEGER NOT NULL PRIMARY KEY,
            "OccurredAt" TIMESTAMP NOT NULL,
            "OptionalAt" TIMESTAMP NULL,
            "ConvertedAt" TIMESTAMP NOT NULL,
            "OffsetAt" TIMESTAMPTZ NOT NULL
        );
        """;

    private readonly PostgreSqlContainerFixture _fixture;

    public PostgreSqlDateTimeBindingIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task ScalarDateTimesPreserveTicksAndMaterializeAsUnspecifiedWhileZeroOffsetRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgdatetime");
        var store = harness.GetRequiredService<PostgreSqlDateTimeItemStore>();
        var ticks = new DateTime(2026, 7, 13, 12, 34, 56, DateTimeKind.Unspecified).AddTicks(1_234_560).Ticks;
        var values = new[]
        {
            new DateTime(ticks, DateTimeKind.Utc),
            new DateTime(ticks + 10, DateTimeKind.Local),
            new DateTime(ticks + 20, DateTimeKind.Unspecified),
        };

        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var expectedOffset = new DateTimeOffset(new DateTime(value.Ticks, DateTimeKind.Utc));
            var returned = await store.InsertAsync(new PostgreSqlDateTimeItem
            {
                Id = i + 1,
                OccurredAt = value,
                OptionalAt = i == 1 ? null : value,
                ConvertedAt = new PostgreSqlWallClock(value),
                OffsetAt = expectedOffset,
            });

            Assert.NotNull(returned);
            AssertWallClock(value, returned!.OccurredAt);
            if (i == 1)
            {
                Assert.Null(returned.OptionalAt);
            }
            else
            {
                Assert.True(returned.OptionalAt.HasValue);
                AssertWallClock(value, returned.OptionalAt.Value);
            }
            AssertWallClock(value, returned.ConvertedAt.Value);
            Assert.Equal(expectedOffset.UtcTicks, returned.OffsetAt.UtcTicks);
            Assert.Equal(TimeSpan.Zero, returned.OffsetAt.Offset);
        }
    }

    [SkippableFact]
    public async Task AnyCollectionsBridgeDirectAndConverterBackedUtcAndLocalDateTimes()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgdatetimeany");
        var store = harness.GetRequiredService<PostgreSqlDateTimeItemStore>();
        var baseTicks = new DateTime(2026, 7, 13, 13, 0, 0, DateTimeKind.Unspecified).Ticks;
        var utc = new DateTime(baseTicks, DateTimeKind.Utc);
        var local = new DateTime(baseTicks + 10, DateTimeKind.Local);

        await store.InsertAsync(NewItem(1, utc));
        await store.InsertAsync(NewItem(2, local));
        await store.InsertAsync(NewItem(3, new DateTime(baseTicks + 20, DateTimeKind.Unspecified)));

        var direct = await store.InOccurredAsync(new[] { utc, local });
        Assert.Equal(new[] { 1, 2 }, direct.Select(static item => item.Id).OrderBy(static id => id));
        var converted = await store.InConvertedAsync(new[] { new PostgreSqlWallClock(utc), new PostgreSqlWallClock(local) });
        Assert.Equal(new[] { 1, 2 }, converted.Select(static item => item.Id).OrderBy(static id => id));
    }

    [SkippableFact]
    public async Task NonZeroDateTimeOffsetIsRejectedWithoutWriting()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "pgoffset");
        var store = harness.GetRequiredService<PostgreSqlDateTimeItemStore>();
        var wallClock = new DateTime(2026, 7, 13, 14, 0, 0, DateTimeKind.Unspecified);
        var item = NewItem(1, wallClock);
        item.OffsetAt = new DateTimeOffset(wallClock, TimeSpan.FromHours(5.5));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => store.InsertAsync(item));

        Assert.Contains("offset 0", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0L, await store.CountAsync());
    }

    private static PostgreSqlDateTimeItem NewItem(int id, DateTime value)
        => new()
        {
            Id = id,
            OccurredAt = value,
            OptionalAt = value,
            ConvertedAt = new PostgreSqlWallClock(value),
            OffsetAt = new DateTimeOffset(new DateTime(value.Ticks, DateTimeKind.Utc)),
        };

    private static void AssertWallClock(DateTime expected, DateTime actual)
    {
        Assert.Equal(expected.Ticks, actual.Ticks);
        Assert.Equal(DateTimeKind.Unspecified, actual.Kind);
    }
}
