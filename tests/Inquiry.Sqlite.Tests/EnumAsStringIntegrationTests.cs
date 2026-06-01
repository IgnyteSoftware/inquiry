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
}

/// <summary>W10 enum-as-string end-to-end against SQLite: the column stores the member name as text,
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
}
