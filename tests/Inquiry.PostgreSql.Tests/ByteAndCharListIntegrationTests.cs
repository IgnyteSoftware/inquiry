using Inquiry.Entities;
using Inquiry.PostgreSql.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.PostgreSql.Tests;

public enum SmallListStatus : byte
{
    Open = 1,
    Closed = 200,
}

[InquiryTable("SmallListItem")]
public sealed class SmallListItem
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public byte Level { get; set; }

    [InquiryColumn]
    public SmallListStatus Status { get; set; }

    [InquiryColumn]
    public char Grade { get; set; }
}

public partial class SmallListItemStore : InquiryStore<SmallListItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(SmallListItem item, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(SmallListItem.Level), Compare.In)]
    public partial Task<IReadOnlyList<SmallListItem>> ByLevelsAsync(IReadOnlyList<byte> levels, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(SmallListItem.Status), Compare.In)]
    public partial Task<IReadOnlyList<SmallListItem>> ByStatusesAsync(IReadOnlyList<SmallListStatus> statuses, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate, InquiryWhere(nameof(SmallListItem.Grade), Compare.In)]
    public partial Task<IReadOnlyList<SmallListItem>> ByGradesAsync(IReadOnlyList<char> grades, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>Compare.In</c> over <c>byte</c>, byte-backed enum and <c>char</c> columns. Npgsql sends a
/// <c>byte[]</c> as <c>bytea</c> and a <c>char[]</c> as <c>text</c>, which <c>= ANY</c> rejects, so
/// the list must bind as <c>smallint[]</c> and <c>text[]</c>.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class ByteAndCharListIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public ByteAndCharListIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InListsOverByteByteEnumAndCharColumnsMatchRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        const string ddl = "CREATE TABLE \"SmallListItem\" (\"Id\" INTEGER PRIMARY KEY, \"Level\" SMALLINT NOT NULL, \"Status\" SMALLINT NOT NULL, \"Grade\" CHAR(1) NOT NULL);";
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, ddl, "smalllists");
        var store = harness.GetRequiredService<SmallListItemStore>();
        await store.InsertAsync(new() { Id = 1, Level = 200, Status = SmallListStatus.Closed, Grade = 'B' });
        await store.InsertAsync(new() { Id = 2, Level = 3, Status = SmallListStatus.Open, Grade = 'A' });

        Assert.Equal(1, Assert.Single(await store.ByLevelsAsync([200, 9])).Id);
        Assert.Equal(1, Assert.Single(await store.ByStatusesAsync([SmallListStatus.Closed])).Id);
        Assert.Equal(1, Assert.Single(await store.ByGradesAsync(['B', 'Z'])).Id);
    }
}
