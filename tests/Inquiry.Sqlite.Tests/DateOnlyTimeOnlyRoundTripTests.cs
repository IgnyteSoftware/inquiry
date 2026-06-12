using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

public sealed class DateOnlyTimeOnlyRoundTripTests
{
    [Fact]
    public async Task DateOnlyAndTimeOnlyValuesRoundTrip()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.ScheduleItem, "ScheduleItem");
        var store = harness.GetRequiredService<ScheduleItemStore>();
        var item = new ScheduleItem
        {
            EventDate = new DateOnly(2026, 6, 12),
            StartTime = new TimeOnly(9, 30, 15),
            EndDate = new DateOnly(2026, 6, 13),
            EndTime = new TimeOnly(17, 45, 30, 123),
        };

        var inserted = await store.InsertReturningAsync(item);
        var all = await store.SelectAllAsync();
        var fetched = Assert.Single(all);

        Assert.NotNull(inserted);
        Assert.Equal(item.EventDate, fetched.EventDate);
        Assert.Equal(item.StartTime, fetched.StartTime);
        Assert.Equal(item.EndDate, fetched.EndDate);
        Assert.Equal(item.EndTime, fetched.EndTime);
    }

    [Fact]
    public async Task NullableDateOnlyAndTimeOnlyRoundTripAsNull()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.ScheduleItem, "ScheduleItem");
        var store = harness.GetRequiredService<ScheduleItemStore>();
        var item = new ScheduleItem
        {
            EventDate = new DateOnly(2026, 1, 1),
            StartTime = new TimeOnly(0, 0),
            EndDate = null,
            EndTime = null,
        };

        await store.InsertReturningAsync(item);
        var all = await store.SelectAllAsync();
        var fetched = Assert.Single(all);

        Assert.Null(fetched.EndDate);
        Assert.Null(fetched.EndTime);
    }
}
