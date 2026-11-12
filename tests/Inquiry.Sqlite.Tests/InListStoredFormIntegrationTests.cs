using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("StoredFormItem")]
public sealed class StoredFormItem
{
    [InquiryKey]
    public Guid Id { get; set; }

    [InquiryColumn]
    public DateTime At { get; set; }

    [InquiryColumn]
    public DateTimeOffset AtOffset { get; set; }

    [InquiryColumn]
    public TimeOnly Time { get; set; }

    [InquiryColumn]
    public byte[] Hash { get; set; } = [];

    [InquiryColumn]
    public float Ratio { get; set; }
}

public partial class StoredFormItemStore : InquiryStore<StoredFormItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(StoredFormItem item, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(StoredFormItem.Id), Compare.In)]
    public partial Task<IReadOnlyList<StoredFormItem>> ByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(StoredFormItem.At), Compare.In)]
    public partial Task<IReadOnlyList<StoredFormItem>> ByTimestampsAsync(IReadOnlyList<DateTime> timestamps, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(StoredFormItem.AtOffset), Compare.In)]
    public partial Task<IReadOnlyList<StoredFormItem>> ByOffsetsAsync(IReadOnlyList<DateTimeOffset?> offsets, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(StoredFormItem.Time), Compare.In)]
    public partial Task<IReadOnlyList<StoredFormItem>> ByTimesAsync(IReadOnlyList<TimeOnly> times, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(StoredFormItem.Hash), Compare.In)]
    public partial Task<IReadOnlyList<StoredFormItem>> ByHashesAsync(IReadOnlyList<byte[]> hashes, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(StoredFormItem.Ratio), Compare.In)]
    public partial Task<IReadOnlyList<StoredFormItem>> ByRatiosAsync(IReadOnlyList<float> ratios, CancellationToken cancellationToken = default);

    [InquiryDelete, InquiryWhere(nameof(StoredFormItem.Id), Compare.In)]
    public partial Task<int> DeleteAllAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>Compare.In</c> over the types that Microsoft.Data.Sqlite stores in its own text or number form:
/// upper-case GUIDs, <c>yyyy-MM-dd HH:mm:ss</c> timestamps, times, BLOBs and floats widened to double.
/// The list compares against the stored value with no conversion, so each element must take that form.
/// </summary>
public sealed class InListStoredFormIntegrationTests
{
    private const string Ddl = "CREATE TABLE StoredFormItem (Id TEXT NOT NULL PRIMARY KEY, At TEXT NOT NULL, AtOffset TEXT NOT NULL, Time TEXT NOT NULL, Hash BLOB NOT NULL, Ratio REAL NOT NULL);";

    [Fact]
    public async Task InListsMatchTheRowsThatScalarParametersStored()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "InListStoredForm");
        var store = harness.GetRequiredService<StoredFormItemStore>();
        var first = new StoredFormItem
        {
            Id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            At = new DateTime(2026, 7, 10, 12, 34, 56, 123, DateTimeKind.Utc),
            AtOffset = new DateTimeOffset(2026, 7, 10, 12, 34, 56, TimeSpan.FromHours(2)),
            Time = new TimeOnly(12, 34, 56, 123),
            Hash = [0, 1, 254, 255],
            Ratio = 1.1f,
        };
        var second = new StoredFormItem
        {
            Id = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"),
            At = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
            AtOffset = new DateTimeOffset(2026, 1, 2, 0, 0, 0, 500, TimeSpan.Zero),
            Time = new TimeOnly(8, 0),
            Hash = [9],
            Ratio = 3f,
        };
        await store.InsertAsync(first);
        await store.InsertAsync(second);

        Assert.Equal(first.Id, Assert.Single(await store.ByIdsAsync([first.Id, Guid.NewGuid()])).Id);
        Assert.Equal(first.Id, Assert.Single(await store.ByTimestampsAsync([first.At, DateTime.UnixEpoch])).Id);
        Assert.Equal(first.Id, Assert.Single(await store.ByOffsetsAsync([null, first.AtOffset])).Id);
        Assert.Equal(first.Id, Assert.Single(await store.ByTimesAsync([first.Time, TimeOnly.MaxValue])).Id);
        Assert.Equal(first.Id, Assert.Single(await store.ByHashesAsync([first.Hash, [7, 7]])).Id);
        Assert.Equal(first.Id, Assert.Single(await store.ByRatiosAsync([first.Ratio, 99f])).Id);

        // Whole seconds and midnight take the driver's short forms.
        Assert.Equal(second.Id, Assert.Single(await store.ByTimestampsAsync([second.At])).Id);
        Assert.Equal(second.Id, Assert.Single(await store.ByTimesAsync([second.Time])).Id);

        Assert.Equal(2, await store.DeleteAllAsync([first.Id, second.Id]));
    }
}
