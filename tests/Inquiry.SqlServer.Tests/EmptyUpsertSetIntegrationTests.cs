using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Entities;
using Inquiry.Interceptors;
using Inquiry.SqlServer.DependencyInjection;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("SsKeyOnlyItem")]
public sealed class SsKeyOnlyItem
{
    [InquiryKey(IsGenerated = true)]
    public int? Id { get; set; }
}

public partial class SsKeyOnlyItemStore : InquiryStore<SsKeyOnlyItem>
{
    [InquiryUpsert]
    public partial Task<int> UpsertAsync(SsKeyOnlyItem item, CancellationToken ct = default);

    [InquiryUpsert]
    public partial Task<SsKeyOnlyItem?> UpsertReturningAsync(SsKeyOnlyItem item, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SsKeyOnlyItem>> AllAsync(CancellationToken ct = default);
}

[InquiryTable("SsAuditOnlyItem")]
public sealed class SsAuditOnlyItem
{
    [InquiryKey(IsGenerated = true)]
    public int? Id { get; set; }

    [InquiryCreatedAt]
    public System.DateTime CreatedAt { get; set; }
}

public partial class SsAuditOnlyItemStore : InquiryStore<SsAuditOnlyItem>
{
    [InquiryUpsert]
    public partial Task<SsAuditOnlyItem?> UpsertReturningAsync(SsAuditOnlyItem item, CancellationToken ct = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<SsAuditOnlyItem>> AllAsync(CancellationToken ct = default);
}

[InquiryTable("SsFailingAuditOnlyItem")]
public sealed class SsFailingAuditOnlyItem
{
    [InquiryKey(IsGenerated = true)]
    public int? Id { get; set; }

    [InquiryCreatedAt]
    public System.DateTime CreatedAt { get; set; }
}

public partial class SsFailingAuditOnlyItemStore : InquiryStore<SsFailingAuditOnlyItem>
{
    [InquiryUpsert]
    public partial Task<SsFailingAuditOnlyItem?> UpsertReturningAsync(SsFailingAuditOnlyItem item, CancellationToken ct = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken ct = default);
}

internal sealed class OwnedUpsertCleanupProbe : IInquiryCommandInterceptor
{
    public bool Verified { get; private set; }

