using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog.LockingRegression;

/// <summary>
/// SQL Server table-hint placement fixture (#540): the <c>Status</c> table has a <c>Status</c> column,
/// which used to receive the hint. Compiled only by the SQL Server test project.
/// </summary>
[InquiryTable("Status")]
public sealed class StatusRow
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn("Status")]
    public int Code { get; set; }
}

[InquiryProjection(typeof(StatusRow))]
public sealed record StatusCode
{
    [InquiryColumn("Status")]
    public int Code { get; init; }
}

public partial class StatusRowStore : InquiryStore<StatusRow>
{
    [InquiryInsert]
    public partial Task<StatusRow?> InsertAsync(StatusRow row, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(StatusRow row, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
    public partial Task<StatusRow?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAll(LockMode = InquiryLockMode.Share)]
    public partial Task<IReadOnlyList<StatusRow>> AllForShareAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Code", LockMode = InquiryLockMode.UpdateNoWait)]
    public partial Task<IReadOnlyList<StatusRow>> ByCodeNoWaitAsync(int code, CancellationToken cancellationToken = default);

    [InquirySelectAllByPredicate(LockMode = InquiryLockMode.UpdateSkipLocked)]
    [InquiryWhere("Code")]
    public partial Task<IReadOnlyList<StatusRow>> WhereCodeSkipLockedAsync(int code, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Code", LockMode = InquiryLockMode.Update)]
    public partial Task<IReadOnlyList<StatusCode>> CodesForUpdateAsync(int code, CancellationToken cancellationToken = default);
}

/// <summary>The same table name and column name in a non-default schema.</summary>
[InquiryTable("Status", Schema = "lockops")]
public sealed class OpsStatusRow
{
    [InquiryKey]
    public int Id { get; set; }

    [InquiryColumn("Status")]
    public int Code { get; set; }
}

public partial class OpsStatusRowStore : InquiryStore<OpsStatusRow>
{
    [InquiryInsert]
    public partial Task<OpsStatusRow?> InsertAsync(OpsStatusRow row, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey(LockMode = InquiryLockMode.Update)]
    public partial Task<OpsStatusRow?> GetForUpdateAsync(int id, CancellationToken cancellationToken = default);

    [InquirySelectAllByField("Code", LockMode = InquiryLockMode.UpdateSkipLocked)]
    public partial Task<IReadOnlyList<OpsStatusRow>> ByCodeSkipLockedAsync(int code, CancellationToken cancellationToken = default);
}

public static class StatusRowSchema
{
    // CREATE SCHEMA must start its own batch, so it runs through EXEC.
    public const string SqlServerDdl = """
        CREATE TABLE [Status] (Id INT PRIMARY KEY, [Status] INT NOT NULL);
        EXEC('CREATE SCHEMA lockops');
        CREATE TABLE lockops.[Status] (Id INT PRIMARY KEY, [Status] INT NOT NULL);
        """;
}
