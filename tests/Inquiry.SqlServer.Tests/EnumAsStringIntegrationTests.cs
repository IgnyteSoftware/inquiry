using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.SqlServer.Tests;

public enum TicketStatus
{
    Active,
    Closed,
}

[InquiryTable("Ticket")]
public sealed class Ticket
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

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
    public partial Task<Ticket?> GetAsync(int id, CancellationToken cancellationToken = default);

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

[Collection(SqlServerCollection.Name)]
public sealed class EnumAsStringIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public EnumAsStringIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = "CREATE TABLE [Ticket] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [Status] NVARCHAR(MAX) NOT NULL, [Prior] NVARCHAR(MAX) NULL);";

    [SkippableFact]
    public async Task StoresAndReadsEnumMemberNames()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed, Prior = TicketStatus.Active });

        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Equal(TicketStatus.Closed, loaded!.Status);
        Assert.Equal(TicketStatus.Active, loaded.Prior);
    }

    [SkippableFact]
    public async Task NullableEnumNullRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active, Prior = null });

        var loaded = await store.GetAsync(1);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.Prior);
    }

    [SkippableFact]
    public async Task FiltersByEnumValueAsString()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        var active = await store.ByStatusAsync(TicketStatus.Active);
        Assert.Equal(2, active.Count);
    }

    [SkippableFact]
    public async Task StreamingFilterBindsEnumAsString()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
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

    [SkippableFact]
    public async Task InPredicateFiltersEnumAsStringByMemberName()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        var active = await store.ByStatusesAsync(new[] { TicketStatus.Active });
        Assert.Equal(2, active.Count);
        Assert.All(active, t => Assert.Equal(TicketStatus.Active, t.Status));
    }

    [SkippableFact]
    public async Task NotInPredicateFiltersEnumAsStringByMemberName()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        var notActive = await store.ExcludeStatusesAsync(new[] { TicketStatus.Active });
        Assert.Single(notActive);
        Assert.Equal(TicketStatus.Closed, notActive[0].Status);
    }

    [SkippableFact]
    public async Task InPredicateWithEmptyCollectionReturnsNoRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });

        var result = await store.ByStatusesAsync(System.Array.Empty<TicketStatus>());
        Assert.Empty(result);
    }

    [SkippableFact]
    public async Task NotInPredicateWithEmptyCollectionReturnsAllRows()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });
        await store.InsertAsync(new Ticket { Status = TicketStatus.Closed });

        var result = await store.ExcludeStatusesAsync(System.Array.Empty<TicketStatus>());
        Assert.Equal(2, result.Count);
    }

    [SkippableFact]
    public async Task InPredicateWithNullCollectionReturnsNoRowsAndDoesNotThrow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "enumstr");
        var store = harness.GetRequiredService<TicketStore>();

        await store.InsertAsync(new Ticket { Status = TicketStatus.Active });

        var result = await store.ByStatusesAsync(null!);
        Assert.Empty(result);
    }
}