    public async ValueTask CommandFailedAsync(InquiryCommandFailedContext context, CancellationToken cancellationToken = default)
    {
        if (context.Exception is not SqlException { Number: 547 }
            || !context.Command.CommandText.Contains("[SsFailingAuditOnlyItem]", System.StringComparison.Ordinal))
        {
            return;
        }

        await using var probe = context.Command.Connection!.CreateCommand();
        probe.CommandText =
            "IF XACT_STATE() <> 0 THROW 51005, 'Owned transaction was not fully rolled back', 1; " +
            "IF @@TRANCOUNT <> 0 THROW 51006, 'Owned transaction count leaked', 1; " +
            "SET IDENTITY_INSERT [SsIdentityProbe] ON; SET IDENTITY_INSERT [SsIdentityProbe] OFF;";
        await probe.ExecuteNonQueryAsync(cancellationToken);
        Verified = true;
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class EmptyUpsertSetIntegrationTests
{
    private readonly SqlServerContainerFixture _fixture;
    public EmptyUpsertSetIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TABLE [SsKeyOnlyItem] ([Id] INT IDENTITY(1,1) PRIMARY KEY);
        CREATE TABLE [SsAuditOnlyItem] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [CreatedAt] DATETIME2 NOT NULL);
        CREATE TABLE [SsFailingAuditOnlyItem] ([Id] INT IDENTITY(1,1) PRIMARY KEY, [CreatedAt] DATETIME2 NOT NULL, CONSTRAINT [CK_SsFailingAuditOnlyItem_CreatedAt] CHECK ([CreatedAt] >= '2000-01-01'));
        CREATE TABLE [SsIdentityProbe] ([Id] INT IDENTITY(1,1) PRIMARY KEY);
        """;

    [SkippableFact]
    public async Task KeyOnlyEntityUpsertEmitsValidSqlAndConflictIsNoOp()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "keyonly");
        var store = harness.GetRequiredService<SsKeyOnlyItemStore>();

        Assert.Equal(1, await store.UpsertAsync(new SsKeyOnlyItem { Id = null }));
        var id = Assert.Single(await store.AllAsync()).Id;
        Assert.NotNull(id);

        await store.UpsertAsync(new SsKeyOnlyItem { Id = id });
        Assert.Single(await store.AllAsync());

        var conflict = await store.UpsertReturningAsync(new SsKeyOnlyItem { Id = id });
        Assert.NotNull(conflict);
        Assert.Equal(id, conflict!.Id);
        Assert.Single(await store.AllAsync());

        var inserted = await store.UpsertReturningAsync(new SsKeyOnlyItem { Id = null });
        Assert.NotNull(inserted);
        Assert.NotNull(inserted!.Id);
        Assert.Equal(2, (await store.AllAsync()).Count);
    }

    [SkippableFact]
    public async Task ConcurrentSameExplicitKeyUpsertsAllReturnOneExistingRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "keyonlyrace");
        var store = harness.GetRequiredService<SsKeyOnlyItemStore>();

        var returned = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => store.UpsertReturningAsync(new SsKeyOnlyItem { Id = 42 })));

        Assert.All(returned, item => Assert.Equal(42, Assert.IsType<SsKeyOnlyItem>(item).Id));
        var rows = await store.AllAsync();
        Assert.Single(rows);
        Assert.Equal(42, rows[0].Id);
    }

    [SkippableFact]
    public async Task MultiColumnConflictReturnsStoredProjectionInEntityOrder()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "auditonly");
        var store = harness.GetRequiredService<SsAuditOnlyItemStore>();
        var storedAt = new System.DateTime(2020, 2, 3, 4, 5, 6, System.DateTimeKind.Utc);
        var inserted = await store.UpsertReturningAsync(new SsAuditOnlyItem { Id = 73, CreatedAt = storedAt });
        var conflictingAt = new System.DateTime(2025, 6, 7, 8, 9, 10, System.DateTimeKind.Utc);

        var conflict = await store.UpsertReturningAsync(new SsAuditOnlyItem { Id = 73, CreatedAt = conflictingAt });

        Assert.NotNull(inserted);
        Assert.NotNull(conflict);
        Assert.Equal(73, conflict!.Id);
        Assert.Equal(storedAt, conflict.CreatedAt);
        Assert.NotEqual(conflictingAt, conflict.CreatedAt);
        Assert.Single(await store.AllAsync());
    }

    [SkippableFact]
    public async Task OwnedTransactionFailurePreservesConstraintErrorAndIdentityStateRecovers()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "ownedfailure");
        var cleanupProbe = new OwnedUpsertCleanupProbe();
        await using var services = new ServiceCollection()
            .AddInquiry(typeof(SsFailingAuditOnlyItemStore).Assembly)
            .AddInquirySqlServer(harness.ConnectionString)
            .AddSingleton<IInquiryCommandInterceptor>(cleanupProbe)
            .BuildServiceProvider();
        var store = services.GetRequiredService<SsFailingAuditOnlyItemStore>();

        var exception = await Assert.ThrowsAsync<SqlException>(() => store.UpsertReturningAsync(new SsFailingAuditOnlyItem
        {
            Id = 91,
            CreatedAt = new System.DateTime(1900, 1, 1),
        }));

        Assert.Equal(547, exception.Number);
        Assert.True(cleanupProbe.Verified);
        var recovered = await store.UpsertReturningAsync(new SsFailingAuditOnlyItem
        {
            CreatedAt = new System.DateTime(2024, 1, 1),
        });
        Assert.NotNull(recovered);
        Assert.Equal(1L, await store.CountAsync());
    }

    [SkippableFact]
    public async Task CallerTransactionCommittableFailureRollsBackOnlyOperationSavepoint()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "savepointfailure");
        var store = harness.GetRequiredService<SsFailingAuditOnlyItemStore>();
        var inquiry = harness.GetRequiredService<global::Inquiry.IInquiry>();
        await using var transaction = await inquiry.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<SqlException>(() => store.UpsertReturningAsync(new SsFailingAuditOnlyItem
        {
            Id = 92,
            CreatedAt = new System.DateTime(1900, 1, 1),
        }));
        Assert.Equal(547, exception.Number);
        await transaction.ExecuteAsync(
            $"IF XACT_STATE() <> 1 THROW 51001, 'Expected committable caller transaction', 1; IF @@TRANCOUNT <> 1 THROW 51002, 'Expected exactly one caller transaction', 1; SET IDENTITY_INSERT [SsIdentityProbe] ON; SET IDENTITY_INSERT [SsIdentityProbe] OFF;");

        var recovered = await store.UpsertReturningAsync(new SsFailingAuditOnlyItem
        {
            CreatedAt = new System.DateTime(2024, 2, 1),
        });
        Assert.NotNull(recovered);
        Assert.Equal(1L, await store.CountAsync());
        await transaction.RollbackAsync();
    }

}
