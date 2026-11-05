using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog.LockingRegression;

/// <summary>
/// Row-locking regression fixture (#539) compiled by every server provider test project. The live
/// scenarios read <see cref="LockProbeSummary"/> projections with and without a lock while another
/// transaction writes the row.
/// </summary>
[InquiryTable("LockProbe")]
public sealed class LockProbe
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryColumn("State")]
    public int State { get; set; }

    [InquiryColumn("Attempts")]
    public int Attempts { get; set; }
}

[InquiryProjection(typeof(LockProbe))]
public sealed record LockProbeSummary
{
    [InquiryColumn("Id")]
    public long Id { get; init; }

    [InquiryColumn("State")]
    public int State { get; init; }
}

[InquiryProjection(typeof(LockProbe))]
public sealed record LockProbeState
{
    [InquiryColumn("State")]
    public int State { get; init; }
}

public partial class LockProbeStore : InquiryStore<LockProbe>
{
    [InquiryInsert]
    public partial Task<LockProbe?> InsertAsync(LockProbe probe, CancellationToken cancellationToken = default);

    [InquirySelectOneByKey]
    public partial Task<LockProbe?> GetAsync(long id, CancellationToken cancellationToken = default);

    [InquiryUpdate]
    public partial Task<bool> UpdateAsync(LockProbe probe, CancellationToken cancellationToken = default);

    [InquirySelectAll(LockMode = InquiryLockMode.Update)]
    public partial Task<IReadOnlyList<LockProbeSummary>> SummariesForUpdateAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField("State", LockMode = InquiryLockMode.Update)]
    public partial Task<IReadOnlyList<LockProbeSummary>> SummariesByStateForUpdateAsync(int state, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<LockProbeSummary>> SummariesAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField("State")]
    public partial Task<IReadOnlyList<LockProbeSummary>> SummariesByStateAsync(int state, CancellationToken cancellationToken = default);
}

/// <summary>Per-dialect DDL for the <see cref="LockProbe"/> table.</summary>
public static class LockProbeSchema
{
    public const string PostgreSqlDdl = """
        CREATE TABLE "LockProbe" ("Id" BIGINT PRIMARY KEY, "State" INTEGER NOT NULL, "Attempts" INTEGER NOT NULL);
        """;

    public const string SqlServerDdl = """
        CREATE TABLE LockProbe (Id BIGINT PRIMARY KEY, State INT NOT NULL, Attempts INT NOT NULL);
        """;

    public const string MySqlDdl = """
        CREATE TABLE LockProbe (Id BIGINT PRIMARY KEY, State INT NOT NULL, Attempts INT NOT NULL);
        """;

    public const string OracleDdl = """
        CREATE TABLE LockProbe (Id NUMBER(19) PRIMARY KEY, State NUMBER(10) NOT NULL, Attempts NUMBER(10) NOT NULL)
        """;
}
