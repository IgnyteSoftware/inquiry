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

public static class StatusRowSchema
{
    public const string SqlServerDdl = """
        CREATE TABLE [Status] (Id INT PRIMARY KEY, [Status] INT NOT NULL);
        """;
}
