using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("KindEvent")]
public sealed class KindEvent
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn, InquiryDateTimeKind(DateTimeKind.Utc)]
    public DateTime OccurredAt { get; set; }

    [InquiryColumn, InquiryDateTimeKind(DateTimeKind.Utc)]
    public DateTime? ClosedAt { get; set; }

    [InquiryColumn]
    public DateTime Plain { get; set; }
}

[InquiryProjection(typeof(KindEvent))]
public sealed class KindEventSummary
{
    [InquiryColumn]
    public int Id { get; set; }

    [InquiryColumn, InquiryDateTimeKind(DateTimeKind.Utc)]
    public DateTime? ClosedAt { get; set; }
}

[InquiryAdHoc]
public sealed class KindEventRow
{
    [InquiryDateTimeKind(DateTimeKind.Utc)]
    public DateTime OccurredAt { get; set; }

    [InquiryDateTimeKind(DateTimeKind.Utc)]
    public DateTime? ClosedAt { get; set; }

    public DateTime Plain { get; set; }
}

public partial class KindEventStore : InquiryStore<KindEvent>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(KindEvent entity, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<KindEvent?> GetAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "Id")]
    public partial Task<IReadOnlyList<KindEventSummary>> ListSummariesAsync(CancellationToken cancellationToken = default);
}

/// <summary>#613: a column that declares a DateTimeKind reads with that kind and unchanged ticks.</summary>
public sealed class DateTimeKindIntegrationTests
{
    private const string Ddl = "CREATE TABLE KindEvent (Id INTEGER PRIMARY KEY, OccurredAt TEXT NOT NULL, ClosedAt TEXT NULL, Plain TEXT NOT NULL);";

    private static readonly DateTime _occurred = new(2026, 3, 4, 5, 6, 7, 890, DateTimeKind.Utc);
    private static readonly DateTime _closed = new(2026, 3, 5, 1, 2, 3, DateTimeKind.Utc);
    private static readonly DateTime _plainValue = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

    private static async Task<SqliteTestHarness> SeedAsync()
    {
        var harness = await SqliteTestHarness.CreateAsync(Ddl, "DateTimeKind");
        var store = harness.GetRequiredService<KindEventStore>();
        await store.InsertAsync(new KindEvent { Id = 1, OccurredAt = _occurred, ClosedAt = _closed, Plain = _plainValue });
        await store.InsertAsync(new KindEvent { Id = 2, OccurredAt = _occurred, ClosedAt = null, Plain = _plainValue });
        return harness;
    }

    private static void AssertUtc(DateTime expected, DateTime? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(DateTimeKind.Utc, actual.Value.Kind);
        Assert.Equal(expected.Ticks, actual.Value.Ticks);
    }

    private static void AssertPlain(DateTime actual)
    {
        Assert.Equal(DateTimeKind.Unspecified, actual.Kind);
        Assert.Equal(_plainValue.Ticks, actual.Ticks);
    }

    [Fact]
    public async Task GeneratedStoreReadAppliesTheDeclaredKind()
    {
        await using var harness = await SeedAsync();
        var store = harness.GetRequiredService<KindEventStore>();

        var first = await store.GetAsync(1);
        Assert.NotNull(first);
        AssertUtc(_occurred, first.OccurredAt);
        AssertUtc(_closed, first.ClosedAt);
        AssertPlain(first.Plain);

        var second = await store.GetAsync(2);
        Assert.NotNull(second);
        Assert.Null(second.ClosedAt);
    }

    [Fact]
    public async Task ProjectionReadAppliesTheDeclaredKind()
    {
        await using var harness = await SeedAsync();
        var store = harness.GetRequiredService<KindEventStore>();

        var rows = await store.ListSummariesAsync();

        Assert.Equal(2, rows.Count);
        AssertUtc(_closed, rows[0].ClosedAt);
        Assert.Null(rows[1].ClosedAt);
    }

    [Fact]
    public async Task HandwrittenSqlReadsApplyTheDeclaredKind()
    {
        await using var harness = await SeedAsync();
        var inquiry = harness.GetRequiredService<IInquiry>();

        // Reordered columns exercise the name-mapped path that reads through resolved ordinals.
        var entities = await inquiry.QueryListAsync<KindEvent>($"SELECT Plain, ClosedAt, OccurredAt, Id FROM KindEvent ORDER BY Id");
        AssertUtc(_occurred, entities[0].OccurredAt);
        AssertUtc(_closed, entities[0].ClosedAt);
        AssertPlain(entities[0].Plain);
        Assert.Null(entities[1].ClosedAt);

        var rows = await inquiry.QueryListAsync<KindEventRow>($"SELECT OccurredAt, ClosedAt, Plain FROM KindEvent ORDER BY Id");
        AssertUtc(_occurred, rows[0].OccurredAt);
        AssertUtc(_closed, rows[0].ClosedAt);
        AssertPlain(rows[0].Plain);
        Assert.Null(rows[1].ClosedAt);

        var summaries = await inquiry.QueryListAsync<KindEventSummary>($"SELECT ClosedAt, Id FROM KindEvent ORDER BY Id");
        AssertUtc(_closed, summaries[0].ClosedAt);
    }
}
