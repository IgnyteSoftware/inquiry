using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

public enum TicketStatus
{
    Active,
    Closed,
}

[InquiryTable("Ticket")]
public sealed class Ticket
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Status"), InquiryEnumAsString]
    public TicketStatus Status { get; set; }

    [InquiryColumn("Prior"), InquiryEnumAsString]
    public TicketStatus? Prior { get; set; }
}

public partial class TicketStore : InquiryStore<Ticket>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Ticket ticket, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<Ticket?> GetAsync(long id, CancellationToken cancellationToken = default);

    [InquirySelectAllByField(nameof(Ticket.Status))]
    public partial Task<System.Collections.Generic.IReadOnlyList<Ticket>> ByStatusAsync(TicketStatus status, CancellationToken cancellationToken = default);

    [InquirySelectAllByField(nameof(Ticket.Status))]
    public partial System.Collections.Generic.IAsyncEnumerable<Ticket> StreamByStatusAsync(TicketStatus status, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(Ticket.Status), Compare.In)]
    public partial Task<System.Collections.Generic.IReadOnlyList<Ticket>> ByStatusesAsync(System.Collections.Generic.IReadOnlyList<TicketStatus> statuses, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate]
    [InquiryWhere(nameof(Ticket.Status), Compare.NotIn)]
    public partial Task<System.Collections.Generic.IReadOnlyList<Ticket>> ExcludeStatusesAsync(System.Collections.Generic.IReadOnlyList<TicketStatus> statuses, CancellationToken cancellationToken = default);
}

/// <summary>Enum-as-string end-to-end against SQLite: the column stores the member name as text,
/// round-trips through materialization, filters by enum value, and a null nullable-enum maps to NULL.</summary>
public sealed class EnumAsStringIntegrationTests
{
    private const string Ddl = "CREATE TABLE Ticket (Id INTEGER PRIMARY KEY AUTOINCREMENT, Status TEXT NOT NULL, Prior TEXT NULL);";

    [Fact]
    public async Task StoresAndReadsEnumMemberNames()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed, Prior = TicketStatus.Active });

        // The raw column holds the member name, not the underlying integer.
        var raw = await harness.ExecuteScalarAsync("SELECT Status FROM Ticket LIMIT 1");
        Assert.Equal("Closed", raw);

        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(TicketStatus.Closed, loaded!.Status);
        Assert.Equal(TicketStatus.Active, loaded.Prior);
    }

    [Fact]
    public async Task NullableEnumNullRoundTrips()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active, Prior = null });

        var rawPrior = await harness.ExecuteScalarAsync("SELECT Prior FROM Ticket LIMIT 1");
        Assert.Null(rawPrior);

        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.Prior);
    }

    [Fact]
    public async Task FiltersByEnumValueAsString()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        var active = await store.ByStatusAsync(TicketStatus.Active);
        Assert.Equal(2, active.Count);
    }

    [Fact]
    public async Task StreamingFilterBindsEnumAsString()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        var count = 0;
        await foreach (var ticket in store.StreamByStatusAsync(TicketStatus.Active))
        {
            Assert.Equal(TicketStatus.Active, ticket.Status);
            count++;
        }

        Assert.Equal(2, count);
    }

    /// <summary>Reproduces bug #50: IN over an enum-as-string column must bind member names, not integers.</summary>
    [Fact]
    public async Task InPredicateFiltersEnumAsStringByMemberName()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        // Without the fix, IN binds integers (0,1) against a TEXT column → 0 matches.
        var active = await store.ByStatusesAsync(new[] { TicketStatus.Active });
        Assert.Equal(2, active.Count);
        Assert.All(active, t => Assert.Equal(TicketStatus.Active, t.Status));
    }

    /// <summary>Reproduces bug #50 for NOT IN: must also bind member names, not integers.</summary>
    [Fact]
    public async Task NotInPredicateFiltersEnumAsStringByMemberName()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        // NOT IN([Active]) should return only Closed rows.
        var notActive = await store.ExcludeStatusesAsync(new[] { TicketStatus.Active });
        Assert.Single(notActive);
        Assert.Equal(TicketStatus.Closed, notActive[0].Status);
    }

    [Fact]
    public async Task InPredicateWithEmptyCollectionReturnsNoRows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });

        var result = await store.ByStatusesAsync(System.Array.Empty<TicketStatus>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task NotInPredicateWithEmptyCollectionReturnsAllRows()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });

        // Empty NOT IN is the match-all tautology.
        var result = await store.ExcludeStatusesAsync(System.Array.Empty<TicketStatus>());
        Assert.Equal(2, result.Count);
    }

    /// <summary>Locks fix #1: a null collection must flow through to the helper (treated as empty),
    /// not throw from Enumerable.Select(null, …).</summary>
    [Fact]
    public async Task InPredicateWithNullCollectionReturnsNoRowsAndDoesNotThrow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "EnumAsString");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        var result = await store.ByStatusesAsync(null!);
        Assert.Empty(result);
    }
}
