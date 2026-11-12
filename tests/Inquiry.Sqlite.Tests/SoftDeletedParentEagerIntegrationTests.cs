using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("SoftOwner")]
public sealed class SoftOwner
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}

[InquiryTable("SoftTicket")]
public sealed class SoftTicket
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryColumn]
    public long OwnerId { get; set; }

    [InquiryColumn, InquirySoftDelete]
    public bool IsDeleted { get; set; }

    [InquiryRelation(nameof(OwnerId))]
    public SoftOwner? Owner { get; set; }
}

public partial class SoftTicketStore : InquiryStore<SoftTicket>
{
    [InquirySelectOneByKeyEager(IncludeDeleted = true)]
    public partial Task<SoftTicket?> GetWithDeletedAsync(long id, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<SoftTicket?> GetAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>
/// A by-key eager select with <c>IncludeDeleted = true</c> returns a soft-deleted parent. Its to-one
/// reference is found through a subquery on the parent row, so that subquery must also see the
/// soft-deleted parent.
/// </summary>
public sealed class SoftDeletedParentEagerIntegrationTests
{
    private const string Ddl = """
        CREATE TABLE SoftOwner (Id INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL);
        CREATE TABLE SoftTicket (Id INTEGER NOT NULL PRIMARY KEY, OwnerId INTEGER NOT NULL, IsDeleted INTEGER NOT NULL);
        INSERT INTO SoftOwner (Id, Name) VALUES (7, 'Ada');
        INSERT INTO SoftTicket (Id, OwnerId, IsDeleted) VALUES (1, 7, 1), (2, 7, 0);
        """;

    [Fact]
    public async Task IncludeDeletedLoadsTheReferenceOfASoftDeletedParent()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "SoftDeletedParentEager");
        var store = harness.GetRequiredService<SoftTicketStore>();

        var deleted = await store.GetWithDeletedAsync(1);

        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        Assert.Equal("Ada", deleted.Owner?.Name);

        // The default method still hides the soft-deleted parent and loads the reference of a live one.
        Assert.Null(await store.GetAsync(1));
        Assert.Equal("Ada", (await store.GetAsync(2))?.Owner?.Name);
    }
}
