using Inquiry.Entities;
using Inquiry.SqlServer.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

[InquiryTable("BulkGuardChild")]
public sealed class BulkGuardChild
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn]
    public int ParentId { get; set; }

    [InquiryColumn]
    public int Quantity { get; set; }

    [InquiryColumn]
    public string? Note { get; set; }
}

public partial class BulkGuardChildStore : InquiryStore<BulkGuardChild>
{
    [InquiryBulkInsert]
    public partial Task<long> BulkInsertAsync(IEnumerable<BulkGuardChild> rows, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<BulkGuardChild?> GetAsync(int id, CancellationToken cancellationToken = default);

    [InquiryCount]
    public partial Task<long> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A SQL Server bulk insert follows the rules of an ordinary <c>INSERT</c>: it enforces
/// <c>FOREIGN KEY</c> and <c>CHECK</c> constraints and leaves them trusted, stores <c>NULL</c> as
/// <c>NULL</c> on a column that has a default, and fires insert triggers.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class BulkInsertIntegrityIntegrationTests
{
    // CREATE TRIGGER must be alone in its batch, so it runs through EXEC.
    private const string Ddl = """
        CREATE TABLE BulkGuardParent (Id INT NOT NULL PRIMARY KEY);
        CREATE TABLE BulkGuardChild (
            Id INT NOT NULL PRIMARY KEY,
            ParentId INT NOT NULL CONSTRAINT FK_BulkGuardChild_Parent REFERENCES BulkGuardParent (Id),
            Quantity INT NOT NULL CONSTRAINT CK_BulkGuardChild_Quantity CHECK (Quantity > 0),
            Note NVARCHAR(20) NULL CONSTRAINT DF_BulkGuardChild_Note DEFAULT N'default');
        CREATE TABLE BulkGuardAudit (ChildId INT NOT NULL);
        EXEC('CREATE TRIGGER TR_BulkGuardChild_Insert ON BulkGuardChild AFTER INSERT AS INSERT INTO BulkGuardAudit (ChildId) SELECT Id FROM inserted;');
        INSERT INTO BulkGuardParent (Id) VALUES (1);
        """;

    private readonly SqlServerContainerFixture _fixture;

    public BulkInsertIntegrityIntegrationTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task BulkInsertRejectsRowsThatBreakAConstraintAndKeepsItTrusted()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "bulkguard");
        var store = harness.GetRequiredService<BulkGuardChildStore>();
        var inquiry = harness.GetRequiredService<IInquiry>();

        var danglingParent = await Assert.ThrowsAsync<SqlException>(
            () => store.BulkInsertAsync([new BulkGuardChild { Id = 1, ParentId = 999, Quantity = 1 }]));
        Assert.Equal(547, danglingParent.Number);
        var failedCheck = await Assert.ThrowsAsync<SqlException>(
            () => store.BulkInsertAsync([new BulkGuardChild { Id = 2, ParentId = 1, Quantity = -5 }]));
        Assert.Equal(547, failedCheck.Number);

        Assert.Equal(0, await store.CountAsync());
        Assert.Equal(1, await store.BulkInsertAsync([new BulkGuardChild { Id = 3, ParentId = 1, Quantity = 1 }]));
        Assert.Equal(0, await inquiry.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_BulkGuardChild_Parent' AND is_not_trusted = 1"));
        Assert.Equal(0, await inquiry.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_BulkGuardChild_Quantity' AND is_not_trusted = 1"));
    }

    [SkippableFact]
    public async Task BulkInsertStoresNullAsNullAndFiresInsertTriggers()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "bulkguardnull");
        var store = harness.GetRequiredService<BulkGuardChildStore>();
        var inquiry = harness.GetRequiredService<IInquiry>();

        Assert.Equal(2, await store.BulkInsertAsync(
        [
            new BulkGuardChild { Id = 1, ParentId = 1, Quantity = 1, Note = null },
            new BulkGuardChild { Id = 2, ParentId = 1, Quantity = 2, Note = "kept" },
        ]));

        Assert.Null((await store.GetAsync(1))!.Note);
        Assert.Equal("kept", (await store.GetAsync(2))!.Note);
        Assert.Equal(2, await inquiry.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM BulkGuardAudit"));
    }
}
